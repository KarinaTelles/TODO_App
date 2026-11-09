using TaskAssistant.Api.Data;

namespace TaskAssistant.Api.Tasks;

public record TaskDto(
    int Id,
    string Title,
    string? Notes,
    DateOnly? DueDate,
    TaskPriority Priority,
    bool Done,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int Version)
{
    public static TaskDto From(TaskItem t) =>
        new(t.Id, t.Title, t.Notes, t.DueDate, t.Priority, t.Done,
            DateTime.SpecifyKind(t.CreatedAt, DateTimeKind.Utc),
            DateTime.SpecifyKind(t.UpdatedAt, DateTimeKind.Utc),
            t.Version);
}

public record CreateTaskRequest(string? Title, string? Notes, DateOnly? DueDate, TaskPriority? Priority);

// Full replacement. Version is the value the client read; a different current
// value means someone changed the task in between and the request gets 409.
public record UpdateTaskRequest(
    string? Title, string? Notes, DateOnly? DueDate, TaskPriority? Priority, bool Done, int? Version);

public enum TaskStatusFilter
{
    All,
    Open,
    Done,
}

public record TaskQuery(
    TaskStatusFilter Status = TaskStatusFilter.All,
    string? Search = null,
    DateOnly? DueBefore = null,
    TaskPriority? Priority = null,
    int Limit = 200);

public static class TaskRules
{
    public const int TitleMax = 200;
    public const int NotesMax = 2000;

    public static Dictionary<string, string[]> Validate(string? title, string? notes)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(title))
            errors["title"] = ["O título é obrigatório."];
        else if (title.Trim().Length > TitleMax)
            errors["title"] = [$"O título pode ter no máximo {TitleMax} caracteres."];
        if (notes is not null && notes.Length > NotesMax)
            errors["notes"] = [$"As notas podem ter no máximo {NotesMax} caracteres."];
        return errors;
    }

    public static string? CleanNotes(string? notes) =>
        string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
}
