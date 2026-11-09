using System.Net;
using System.Net.Http.Json;
using TaskAssistant.Api.Data;
using TaskAssistant.Api.Tasks;

namespace TaskAssistant.Api.Tests;

public class TaskEndpointTests
{
    [Fact]
    public async Task Create_get_update_delete()
    {
        using var app = new TestApp();
        var http = app.CreateClient();
        var ct = TestContext.Current.CancellationToken;

        var created = await app.SeedAsync(http, "  Pagar luz  ", new DateOnly(2026, 11, 20));
        Assert.Equal("Pagar luz", created.Title);
        Assert.Equal(TaskPriority.Normal, created.Priority);
        Assert.Equal(1, created.Version);

        var fetched = await http.GetFromJsonAsync<TaskDto>($"/api/tasks/{created.Id}", TestApp.Json, ct);
        Assert.Equal(created.Id, fetched!.Id);

        var put = await http.PutAsJsonAsync($"/api/tasks/{created.Id}", new
        {
            title = "Pagar conta de luz",
            notes = "vence dia 20",
            dueDate = "2026-11-20",
            priority = "high",
            done = true,
            version = 1,
        }, TestApp.Json, ct);
        put.EnsureSuccessStatusCode();
        var updated = await put.Content.ReadFromJsonAsync<TaskDto>(TestApp.Json, ct);
        Assert.Equal(2, updated!.Version);
        Assert.True(updated.Done);
        Assert.Equal(TaskPriority.High, updated.Priority);

        var del = await http.DeleteAsync($"/api/tasks/{created.Id}", ct);
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);
        var gone = await http.GetAsync($"/api/tasks/{created.Id}", ct);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        Assert.Equal("application/problem+json", gone.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Filters_by_status_search_and_due_date()
    {
        using var app = new TestApp();
        var http = app.CreateClient();
        var ct = TestContext.Current.CancellationToken;

        var a = await app.SeedAsync(http, "Comprar pão", new DateOnly(2026, 11, 10));
        await app.SeedAsync(http, "Ligar para o banco", new DateOnly(2026, 12, 1));
        var c = await app.SeedAsync(http, "Comprar café");
        (await http.PutAsJsonAsync($"/api/tasks/{c.Id}",
            new { title = c.Title, done = true, version = c.Version }, TestApp.Json, ct)).EnsureSuccessStatusCode();

        var open = await http.GetFromJsonAsync<List<TaskDto>>("/api/tasks?status=open", TestApp.Json, ct);
        Assert.Equal(2, open!.Count);
        var search = await http.GetFromJsonAsync<List<TaskDto>>("/api/tasks?search=comprar", TestApp.Json, ct);
        Assert.Equal(2, search!.Count);
        var due = await http.GetFromJsonAsync<List<TaskDto>>("/api/tasks?dueBefore=2026-11-15", TestApp.Json, ct);
        Assert.Equal([a.Id], due!.Select(t => t.Id));
    }

    [Fact]
    public async Task Rejects_invalid_input_with_validation_problem()
    {
        using var app = new TestApp();
        var http = app.CreateClient();
        var ct = TestContext.Current.CancellationToken;

        var res = await http.PostAsJsonAsync("/api/tasks", new { title = "   " }, TestApp.Json, ct);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync(ct);
        Assert.Contains("\"title\"", body);

        var longTitle = await http.PostAsJsonAsync("/api/tasks", new { title = new string('x', 201) }, TestApp.Json, ct);
        Assert.Equal(HttpStatusCode.BadRequest, longTitle.StatusCode);
    }

    [Fact]
    public async Task Update_with_old_version_returns_conflict()
    {
        using var app = new TestApp();
        var http = app.CreateClient();
        var ct = TestContext.Current.CancellationToken;
        var t = await app.SeedAsync(http, "Revisar contrato");

        (await http.PutAsJsonAsync($"/api/tasks/{t.Id}", new { title = "Revisar contrato v2", version = 1 }, TestApp.Json, ct))
            .EnsureSuccessStatusCode();
        var stale = await http.PutAsJsonAsync($"/api/tasks/{t.Id}", new { title = "Outra coisa", version = 1 }, TestApp.Json, ct);

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var current = await http.GetFromJsonAsync<TaskDto>($"/api/tasks/{t.Id}", TestApp.Json, ct);
        Assert.Equal("Revisar contrato v2", current!.Title);
    }

    [Fact]
    public async Task OpenApi_document_is_served()
    {
        using var app = new TestApp();
        var res = await app.CreateClient().GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        res.EnsureSuccessStatusCode();
        Assert.Contains("/api/tasks", await res.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }
}
