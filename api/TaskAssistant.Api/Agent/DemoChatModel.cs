using System.Text;
using System.Text.Json;

namespace TaskAssistant.Api.Agent;

// Stand-in for the real model when there is no API key. Not AI: it parses a
// fixed set of commands and emits the same tool calls, then describes tool
// results in plain text. The confirmation flow it exercises is the real one.
public class DemoChatModel(Calendar calendar) : IChatModel
{
    public const string ModeName = "demo";
    public string Mode => ModeName;
    public string? ModelName => null;

    public Task<ModelTurn> NextTurnAsync(
        string systemPrompt, IReadOnlyList<ToolDefinition> tools, IReadOnlyList<AgentMessage> history, CancellationToken ct)
    {
        var last = history[^1];
        var text = last.Content.LastOrDefault(b => b.Type == AgentBlock.TextType)?.Text;

        if (text is not null)
        {
            var command = DemoCommandParser.Parse(text, calendar.Today);
            if (command is null)
                return Done(DemoCommandParser.Help);

            var toolUse = AgentBlock.ToolUse($"demo_{Guid.NewGuid():N}", command.Tool, command.Input);
            return Task.FromResult(new ModelTurn([toolUse], StopReasons.ToolUse));
        }

        var results = last.Content.Where(b => b.Type == AgentBlock.ToolResultType).ToList();
        var sb = new StringBuilder();
        foreach (var r in results) sb.AppendLine(Describe(r));
        return Done(sb.ToString().TrimEnd());
    }

    private static Task<ModelTurn> Done(string text) =>
        Task.FromResult(new ModelTurn([AgentBlock.FromText(text)], StopReasons.EndTurn));

    private static string Describe(AgentBlock result)
    {
        using var doc = JsonDocument.Parse(result.Content ?? "{}");
        var root = doc.RootElement;

        if (root.TryGetProperty("tasks", out var tasks))
        {
            if (tasks.GetArrayLength() == 0) return "Nenhuma tarefa encontrada.";
            var sb = new StringBuilder($"{tasks.GetArrayLength()} tarefa(s):");
            foreach (var t in tasks.EnumerateArray()) sb.Append('\n').Append(Line(t));
            return sb.ToString();
        }
        if (root.TryGetProperty("task", out var task) && !root.TryGetProperty("status", out _))
            return Line(task);

        var status = root.TryGetProperty("status", out var s) ? s.GetString() : null;
        return status switch
        {
            "executed" => $"Feito. {root.GetProperty("summary").GetString()}",
            "rejected" or "expired" or "stale" or "failed" => root.GetProperty("message").GetString()!,
            _ => root.TryGetProperty("message", out var m) ? $"Não foi possível: {m.GetString()}" : "Sem resultado.",
        };
    }

    private static string Line(JsonElement t)
    {
        var due = t.GetProperty("due_date").GetString() is { } d
            ? $", prazo {DateOnly.Parse(d, System.Globalization.CultureInfo.InvariantCulture):dd/MM/yyyy}"
            : "";
        var done = t.GetProperty("done").GetBoolean() ? " (concluída)" : "";
        return $"#{t.GetProperty("id").GetInt32()} {t.GetProperty("title").GetString()}{due}{done}";
    }
}
