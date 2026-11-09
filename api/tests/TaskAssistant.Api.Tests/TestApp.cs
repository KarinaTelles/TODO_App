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
using TaskAssistant.Api.Agent;
using TaskAssistant.Api.Tasks;

namespace TaskAssistant.Api.Tests;

// Real app over a private in-memory SQLite database, with a controllable clock
// and, optionally, a scripted model in place of the real one.
public sealed class TestApp : WebApplicationFactory<Program>
{
    private readonly string _connectionString = $"Data Source=file:tests-{Guid.NewGuid():N}?mode=memory&cache=shared";
    private readonly SqliteConnection _keepAlive;
    private readonly IChatModel? _model;

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 11, 9, 15, 0, 0, TimeSpan.Zero));

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public TestApp(IChatModel? model = null)
    {
        _model = model;
        _keepAlive = new SqliteConnection(_connectionString);
        _keepAlive.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Tasks", _connectionString);
        builder.UseSetting("ANTHROPIC_API_KEY", "");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            if (_model is not null)
            {
                services.RemoveAll<IChatModel>();
                services.AddSingleton(_model);
            }
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

    public async Task<ChatResponse> ChatAsync(HttpClient http, string message, Guid? conversationId = null)
    {
        var res = await http.PostAsJsonAsync("/api/chat", new { conversationId, message }, Json);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<ChatResponse>(Json))!;
    }

    public async Task<(HttpResponseMessage Response, ActionResolutionResponse? Body)> ResolveAsync(
        HttpClient http, Guid actionId, string verb)
    {
        var res = await http.PostAsync($"/api/actions/{actionId}/{verb}", null);
        var body = res.IsSuccessStatusCode ? await res.Content.ReadFromJsonAsync<ActionResolutionResponse>(Json) : null;
        return (res, body);
    }
}

// A model that follows a script. Each step sees the transcript it was given,
// so tests can assert exactly what would have been sent to a real model.
public sealed class ScriptedModel(params Func<IReadOnlyList<AgentMessage>, ModelTurn>[] steps) : IChatModel
{
    private int _next;
    public List<List<AgentMessage>> Calls { get; } = [];
    public string Mode => "scripted";
    public string? ModelName => "scripted";

    public Task<ModelTurn> NextTurnAsync(
        string systemPrompt, IReadOnlyList<ToolDefinition> tools, IReadOnlyList<AgentMessage> history, CancellationToken ct)
    {
        Calls.Add(history.ToList());
        if (_next >= steps.Length) throw new InvalidOperationException("O roteiro do modelo acabou.");
        return Task.FromResult(steps[_next++](history));
    }

    public static ModelTurn Tools(params AgentBlock[] calls) => new([.. calls], StopReasons.ToolUse);
    public static ModelTurn Say(string text) => new([AgentBlock.FromText(text)], StopReasons.EndTurn);
    public static AgentBlock Call(string id, string name, object input) => AgentBlock.ToolUse(id, name, input);

    public static List<AgentBlock> LastToolResults(List<AgentMessage> history) =>
        history[^1].Content.Where(b => b.Type == AgentBlock.ToolResultType).ToList();
}
