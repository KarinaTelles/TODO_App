namespace TaskAssistant.Api.Agent;

public class AgentOptions
{
    public string Model { get; set; } = "claude-opus-5-5";
    public string Effort { get; set; } = "medium";
    public int MaxTokens { get; set; } = 16000;
    public int MaxIterations { get; set; } = 8;
    public int PendingActionMinutes { get; set; } = 10;
    public int MaxBulkDelete { get; set; } = 10;
    public string TimeZone { get; set; } = "America/Sao_Paulo";
}

public class Calendar(TimeProvider clock, Microsoft.Extensions.Options.IOptions<AgentOptions> options)
{
    private readonly TimeZoneInfo _zone = Resolve(options.Value.TimeZone);

    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), _zone).DateTime);

    private static TimeZoneInfo Resolve(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.Utc; }
    }
}
