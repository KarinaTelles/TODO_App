using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TaskAssistant.Api.Data;

namespace TaskAssistant.Api.Agent;

public enum ResolutionOutcome
{
    Ok,
    NotFound,
    AlreadyResolved,
}

public record Resolution(ResolutionOutcome Outcome, PendingActionDto? Action = null, ChatResponse? Chat = null);

public class ActionService(AppDbContext db, ToolExecutor executor, AgentRunner runner, TimeProvider clock)
{
    public static readonly object ExpiredPayload = new
    {
        status = "expired",
        message = "A proposta expirou sem confirmação. Nada foi alterado.",
    };

    public static readonly object RejectedPayload = new
    {
        status = "rejected",
        message = "O usuário recusou esta ação. Nada foi alterado.",
    };

    // The stored result is the exact tool_result block, tied to the tool_use id
    // of the turn that proposed the action.
    public static string ResultJson(PendingAction action, object payload, bool isError) =>
        JsonSerializer.Serialize(AgentBlock.ToolResult(action.ToolUseId, payload, isError), AgentJson.Options);

    public async Task<Resolution> ConfirmAsync(Guid id, CancellationToken ct)
    {
        var action = await db.PendingActions.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        if (action is null) return new(ResolutionOutcome.NotFound);

        var now = clock.GetUtcNow().UtcDateTime;

        // Pending -> Executed in one conditional UPDATE. A second confirm, or a
        // confirm racing with a reject, matches zero rows and changes nothing.
        var claimed = await db.PendingActions
            .Where(a => a.Id == id && a.Status == ActionStatus.Pending && a.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Status, ActionStatus.Executed)
                .SetProperty(a => a.ResolvedAt, now), ct);

        if (claimed == 0)
        {
            await runner.ExpireOverdueAsync(action.ConversationId, ct);
            var current = await db.PendingActions.AsNoTracking().FirstAsync(a => a.Id == id, ct);
            if (current.Status == ActionStatus.Expired && action.Status == ActionStatus.Pending)
            {
                // It expired just now: report it and let the model continue.
                var chat = await runner.ResumeIfReadyAsync(action.ConversationId, ct);
                return new(ResolutionOutcome.Ok, PendingActionDto.From(current), chat);
            }
            return new(ResolutionOutcome.AlreadyResolved, PendingActionDto.From(current));
        }

        Execution execution;
        try
        {
            execution = await executor.ExecuteAsync(action, ct);
        }
        catch (Exception)
        {
            // The executor's transaction rolled back. Record the failure so the
            // action does not stay "executed" without a result.
            execution = new(ActionStatus.Failed, new { status = "failed", message = "A ação falhou e nada foi alterado." });
        }
        var isError = execution.Status != ActionStatus.Executed;
        await db.PendingActions
            .Where(a => a.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Status, execution.Status)
                .SetProperty(a => a.ResultJson, ResultJson(action, execution.Payload, isError)), CancellationToken.None);

        var updated = await db.PendingActions.AsNoTracking().FirstAsync(a => a.Id == id, ct);
        var resumed = await runner.ResumeIfReadyAsync(action.ConversationId, ct);
        return new(ResolutionOutcome.Ok, PendingActionDto.From(updated), resumed);
    }

    public async Task<Resolution> RejectAsync(Guid id, CancellationToken ct)
    {
        var action = await db.PendingActions.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        if (action is null) return new(ResolutionOutcome.NotFound);

        var now = clock.GetUtcNow().UtcDateTime;
        var claimed = await db.PendingActions
            .Where(a => a.Id == id && a.Status == ActionStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Status, ActionStatus.Rejected)
                .SetProperty(a => a.ResolvedAt, now)
                .SetProperty(a => a.ResultJson, ResultJson(action, RejectedPayload, true)), ct);

        var current = await db.PendingActions.AsNoTracking().FirstAsync(a => a.Id == id, ct);
        if (claimed == 0) return new(ResolutionOutcome.AlreadyResolved, PendingActionDto.From(current));

        var chat = await runner.ResumeIfReadyAsync(action.ConversationId, ct);
        return new(ResolutionOutcome.Ok, PendingActionDto.From(current), chat);
    }
}
