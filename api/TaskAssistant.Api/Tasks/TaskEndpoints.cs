using TaskAssistant.Api.Data;

namespace TaskAssistant.Api.Tasks;

public static class TaskEndpoints
{
    public static void MapTaskEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tasks").WithTags("Tarefas");

        group.MapGet("/", async Task<IResult> (
            TaskService tasks,
            string? status,
            string? search,
            DateOnly? dueBefore,
            string? priority,
            CancellationToken ct) =>
        {
            var errors = new Dictionary<string, string[]>();
            var statusFilter = TaskStatusFilter.All;
            if (status is not null && (!Enum.TryParse(status, ignoreCase: true, out statusFilter) || !Enum.IsDefined(statusFilter)))
                errors["status"] = ["Use open, done ou all."];
            TaskPriority? priorityFilter = null;
            if (priority is not null)
            {
                if (Enum.TryParse<TaskPriority>(priority, ignoreCase: true, out var p) && Enum.IsDefined(p)) priorityFilter = p;
                else errors["priority"] = ["Use low, normal ou high."];
            }
            if (errors.Count > 0) return TypedResults.ValidationProblem(errors);

            var list = await tasks.QueryAsync(new TaskQuery(statusFilter, search, dueBefore, priorityFilter), ct);
            return TypedResults.Ok(list.Select(TaskDto.From).ToList());
        });

        group.MapGet("/{id:int}", async Task<IResult> (int id, TaskService tasks, CancellationToken ct) =>
        {
            var task = await tasks.GetAsync(id, ct);
            return task is null ? NotFound(id) : TypedResults.Ok(TaskDto.From(task));
        });

        group.MapPost("/", async Task<IResult> (CreateTaskRequest body, TaskService tasks, CancellationToken ct) =>
        {
            var errors = TaskRules.Validate(body.Title, body.Notes);
            if (errors.Count > 0) return TypedResults.ValidationProblem(errors);

            var task = await tasks.CreateAsync(
                body.Title!, body.Notes, body.DueDate, body.Priority ?? TaskPriority.Normal, ct);
            return TypedResults.Created($"/api/tasks/{task.Id}", TaskDto.From(task));
        });

        group.MapPut("/{id:int}", async Task<IResult> (
            int id, UpdateTaskRequest body, TaskService tasks, CancellationToken ct) =>
        {
            var errors = TaskRules.Validate(body.Title, body.Notes);
            if (body.Version is null)
                errors["version"] = ["Envie a versão lida da tarefa."];
            if (errors.Count > 0) return TypedResults.ValidationProblem(errors);

            var result = await tasks.UpdateAsync(id, body.Version, t =>
            {
                t.Title = body.Title!.Trim();
                t.Notes = TaskRules.CleanNotes(body.Notes);
                t.DueDate = body.DueDate;
                t.Priority = body.Priority ?? t.Priority;
                t.Done = body.Done;
            }, ct);
            return ToResult(id, result, TaskDto.From);
        });

        group.MapDelete("/{id:int}", async Task<IResult> (
            int id, int? version, TaskService tasks, CancellationToken ct) =>
        {
            var result = await tasks.DeleteAsync(id, version, ct);
            return ToResult(id, result, _ => (object?)null);
        });
    }

    private static IResult ToResult<T>(int id, WriteResult result, Func<TaskItem, T> map) =>
        result.Outcome switch
        {
            WriteOutcome.NotFound => NotFound(id),
            WriteOutcome.Conflict => TypedResults.Problem(
                title: "Versão desatualizada",
                detail: $"A tarefa {id} foi alterada depois da sua leitura. Versão atual: {result.Task!.Version}.",
                statusCode: StatusCodes.Status409Conflict,
                extensions: new Dictionary<string, object?> { ["currentVersion"] = result.Task!.Version }),
            _ => map(result.Task!) is { } value ? TypedResults.Ok(value) : TypedResults.NoContent(),
        };

    private static IResult NotFound(int id) =>
        TypedResults.Problem(
            title: "Tarefa não encontrada",
            detail: $"Não existe tarefa com id {id}.",
            statusCode: StatusCodes.Status404NotFound);
}
