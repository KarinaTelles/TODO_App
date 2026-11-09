using Microsoft.EntityFrameworkCore;
using TaskAssistant.Api.Data;

namespace TaskAssistant.Api.Tasks;

public enum WriteOutcome
{
    Ok,
    NotFound,
    Conflict,
}

public record WriteResult(WriteOutcome Outcome, TaskItem? Task = null);

// Single place where tasks are read and written. Both the REST endpoints and
// the agent tools go through it.
public class TaskService(AppDbContext db, TimeProvider clock)
{
    public async Task<List<TaskItem>> QueryAsync(TaskQuery q, CancellationToken ct = default)
    {
        var query = db.Tasks.AsNoTracking().AsQueryable();
        query = q.Status switch
        {
            TaskStatusFilter.Open => query.Where(t => !t.Done),
            TaskStatusFilter.Done => query.Where(t => t.Done),
            _ => query,
        };
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var pattern = $"%{q.Search.Trim()}%";
            query = query.Where(t => EF.Functions.Like(t.Title, pattern)
                                     || (t.Notes != null && EF.Functions.Like(t.Notes, pattern)));
        }
        if (q.DueBefore is { } before)
            query = query.Where(t => t.DueDate != null && t.DueDate <= before);
        if (q.Priority is { } priority)
            query = query.Where(t => t.Priority == priority);

        var list = await query.ToListAsync(ct);
        return list
            .OrderBy(t => t.Done)
            .ThenBy(t => t.DueDate is null)
            .ThenBy(t => t.DueDate)
            .ThenByDescending(t => t.Priority)
            .ThenBy(t => t.Id)
            .Take(Math.Clamp(q.Limit, 1, 500))
            .ToList();
    }

    public Task<TaskItem?> GetAsync(int id, CancellationToken ct = default) =>
        db.Tasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);

    public Task<List<TaskItem>> GetManyAsync(IEnumerable<int> ids, CancellationToken ct = default)
    {
        var set = ids.ToList();
        return db.Tasks.AsNoTracking().Where(t => set.Contains(t.Id)).OrderBy(t => t.Id).ToListAsync(ct);
    }

    public async Task<TaskItem> CreateAsync(
        string title, string? notes, DateOnly? dueDate, TaskPriority priority, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var task = new TaskItem
        {
            Title = title.Trim(),
            Notes = TaskRules.CleanNotes(notes),
            DueDate = dueDate,
            Priority = priority,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };
        db.Tasks.Add(task);
        await db.SaveChangesAsync(ct);
        db.Entry(task).State = EntityState.Detached;
        return task;
    }

    // Applies a change only if the task still has the expected version.
    public async Task<WriteResult> UpdateAsync(
        int id, int? expectedVersion, Action<TaskItem> apply, CancellationToken ct = default)
    {
        var task = await db.Tasks.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (task is null) return new(WriteOutcome.NotFound);
        if (expectedVersion is { } v && task.Version != v)
            return new(WriteOutcome.Conflict, Detach(task));

        apply(task);
        task.Version += 1;
        task.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new(WriteOutcome.Conflict, Detach(task));
        }
        return new(WriteOutcome.Ok, Detach(task));
    }

    public async Task<WriteResult> DeleteAsync(int id, int? expectedVersion, CancellationToken ct = default)
    {
        var task = await db.Tasks.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (task is null) return new(WriteOutcome.NotFound);
        if (expectedVersion is { } v && task.Version != v)
            return new(WriteOutcome.Conflict, Detach(task));

        db.Tasks.Remove(task);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new(WriteOutcome.Conflict, Detach(task));
        }
        return new(WriteOutcome.Ok, Detach(task));
    }

    private TaskItem Detach(TaskItem task)
    {
        db.Entry(task).State = EntityState.Detached;
        return task;
    }
}
