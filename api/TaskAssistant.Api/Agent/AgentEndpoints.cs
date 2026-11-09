using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaskAssistant.Api.Data;

namespace TaskAssistant.Api.Agent;

public static class AgentEndpoints
{
    public const int MessageMax = 4000;

    public static void MapAgentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/info", (IChatModel model, IOptions<AgentOptions> options) =>
        {
            var tools = ToolCatalog.Build(options.Value.MaxBulkDelete);
            return TypedResults.Ok(new InfoResponse(
                model.Mode,
                model.ModelName,
                model.Mode == DemoChatModel.ModeName,
                options.Value.PendingActionMinutes,
                options.Value.MaxBulkDelete,
                tools.Where(t => !t.IsWrite).Select(t => t.Name).ToList(),
                tools.Where(t => t.IsWrite).Select(t => t.Name).ToList()));
        }).WithTags("Assistente");

        var group = app.MapGroup("/api").WithTags("Assistente");

        group.MapPost("/chat", async Task<IResult> (ChatRequest body, AgentRunner runner, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.Message))
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["message"] = ["Escreva uma mensagem."] });
            if (body.Message.Length > MessageMax)
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                    { ["message"] = [$"A mensagem pode ter no máximo {MessageMax} caracteres."] });
            try
            {
                return TypedResults.Ok(await runner.SendAsync(body.ConversationId, body.Message.Trim(), ct));
            }
            catch (ConversationNotFoundException ex)
            {
                return TypedResults.Problem(title: "Conversa não encontrada", detail: ex.Message, statusCode: 404);
            }
            catch (PendingActionsOpenException ex)
            {
                return TypedResults.Problem(title: "Ações pendentes", detail: ex.Message + " Confirme ou recuse antes de continuar.",
                    statusCode: 409);
            }
        });

        group.MapGet("/conversations/{id:guid}", async Task<IResult> (
            Guid id, AppDbContext db, AgentRunner runner, IChatModel model, CancellationToken ct) =>
        {
            var conversation = await db.Conversations.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
            if (conversation is null)
                return TypedResults.Problem(title: "Conversa não encontrada", statusCode: 404);

            await runner.ExpireOverdueAsync(id, ct);
            var messages = AgentRunner.ReadHistory(conversation)
                .SelectMany(m => m.Content
                    .Where(b => b.Type == AgentBlock.TextType && !string.IsNullOrWhiteSpace(b.Text))
                    .Select(b => new ChatMessageDto(m.Role, b.Text!)))
                .ToList();
            var pending = await db.PendingActions.AsNoTracking()
                .Where(a => a.ConversationId == id && a.Status == ActionStatus.Pending)
                .OrderBy(a => a.CreatedAt)
                .ToListAsync(ct);
            return TypedResults.Ok(new ConversationDto(id, model.Mode, messages, pending.Select(PendingActionDto.From).ToList()));
        });

        group.MapGet("/actions/{id:guid}", async Task<IResult> (Guid id, AppDbContext db, CancellationToken ct) =>
        {
            var action = await db.PendingActions.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
            return action is null ? ActionNotFound() : TypedResults.Ok(PendingActionDto.From(action));
        });

        group.MapPost("/actions/{id:guid}/confirm", async Task<IResult> (Guid id, ActionService actions, CancellationToken ct) =>
            ToResult(await actions.ConfirmAsync(id, ct)));

        group.MapPost("/actions/{id:guid}/reject", async Task<IResult> (Guid id, ActionService actions, CancellationToken ct) =>
            ToResult(await actions.RejectAsync(id, ct)));
    }

    private static IResult ToResult(Resolution r) => r.Outcome switch
    {
        ResolutionOutcome.NotFound => ActionNotFound(),
        ResolutionOutcome.AlreadyResolved => TypedResults.Problem(
            title: "Ação já resolvida",
            detail: $"Esta ação já está com situação '{r.Action!.Status.ToString().ToLowerInvariant()}'. Nada foi feito.",
            statusCode: 409,
            extensions: new Dictionary<string, object?> { ["action"] = r.Action }),
        _ => TypedResults.Ok(new ActionResolutionResponse(r.Action!, r.Chat)),
    };

    private static IResult ActionNotFound() =>
        TypedResults.Problem(title: "Ação não encontrada", statusCode: 404);
}
