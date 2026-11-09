using System.Text.Json;
using Anthropic;
using Anthropic.Core;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Options;

namespace TaskAssistant.Api.Agent;

// Real model through the official Anthropic C# SDK. One request per call; the
// tool loop stays in AgentRunner (no automatic tool execution).
public class AnthropicChatModel(AnthropicClient client, IOptions<AgentOptions> options) : IChatModel
{
    public const string ModeName = "anthropic";
    private readonly AgentOptions _options = options.Value;

    public string Mode => ModeName;
    public string? ModelName => _options.Model;

    public async Task<ModelTurn> NextTurnAsync(
        string systemPrompt, IReadOnlyList<ToolDefinition> tools, IReadOnlyList<AgentMessage> history, CancellationToken ct)
    {
        var response = await client.Messages.Create(BuildParams(systemPrompt, tools, history), ct);
        return ToTurn(response);
    }

    internal MessageCreateParams BuildParams(
        string systemPrompt, IReadOnlyList<ToolDefinition> tools, IReadOnlyList<AgentMessage> history) =>
        new()
        {
            Model = _options.Model,
            MaxTokens = _options.MaxTokens,
            System = systemPrompt,
            Thinking = new ThinkingConfigAdaptive(),
            OutputConfig = new OutputConfig { Effort = ParseEffort(_options.Effort) },
            ToolChoice = new ToolChoiceAuto(),
            Tools = tools.Select(ToSdkTool).ToList(),
            Messages = history.Select(ToSdkMessage).ToList(),
        };

    internal static ModelTurn ToTurn(Message response)
    {
        var blocks = new List<AgentBlock>();
        foreach (var block in response.Content)
        {
            if (block.TryPickText(out TextBlock? text))
                blocks.Add(AgentBlock.FromText(text.Text));
            else if (block.TryPickThinking(out ThinkingBlock? thinking))
                blocks.Add(new AgentBlock { Type = AgentBlock.ThinkingType, Thinking = thinking.Thinking, Signature = thinking.Signature });
            else if (block.TryPickRedactedThinking(out RedactedThinkingBlock? redacted))
                blocks.Add(new AgentBlock { Type = AgentBlock.RedactedThinkingType, Data = redacted.Data });
            else if (block.TryPickToolUse(out ToolUseBlock? toolUse))
                blocks.Add(new AgentBlock
                {
                    Type = AgentBlock.ToolUseType,
                    Id = toolUse.ID,
                    Name = toolUse.Name,
                    Input = JsonSerializer.SerializeToElement(toolUse.Input),
                });
        }

        return new ModelTurn(blocks, MapStopReason(response.StopReason));
    }

    private static string MapStopReason(ApiEnum<string, StopReason>? reason)
    {
        if (reason is null) return StopReasons.EndTurn;
        if (reason == StopReason.Refusal) return StopReasons.Refusal;
        if (reason == StopReason.MaxTokens) return StopReasons.MaxTokens;
        if (reason == StopReason.ToolUse) return StopReasons.ToolUse;
        return StopReasons.EndTurn;
    }

    private static Effort ParseEffort(string value) => value.ToLowerInvariant() switch
    {
        "low" => Effort.Low,
        "high" => Effort.High,
        "max" => Effort.Max,
        _ => Effort.Medium,
    };

    private static ToolUnion ToSdkTool(ToolDefinition t)
    {
        var schema = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(t.SchemaJson)!;
        return new Tool
        {
            Name = t.Name,
            Description = t.Description,
            InputSchema = InputSchema.FromRawUnchecked(schema),
            Strict = true,
        };
    }

    private static MessageParam ToSdkMessage(AgentMessage m)
    {
        var content = new List<ContentBlockParam>();
        foreach (var b in m.Content)
        {
            switch (b.Type)
            {
                case AgentBlock.TextType:
                    content.Add(new TextBlockParam { Text = b.Text ?? "" });
                    break;
                case AgentBlock.ThinkingType:
                    content.Add(new ThinkingBlockParam { Thinking = b.Thinking ?? "", Signature = b.Signature ?? "" });
                    break;
                case AgentBlock.RedactedThinkingType:
                    content.Add(new RedactedThinkingBlockParam { Data = b.Data ?? "" });
                    break;
                case AgentBlock.ToolUseType:
                    content.Add(new ToolUseBlockParam
                    {
                        ID = b.Id!,
                        Name = b.Name!,
                        Input = b.Input is { } input
                            ? JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(input.GetRawText())!
                            : new Dictionary<string, JsonElement>(),
                    });
                    break;
                case AgentBlock.ToolResultType:
                    content.Add(new ToolResultBlockParam { ToolUseID = b.ToolUseId!, Content = b.Content ?? "{}", IsError = b.IsError });
                    break;
            }
        }

        return m.Role == AgentMessage.Assistant
            ? new AssistantMessageParam { Content = content }
            : new UserMessageParam { Content = content };
    }
}
