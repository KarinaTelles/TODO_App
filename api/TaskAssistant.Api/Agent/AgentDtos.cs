using System.Text.Json;
using TaskAssistant.Api.Data;

namespace TaskAssistant.Api.Agent;

public record ChatRequest(Guid? ConversationId, string? Message);

public record ChatMessageDto(string Role, string Text);

// What happened with each tool call in this request, for the UI to show.
public record ToolStepDto(string Tool, string Kind, string Outcome);

public record PendingActionDto(
    Guid Id,
    Guid ConversationId,
    string Tool,
    string Preview,
    IReadOnlyList<ActionTarget> Targets,
    JsonElement Arguments,
    ActionStatus Status,
    DateTime CreatedAt,
    DateTime ExpiresAt,
    DateTime? ResolvedAt,
    bool? ResultIsError,
    JsonElement? Result)
{
    public static PendingActionDto From(PendingAction a) => new(
        a.Id,
        a.ConversationId,
        a.ToolName,
        a.Preview,
        JsonSerializer.Deserialize<List<ActionTarget>>(a.TargetsJson, AgentJson.Options) ?? [],
        JsonDocument.Parse(a.ArgumentsJson).RootElement.Clone(),
        a.Status,
        Utc(a.CreatedAt),
        Utc(a.ExpiresAt),
        a.ResolvedAt is { } r ? Utc(r) : null,
        ResultBlock(a)?.IsError,
        ResultBlock(a) is { Content: { } content } ? JsonDocument.Parse(content).RootElement.Clone() : null);

    // ResultJson holds the full tool_result block; the API exposes its payload.
    private static AgentBlock? ResultBlock(PendingAction a) =>
        a.ResultJson is null ? null : JsonSerializer.Deserialize<AgentBlock>(a.ResultJson, AgentJson.Options);

    private static DateTime Utc(DateTime d) => DateTime.SpecifyKind(d, DateTimeKind.Utc);
}

public record ChatResponse(
    Guid ConversationId,
    string Mode,
    IReadOnlyList<ChatMessageDto> Messages,
    IReadOnlyList<ToolStepDto> Steps,
    IReadOnlyList<PendingActionDto> PendingActions);

public record ActionResolutionResponse(PendingActionDto Action, ChatResponse? Chat);

public record ConversationDto(
    Guid Id,
    string Mode,
    IReadOnlyList<ChatMessageDto> Messages,
    IReadOnlyList<PendingActionDto> PendingActions);

public record InfoResponse(
    string Mode,
    string? Model,
    bool DemoMode,
    int PendingActionMinutes,
    int MaxBulkDelete,
    IReadOnlyList<string> ReadTools,
    IReadOnlyList<string> WriteTools);
