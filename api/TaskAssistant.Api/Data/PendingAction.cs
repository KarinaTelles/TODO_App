namespace TaskAssistant.Api.Data;

public enum ActionStatus
{
    Pending = 0,
    Executed = 1,
    Rejected = 2,
    Expired = 3,
    Stale = 4,
    Failed = 5,
}

public class PendingAction
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }

    // The tool_use id from the model turn that proposed this action. The
    // tool_result sent back after confirm or reject carries this same id.
    public string ToolUseId { get; set; } = "";
    public string ToolName { get; set; } = "";

    // Arguments exactly as normalized by the server at proposal time.
    public string ArgumentsJson { get; set; } = "{}";

    // Tasks touched by the action with the version each had at proposal time.
    public string TargetsJson { get; set; } = "[]";

    // Text shown to the user, computed by the server from the arguments and the
    // current tasks. The model never writes this text.
    public string Preview { get; set; } = "";

    public ActionStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string? ResultJson { get; set; }
}

public record ActionTarget(int Id, string Title, int Version);
