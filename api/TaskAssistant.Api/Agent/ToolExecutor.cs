using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaskAssistant.Api.Data;
using TaskAssistant.Api.Tasks;

namespace TaskAssistant.Api.Agent;

public record Proposal(PendingAction? Action, object? ErrorPayload);

public record Execution(ActionStatus Status, object Payload);

// Runs read tools, turns write tools into PendingActions, and executes a
// PendingAction once it has been confirmed.
public class ToolExecutor(AppDbContext db, TaskService tasks, TimeProvider clock, IOptions<AgentOptions> options)
{
    private readonly AgentOptions _options = options.Value;

    public static object TaskData(TaskItem t) => new
    {
        id = t.Id,
        title = t.Title,
        notes = t.Notes,
        due_date = t.DueDate?.ToString("yyyy-MM-dd"),
        priority = t.Priority.ToString().ToLowerInvariant(),
        done = t.Done,
        version = t.Version,
    };

    public async Task<(object Payload, bool IsError)> RunReadAsync(string name, JsonElement input, CancellationToken ct)
    {
        try
        {
            switch (name)
            {
                case ToolNames.ListTasks:
                    var status = ToolArgs.OptionalString(input, "status") switch
                    {
                        "open" => TaskStatusFilter.Open,
                        "done" => TaskStatusFilter.Done,
                        _ => TaskStatusFilter.All,
                    };
                    var list = await tasks.QueryAsync(new TaskQuery(
                        status,
                        ToolArgs.OptionalString(input, "search"),
                        ToolArgs.OptionalDate(input, "due_before"),
                        Limit: 100), ct);
                    return (new { count = list.Count, tasks = list.Select(TaskData) }, false);

                case ToolNames.GetTask:
                    var id = ToolArgs.RequireInt(input, "id");
                    var task = await tasks.GetAsync(id, ct);
                    return task is null
                        ? (new { error = "not_found", message = $"Não existe tarefa com id {id}." }, true)
                        : (new { task = TaskData(task) }, false);

                default:
                    return (new { error = "unknown_tool", message = $"Ferramenta desconhecida: {name}." }, true);
            }
        }
        catch (ToolArgumentException ex)
        {
            return (new { error = "invalid_arguments", message = ex.Message }, true);
        }
    }

    // Validates the request, computes the preview from the current data and
    // stores it. Nothing in the task table changes here.
    public async Task<Proposal> ProposeAsync(
        Guid conversationId, string toolUseId, string name, JsonElement input, CancellationToken ct)
    {
        try
        {
            var (args, targets, preview) = name switch
            {
                ToolNames.CreateTask => ProposeCreate(input),
                ToolNames.UpdateTask => await ProposeUpdateAsync(input, ct),
                ToolNames.CompleteTask => await ProposeCompleteAsync(input, ct),
                ToolNames.DeleteTask => await ProposeDeleteAsync(input, ct),
                _ => throw new ToolArgumentException($"Ferramenta desconhecida: {name}."),
            };

            var now = clock.GetUtcNow().UtcDateTime;
            var action = new PendingAction
            {
                Id = Guid.NewGuid(),
                ConversationId = conversationId,
                ToolUseId = toolUseId,
                ToolName = name,
                ArgumentsJson = JsonSerializer.Serialize(args, AgentJson.Options),
                TargetsJson = JsonSerializer.Serialize(targets, AgentJson.Options),
                Preview = preview,
                Status = ActionStatus.Pending,
                CreatedAt = now,
                ExpiresAt = now.AddMinutes(_options.PendingActionMinutes),
            };
            db.PendingActions.Add(action);
            await db.SaveChangesAsync(ct);
            return new(action, null);
        }
        catch (ToolArgumentException ex)
        {
            return new(null, new { error = "invalid_arguments", message = ex.Message });
        }
    }

    private static (object, List<ActionTarget>, string) ProposeCreate(JsonElement input)
    {
        var title = ToolArgs.RequireString(input, "title").Trim();
        var notes = ToolArgs.OptionalString(input, "notes");
        var errors = TaskRules.Validate(title, notes);
        if (errors.Count > 0) throw new ToolArgumentException(string.Join(" ", errors.Values.SelectMany(v => v)));
        var due = ToolArgs.OptionalDate(input, "due_date");
        var priority = ToolArgs.OptionalPriority(input, "priority") ?? TaskPriority.Normal;

        var args = new CreateArgs(title, TaskRules.CleanNotes(notes), due, priority);
        var preview = $"Criar a tarefa {ToolArgs.Quote(title)}, prazo {ToolArgs.FormatDate(due)}, " +
                      $"prioridade {ToolArgs.FormatPriority(priority)}.";
        if (args.Notes is not null) preview += $" Notas: {args.Notes}";
        return (args, [], preview);
    }

    private async Task<(object, List<ActionTarget>, string)> ProposeUpdateAsync(JsonElement input, CancellationToken ct)
    {
        var id = ToolArgs.RequireInt(input, "id");
        var task = await tasks.GetAsync(id, ct) ?? throw new ToolArgumentException($"Não existe tarefa com id {id}.");
        var title = ToolArgs.OptionalString(input, "title")?.Trim();
        var notes = ToolArgs.OptionalString(input, "notes");
        if (title is not null || notes is not null)
        {
            var errors = TaskRules.Validate(title ?? task.Title, notes);
            if (errors.Count > 0) throw new ToolArgumentException(string.Join(" ", errors.Values.SelectMany(v => v)));
        }
        var due = ToolArgs.OptionalDate(input, "due_date");
        var priority = ToolArgs.OptionalPriority(input, "priority");

        var changes = new List<string>();
        if (title is not null && title != task.Title)
            changes.Add($"título de {ToolArgs.Quote(task.Title)} para {ToolArgs.Quote(title)}");
        if (notes is not null && TaskRules.CleanNotes(notes) != task.Notes)
            changes.Add("notas substituídas");
        if (due is not null && due != task.DueDate)
            changes.Add($"prazo de {ToolArgs.FormatDate(task.DueDate)} para {ToolArgs.FormatDate(due)}");
        if (priority is not null && priority != task.Priority)
            changes.Add($"prioridade de {ToolArgs.FormatPriority(task.Priority)} para {ToolArgs.FormatPriority(priority.Value)}");
        if (changes.Count == 0)
            throw new ToolArgumentException($"Nada a alterar na tarefa {id}: os valores pedidos já são os atuais.");

        var args = new UpdateArgs(id, title, notes is null ? null : TaskRules.CleanNotes(notes) ?? "", due, priority);
        var preview = $"Alterar a tarefa #{task.Id} {ToolArgs.Quote(task.Title)}: {string.Join("; ", changes)}.";
        return (args, [new ActionTarget(task.Id, task.Title, task.Version)], preview);
    }

    private async Task<(object, List<ActionTarget>, string)> ProposeCompleteAsync(JsonElement input, CancellationToken ct)
    {
        var id = ToolArgs.RequireInt(input, "id");
        var task = await tasks.GetAsync(id, ct) ?? throw new ToolArgumentException($"Não existe tarefa com id {id}.");
        if (task.Done) throw new ToolArgumentException($"A tarefa {id} já está concluída.");
        return (new CompleteArgs(id), [new ActionTarget(task.Id, task.Title, task.Version)],
            $"Marcar como concluída a tarefa #{task.Id} {ToolArgs.Quote(task.Title)}.");
    }

    private async Task<(object, List<ActionTarget>, string)> ProposeDeleteAsync(JsonElement input, CancellationToken ct)
    {
        var ids = ToolArgs.RequireIntArray(input, "ids").Distinct().ToList();
        if (ids.Count == 0) throw new ToolArgumentException("Informe ao menos um id.");
        if (ids.Count > _options.MaxBulkDelete)
            throw new ToolArgumentException(
                $"No máximo {_options.MaxBulkDelete} tarefas por exclusão; foram pedidas {ids.Count}.");

        var found = await tasks.GetManyAsync(ids, ct);
        var missing = ids.Except(found.Select(t => t.Id)).ToList();
        if (missing.Count > 0)
            throw new ToolArgumentException($"Ids inexistentes: {string.Join(", ", missing)}.");

        var lines = string.Join("; ", found.Select(t => $"#{t.Id} {ToolArgs.Quote(t.Title)}"));
        var preview = found.Count == 1
            ? $"Excluir a tarefa {lines}."
            : $"Excluir {found.Count} tarefas: {lines}.";
        return (new DeleteArgs(found.Select(t => t.Id).ToList()),
            found.Select(t => new ActionTarget(t.Id, t.Title, t.Version)).ToList(), preview);
    }

    // Runs a confirmed action. Every target must still have the version seen at
    // proposal time; afterwards the tasks are read again to check the result.
    public async Task<Execution> ExecuteAsync(PendingAction action, CancellationToken ct)
    {
        var targets = JsonSerializer.Deserialize<List<ActionTarget>>(action.TargetsJson, AgentJson.Options) ?? [];
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        foreach (var target in targets)
        {
            var current = await tasks.GetAsync(target.Id, ct);
            if (current is null || current.Version != target.Version)
            {
                return new(ActionStatus.Stale, new
                {
                    status = "stale",
                    message = current is null
                        ? $"A tarefa {target.Id} não existe mais. Nada foi alterado."
                        : $"A tarefa {target.Id} mudou depois da proposta (versão {target.Version} -> {current.Version}). Nada foi alterado.",
                });
            }
        }

        Execution result;
        switch (action.ToolName)
        {
            case ToolNames.CreateTask:
            {
                var a = Read<CreateArgs>(action);
                var created = await tasks.CreateAsync(a.Title, a.Notes, a.DueDate, a.Priority, ct);
                var check = await tasks.GetAsync(created.Id, ct);
                result = Verify(check is not null && check.Title == a.Title && check.DueDate == a.DueDate
                                && check.Priority == a.Priority, action, check);
                break;
            }
            case ToolNames.UpdateTask:
            {
                var a = Read<UpdateArgs>(action);
                var w = await tasks.UpdateAsync(a.Id, targets[0].Version, t =>
                {
                    if (a.Title is not null) t.Title = a.Title;
                    if (a.Notes is not null) t.Notes = a.Notes.Length == 0 ? null : a.Notes;
                    if (a.DueDate is not null) t.DueDate = a.DueDate;
                    if (a.Priority is not null) t.Priority = a.Priority.Value;
                }, ct);
                if (w.Outcome != WriteOutcome.Ok) return StaleFromWrite(a.Id);
                var check = await tasks.GetAsync(a.Id, ct);
                result = Verify(check is not null
                                && (a.Title is null || check.Title == a.Title)
                                && (a.DueDate is null || check.DueDate == a.DueDate)
                                && (a.Priority is null || check.Priority == a.Priority), action, check);
                break;
            }
            case ToolNames.CompleteTask:
            {
                var a = Read<CompleteArgs>(action);
                var w = await tasks.UpdateAsync(a.Id, targets[0].Version, t => t.Done = true, ct);
                if (w.Outcome != WriteOutcome.Ok) return StaleFromWrite(a.Id);
                var check = await tasks.GetAsync(a.Id, ct);
                result = Verify(check is { Done: true }, action, check);
                break;
            }
            case ToolNames.DeleteTask:
            {
                var a = Read<DeleteArgs>(action);
                foreach (var target in targets)
                {
                    var w = await tasks.DeleteAsync(target.Id, target.Version, ct);
                    if (w.Outcome != WriteOutcome.Ok) return StaleFromWrite(target.Id);
                }
                var left = await tasks.GetManyAsync(a.Ids, ct);
                result = left.Count == 0
                    ? new(ActionStatus.Executed, new { status = "executed", summary = action.Preview, deleted_ids = a.Ids })
                    : new(ActionStatus.Failed, new { status = "failed", message = "Algumas tarefas continuam existindo após a exclusão." });
                break;
            }
            default:
                return new(ActionStatus.Failed, new { status = "failed", message = $"Ferramenta desconhecida: {action.ToolName}." });
        }

        if (result.Status == ActionStatus.Executed) await tx.CommitAsync(ct);
        return result;
    }

    private static Execution Verify(bool ok, PendingAction action, TaskItem? check) => ok
        ? new(ActionStatus.Executed, new { status = "executed", summary = action.Preview, task = TaskData(check!) })
        : new(ActionStatus.Failed, new { status = "failed", message = "A releitura não confirmou o estado esperado; a alteração foi desfeita." });

    private static Execution StaleFromWrite(int id) =>
        new(ActionStatus.Stale, new { status = "stale", message = $"A tarefa {id} mudou durante a execução. Nada foi alterado." });

    private static T Read<T>(PendingAction action) =>
        JsonSerializer.Deserialize<T>(action.ArgumentsJson, AgentJson.Options)!;
}

public record CreateArgs(string Title, string? Notes, DateOnly? DueDate, TaskPriority Priority);
public record UpdateArgs(int Id, string? Title, string? Notes, DateOnly? DueDate, TaskPriority? Priority);
public record CompleteArgs(int Id);
public record DeleteArgs(List<int> Ids);
