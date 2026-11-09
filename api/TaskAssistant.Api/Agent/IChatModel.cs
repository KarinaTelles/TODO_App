namespace TaskAssistant.Api.Agent;

public record ToolDefinition(string Name, string Description, string SchemaJson, bool IsWrite);

// One call to the model: given the transcript, return the next assistant turn.
// The loop that runs tools lives in AgentRunner, never inside the model, so
// tests can swap this for a scripted fake.
public interface IChatModel
{
    string Mode { get; }       // "anthropic" or "demo"
    string? ModelName { get; }
    Task<ModelTurn> NextTurnAsync(
        string systemPrompt,
        IReadOnlyList<ToolDefinition> tools,
        IReadOnlyList<AgentMessage> history,
        CancellationToken ct);
}
