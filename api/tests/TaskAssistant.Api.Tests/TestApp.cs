using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using TaskAssistant.Api.Tasks;

namespace TaskAssistant.Api.Tests;

// Real app over a private in-memory SQLite database, with a controllable clock
// and no external services.
public sealed class TestApp : WebApplicationFactory<Program>
{
    private readonly string _connectionString = $"Data Source=file:tests-{Guid.NewGuid():N}?mode=memory&cache=shared";
    private readonly SqliteConnection _keepAlive;

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 11, 9, 15, 0, 0, TimeSpan.Zero));

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public TestApp()
    {
        _keepAlive = new SqliteConnection(_connectionString);
        _keepAlive.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Tasks", _connectionString);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _keepAlive.Dispose();
    }

    public async Task<TaskDto> SeedAsync(HttpClient http, string title, DateOnly? due = null)
    {
        var res = await http.PostAsJsonAsync("/api/tasks", new { title, dueDate = due }, Json);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<TaskDto>(Json))!;
    }

    public async Task<List<TaskDto>> AllTasksAsync(HttpClient http) =>
        (await http.GetFromJsonAsync<List<TaskDto>>("/api/tasks", Json))!;
}
