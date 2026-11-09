using System.Globalization;
using System.Text.Json;
using TaskAssistant.Api.Data;

namespace TaskAssistant.Api.Agent;

public class ToolArgumentException(string message) : Exception(message);

// Reads tool input defensively. Strict schemas make bad input rare, but the
// server validates again: the model output is never trusted as-is.
internal static class ToolArgs
{
    public static int RequireInt(JsonElement input, string name)
    {
        if (input.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i))
            return i;
        throw new ToolArgumentException($"'{name}' deve ser um número inteiro.");
    }

    public static string? OptionalString(JsonElement input, string name)
    {
        if (!input.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null) return null;
        if (v.ValueKind != JsonValueKind.String) throw new ToolArgumentException($"'{name}' deve ser texto ou null.");
        return v.GetString();
    }

    public static string RequireString(JsonElement input, string name) =>
        OptionalString(input, name) is { Length: > 0 } s ? s : throw new ToolArgumentException($"'{name}' é obrigatório.");

    public static DateOnly? OptionalDate(JsonElement input, string name)
    {
        var s = OptionalString(input, name);
        if (s is null) return null;
        if (DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d;
        throw new ToolArgumentException($"'{name}' deve estar no formato AAAA-MM-DD.");
    }

    public static TaskPriority? OptionalPriority(JsonElement input, string name) =>
        OptionalString(input, name) switch
        {
            null => null,
            "low" => TaskPriority.Low,
            "normal" => TaskPriority.Normal,
            "high" => TaskPriority.High,
            var other => throw new ToolArgumentException($"Prioridade inválida: '{other}'."),
        };

    public static List<int> RequireIntArray(JsonElement input, string name)
    {
        if (!input.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array)
            throw new ToolArgumentException($"'{name}' deve ser uma lista de ids.");
        var list = new List<int>();
        foreach (var item in v.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Number || !item.TryGetInt32(out var i))
                throw new ToolArgumentException($"'{name}' deve conter apenas números inteiros.");
            list.Add(i);
        }
        return list;
    }

    public static string FormatDate(DateOnly? d) => d?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "sem prazo";

    public static string FormatPriority(TaskPriority p) => p switch
    {
        TaskPriority.Low => "baixa",
        TaskPriority.High => "alta",
        _ => "normal",
    };

    public static string Quote(string title) => $"\"{title}\"";
}
