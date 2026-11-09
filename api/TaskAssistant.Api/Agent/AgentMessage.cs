using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaskAssistant.Api.Agent;

// Provider-neutral transcript. It is what gets persisted, what the scripted
// test model reads, and what the Anthropic adapter converts to SDK types.
public sealed record AgentMessage(string Role, List<AgentBlock> Content)
{
    public const string User = "user";
    public const string Assistant = "assistant";
}

public sealed record AgentBlock
{
    public const string TextType = "text";
    public const string ThinkingType = "thinking";
    public const string RedactedThinkingType = "redacted_thinking";
    public const string ToolUseType = "tool_use";
    public const string ToolResultType = "tool_result";

    public required string Type { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Text { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Thinking { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Signature { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Data { get; init; }

    // tool_use
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Id { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Name { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public JsonElement? Input { get; init; }

    // tool_result: Content is always a JSON document, never free text.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ToolUseId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Content { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool IsError { get; init; }

    public static AgentBlock FromText(string text) => new() { Type = TextType, Text = text };

    public static AgentBlock ToolUse(string id, string name, object input) => new()
    {
        Type = ToolUseType,
        Id = id,
        Name = name,
        Input = JsonSerializer.SerializeToElement(input, AgentJson.Options),
    };

    public static AgentBlock ToolResult(string toolUseId, object payload, bool isError = false)
    {
        if (string.IsNullOrWhiteSpace(toolUseId))
            throw new ArgumentException("tool_result precisa do id do tool_use.", nameof(toolUseId));
        return new()
        {
            Type = ToolResultType,
            ToolUseId = toolUseId,
            Content = JsonSerializer.Serialize(payload, AgentJson.Options),
            IsError = isError,
        };
    }
}

public sealed record ModelTurn(List<AgentBlock> Content, string StopReason);

public static class StopReasons
{
    public const string EndTurn = "end_turn";
    public const string ToolUse = "tool_use";
    public const string MaxTokens = "max_tokens";
    public const string Refusal = "refusal";
}

public static class AgentJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        // Tool results go to the model, not to a browser: keep accents readable.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
