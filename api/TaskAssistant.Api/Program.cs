using System.Text.Json.Serialization;
using Anthropic;
using Microsoft.EntityFrameworkCore;
using TaskAssistant.Api.Agent;
using TaskAssistant.Api.Data;
using TaskAssistant.Api.Tasks;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase)));

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [])
    .AllowAnyHeader()
    .AllowAnyMethod()));

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Tasks") ?? "Data Source=tasks.db"));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection("Agent"));
builder.Services.AddSingleton<Calendar>();
builder.Services.AddScoped<TaskService>();
builder.Services.AddScoped<ToolExecutor>();
builder.Services.AddScoped<AgentRunner>();
builder.Services.AddScoped<ActionService>();

// With ANTHROPIC_API_KEY the assistant uses Claude; without it, the offline
// demo parser. /api/info tells the UI which one is active.
var apiKey = builder.Configuration["ANTHROPIC_API_KEY"];
if (!string.IsNullOrWhiteSpace(apiKey))
{
    builder.Services.AddSingleton(new AnthropicClient { ApiKey = apiKey });
    builder.Services.AddSingleton<IChatModel, AnthropicChatModel>();
}
else
{
    builder.Services.AddSingleton<IChatModel, DemoChatModel>();
}

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors();

app.MapOpenApi();
app.MapTaskEndpoints();
app.MapAgentEndpoints();
app.MapGet("/healthz", () => TypedResults.Ok(new { status = "ok" })).ExcludeFromDescription();

app.Run();

public partial class Program;
