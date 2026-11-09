using System.Text.Json;
using Anthropic;
using Microsoft.Extensions.Options;
using TaskAssistant.Api.Agent;

namespace TaskAssistant.Api.Tests;

// Checks the request built for the real API without calling it.
public class AnthropicRequestTests
{
    [Fact]
    public void Request_uses_manual_loop_settings_and_keeps_tool_ids()
    {
        var model = new AnthropicChatModel(new AnthropicClient { ApiKey = "test" }, Options.Create(new AgentOptions { Effort = "high" }));
        var history = new List<AgentMessage>
        {
            new(AgentMessage.User, [AgentBlock.FromText("lista")]),
            new(AgentMessage.Assistant, [AgentBlock.ToolUse("toolu_1", ToolNames.ListTasks, new { status = "open", search = (string?)null, due_before = (string?)null })]),
            new(AgentMessage.User, [AgentBlock.ToolResult("toolu_1", new { count = 0 })]),
        };

        var p = model.BuildParams("sistema", ToolCatalog.Build(10), history);
        // The SDK serializes the request body under RawBodyData.
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(p));
        var root = doc.RootElement.GetProperty("RawBodyData");

        Assert.Equal("claude-opus-5-5", root.GetProperty("model").GetString());
        Assert.Equal("adaptive", root.GetProperty("thinking").GetProperty("type").GetString());
        Assert.Equal("high", root.GetProperty("output_config").GetProperty("effort").GetString());
        Assert.Equal("auto", root.GetProperty("tool_choice").GetProperty("type").GetString());
        var tools = root.GetProperty("tools").EnumerateArray().ToList();
        Assert.Equal(6, tools.Count);
        Assert.All(tools, t =>
        {
            Assert.True(t.GetProperty("strict").GetBoolean());
            Assert.False(t.GetProperty("input_schema").GetProperty("additionalProperties").GetBoolean());
        });
        var result = root.GetProperty("messages")[2].GetProperty("content")[0];
        Assert.Equal("tool_result", result.GetProperty("type").GetString());
        Assert.Equal("toolu_1", result.GetProperty("tool_use_id").GetString());
    }
}
