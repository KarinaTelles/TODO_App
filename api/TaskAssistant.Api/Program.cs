using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
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
builder.Services.AddScoped<TaskService>();

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
app.MapGet("/healthz", () => TypedResults.Ok(new { status = "ok" })).ExcludeFromDescription();

app.Run();

public partial class Program;
