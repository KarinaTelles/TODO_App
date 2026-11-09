namespace TaskAssistant.Api.Data;

public class Conversation
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Full model transcript (provider-neutral AgentMessage list) as JSON.
    public string HistoryJson { get; set; } = "[]";

    // Tool results already produced for the assistant turn that is waiting on
    // confirmations. They are sent together with the write results once every
    // pending action of that turn is resolved.
    public string? HeldResultsJson { get; set; }
}
