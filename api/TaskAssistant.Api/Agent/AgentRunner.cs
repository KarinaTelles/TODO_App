using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaskAssistant.Api.Data;

namespace TaskAssistant.Api.Agent;

public class PendingActionsOpenException() : Exception("Há ações aguardando confirmação nesta conversa.");

public class ConversationNotFoundException(Guid id) : Exception($"Conversa {id} não encontrada.");

// A tool_result already computed for the turn that waits on confirmations, or
// a reference to the PendingAction whose resolution will provide it.
public record HeldResult(string ToolUseId, AgentBlock? Result, Guid? ActionId);

// Manual tool loop. Each iteration asks the model for one turn, runs read
// tools, and turns write tools into PendingActions. If any write was proposed
// the loop stops and returns; it resumes when the last pending action of that
// turn is confirmed, rejected or expires.
public class AgentRunner(
    AppDbContext db,
    IChatModel model,
    ToolExecutor executor,
    Calendar calendar,
    TimeProvider clock,
    IOptions<AgentOptions> options)
{
    private readonly AgentOptions _options = options.Value;
    private IReadOnlyList<ToolDefinition> Tools => ToolCatalog.Build(_options.MaxBulkDelete);

    public async Task<ChatResponse> SendAsync(Guid? conversationId, string text, CancellationToken ct)
    {
        var conversation = conversationId is { } id
            ? await db.Conversations.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new ConversationNotFoundException(id)
            : NewConversation();

        var history = ReadHistory(conversation);
        var userBlocks = new List<AgentBlock>();

        if (conversation.HeldResultsJson is not null)
        {
            await ExpireOverdueAsync(conversation.Id, ct);
            if (await db.PendingActions.AnyAsync(a => a.ConversationId == conversation.Id && a.Status == ActionStatus.Pending, ct))
                throw new PendingActionsOpenException();
            // Every pending action expired: their results go first, then the new text.
            userBlocks.AddRange(await CollectHeldResultsAsync(conversation.HeldResultsJson, ct));
            conversation.HeldResultsJson = null;
        }

        userBlocks.Add(AgentBlock.FromText(text));
        history.Add(new AgentMessage(AgentMessage.User, userBlocks));
        return await RunLoopAsync(conversation, history, ct);
    }

    // Called after a pending action reaches a final state. Resumes only when no
    // other action of the same turn is still open.
    public async Task<ChatResponse?> ResumeIfReadyAsync(Guid conversationId, CancellationToken ct)
    {
        var conversation = await db.Conversations.FirstAsync(c => c.Id == conversationId, ct);
        if (conversation.HeldResultsJson is null) return null;

        var notReady = await db.PendingActions.AnyAsync(a =>
            a.ConversationId == conversationId && (a.Status == ActionStatus.Pending || a.ResultJson == null), ct);
        if (notReady) return null;

        // Claim the held results atomically so two confirmations finishing at
        // the same time do not both resume the conversation.
        var held = conversation.HeldResultsJson;
        var claimed = await db.Conversations
            .Where(c => c.Id == conversationId && c.HeldResultsJson == held)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.HeldResultsJson, (string?)null), ct);
        if (claimed == 0) return null;
        await db.Entry(conversation).ReloadAsync(ct);

        var results = await CollectHeldResultsAsync(held, ct);
        var history = ReadHistory(conversation);
        history.Add(new AgentMessage(AgentMessage.User, results));
        return await RunLoopAsync(conversation, history, ct);
    }

    public async Task ExpireOverdueAsync(Guid conversationId, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var overdue = await db.PendingActions
            .Where(a => a.ConversationId == conversationId && a.Status == ActionStatus.Pending && a.ExpiresAt <= now)
            .ToListAsync(ct);
        foreach (var action in overdue)
        {
            var changed = await db.PendingActions
                .Where(a => a.Id == action.Id && a.Status == ActionStatus.Pending)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(a => a.Status, ActionStatus.Expired)
                    .SetProperty(a => a.ResolvedAt, now)
                    .SetProperty(a => a.ResultJson, ActionService.ResultJson(action, ActionService.ExpiredPayload, true)), ct);
            if (changed > 0) await db.Entry(action).ReloadAsync(ct);
        }
    }

    private async Task<List<AgentBlock>> CollectHeldResultsAsync(string heldJson, CancellationToken ct)
    {
        var held = JsonSerializer.Deserialize<List<HeldResult>>(heldJson, AgentJson.Options) ?? [];
        var ids = held.Where(h => h.ActionId is not null).Select(h => h.ActionId!.Value).ToList();
        var actions = await db.PendingActions.AsNoTracking().Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);

        // Same order as the tool_use blocks of the assistant turn.
        return held.Select(h => h.Result
                                ?? JsonSerializer.Deserialize<AgentBlock>(actions[h.ActionId!.Value].ResultJson!, AgentJson.Options)!)
            .ToList();
    }

    private async Task<ChatResponse> RunLoopAsync(Conversation conversation, List<AgentMessage> history, CancellationToken ct)
    {
        var visible = new List<ChatMessageDto>();
        var steps = new List<ToolStepDto>();
        var proposed = new List<PendingAction>();

        for (var i = 0; i < _options.MaxIterations; i++)
        {
            var turn = await model.NextTurnAsync(SystemPrompt.Build(calendar.Today), Tools, history, ct);

            // Check why the model stopped before touching any tool call. A
            // refusal or a cut-off turn may carry an incomplete tool_use.
            if (turn.StopReason == StopReasons.Refusal)
            {
                visible.Add(new(AgentMessage.Assistant, "O modelo recusou este pedido. Nada foi alterado."));
                break;
            }
            if (turn.StopReason == StopReasons.MaxTokens)
            {
                visible.Add(new(AgentMessage.Assistant, "A resposta foi cortada pelo limite de tokens. Nada foi alterado; tente um pedido menor."));
                break;
            }

            history.Add(new AgentMessage(AgentMessage.Assistant, turn.Content));
            visible.AddRange(turn.Content
                .Where(b => b.Type == AgentBlock.TextType && !string.IsNullOrWhiteSpace(b.Text))
                .Select(b => new ChatMessageDto(AgentMessage.Assistant, b.Text!)));

            var toolUses = turn.Content.Where(b => b.Type == AgentBlock.ToolUseType).ToList();
            if (toolUses.Count == 0) break;

            var held = new List<HeldResult>();
            foreach (var call in toolUses)
            {
                if (string.IsNullOrWhiteSpace(call.Id))
                    throw new InvalidOperationException("O modelo devolveu um tool_use sem id.");

                var definition = Tools.FirstOrDefault(t => t.Name == call.Name);
                var input = call.Input ?? JsonDocument.Parse("{}").RootElement;
                if (definition is null)
                {
                    steps.Add(new(call.Name ?? "", "unknown", "error"));
                    held.Add(new(call.Id, AgentBlock.ToolResult(call.Id,
                        new { error = "unknown_tool", message = $"Ferramenta desconhecida: {call.Name}." }, true), null));
                }
                else if (!definition.IsWrite)
                {
                    var (payload, isError) = await executor.RunReadAsync(definition.Name, input, ct);
                    steps.Add(new(definition.Name, "read", isError ? "error" : "executed"));
                    held.Add(new(call.Id, AgentBlock.ToolResult(call.Id, payload, isError), null));
                }
                else
                {
                    var proposal = await executor.ProposeAsync(conversation.Id, call.Id, definition.Name, input, ct);
                    if (proposal.Action is { } action)
                    {
                        steps.Add(new(definition.Name, "write", "proposed"));
                        proposed.Add(action);
                        held.Add(new(call.Id, null, action.Id));
                    }
                    else
                    {
                        steps.Add(new(definition.Name, "write", "error"));
                        held.Add(new(call.Id, AgentBlock.ToolResult(call.Id, proposal.ErrorPayload!, true), null));
                    }
                }
            }

            if (proposed.Count > 0)
            {
                conversation.HeldResultsJson = JsonSerializer.Serialize(held, AgentJson.Options);
                break;
            }

            // All tool results of a turn go back in a single user message.
            history.Add(new AgentMessage(AgentMessage.User, held.Select(h => h.Result!).ToList()));

            if (i == _options.MaxIterations - 1)
                visible.Add(new(AgentMessage.Assistant, "Parei depois de muitas etapas seguidas. Tente um pedido mais direto."));
        }

        conversation.HistoryJson = JsonSerializer.Serialize(history, AgentJson.Options);
        conversation.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);

        return new ChatResponse(conversation.Id, model.Mode, visible, steps,
            proposed.Select(PendingActionDto.From).ToList());
    }

    private Conversation NewConversation()
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var c = new Conversation { Id = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now };
        db.Conversations.Add(c);
        return c;
    }

    public static List<AgentMessage> ReadHistory(Conversation c) =>
        JsonSerializer.Deserialize<List<AgentMessage>>(c.HistoryJson, AgentJson.Options) ?? [];
}
