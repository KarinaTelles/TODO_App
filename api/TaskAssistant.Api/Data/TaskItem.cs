namespace TaskAssistant.Api.Data;

public enum TaskPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
}

public class TaskItem
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string? Notes { get; set; }
    public DateOnly? DueDate { get; set; }
    public TaskPriority Priority { get; set; } = TaskPriority.Normal;
    public bool Done { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Incremented on every change. EF uses it as a concurrency token, so an
    // update based on an old read fails instead of overwriting someone else.
    public int Version { get; set; } = 1;
}
