using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TaskAssistant.Api.Agent;
using TaskAssistant.Api.Data;
using static TaskAssistant.Api.Tests.ScriptedModel;

namespace TaskAssistant.Api.Tests;

public class AgentFlowTests
{
    private static object CreateInput(string title) =>
        new { title, notes = (string?)null, due_date = "2026-11-10", priority = "normal" };

    private static JsonElement Payload(AgentBlock result) => JsonDocument.Parse(result.Content!).RootElement;

    [Fact]
    public async Task Read_tools_run_without_confirmation()
    {
        var model = new ScriptedModel(
            _ => Tools(Call("toolu_read_1", ToolNames.ListTasks, new { status = "open", search = (string?)null, due_before = (string?)null })),
            _ => Say("Você tem 1 tarefa aberta."));
        using var app = new TestApp(model);
        var http = app.CreateClient();
        await app.SeedAsync(http, "Comprar pão");

        var chat = await app.ChatAsync(http, "o que tenho para fazer?");

        Assert.Empty(chat.PendingActions);
        Assert.Equal([new ToolStepDto(ToolNames.ListTasks, "read", "executed")], chat.Steps);
        Assert.Equal("Você tem 1 tarefa aberta.", chat.Messages.Single().Text);

        var result = Assert.Single(LastToolResults(model.Calls[1]));
        Assert.Equal("toolu_read_1", result.ToolUseId);
        Assert.Equal(1, Payload(result).GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task Write_tool_does_nothing_until_confirmed()
    {
        var model = new ScriptedModel(
            _ => Tools(Call("toolu_w1", ToolNames.CreateTask, CreateInput("Pagar boleto"))),
            _ => Say("Tarefa criada."));
        using var app = new TestApp(model);
        var http = app.CreateClient();

        var chat = await app.ChatAsync(http, "anota pagar boleto para amanhã");

        var pending = Assert.Single(chat.PendingActions);
        Assert.Equal(ActionStatus.Pending, pending.Status);
        Assert.Equal("Criar a tarefa \"Pagar boleto\", prazo 10/11/2026, prioridade normal.", pending.Preview);
        Assert.Empty(await app.AllTasksAsync(http));
        Assert.Single(model.Calls); // the loop paused; the model was not called again

        var (res, body) = await app.ResolveAsync(http, pending.Id, "confirm");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(ActionStatus.Executed, body!.Action.Status);
        var task = Assert.Single(await app.AllTasksAsync(http));
        Assert.Equal("Pagar boleto", task.Title);
        Assert.Equal(new DateOnly(2026, 11, 10), task.DueDate);
        Assert.Equal("Tarefa criada.", body.Chat!.Messages.Single().Text);

        var result = Assert.Single(LastToolResults(model.Calls[1]));
        Assert.Equal("toolu_w1", result.ToolUseId);
        Assert.False(result.IsError);
        Assert.Equal("executed", Payload(result).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Reject_changes_nothing_and_tells_the_model()
    {
        var model = new ScriptedModel(
            _ => Tools(Call("toolu_w1", ToolNames.CreateTask, CreateInput("Pagar boleto"))),
            _ => Say("Ok, não criei."));
        using var app = new TestApp(model);
        var http = app.CreateClient();

        var chat = await app.ChatAsync(http, "anota pagar boleto");
        var (res, body) = await app.ResolveAsync(http, chat.PendingActions[0].Id, "reject");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(ActionStatus.Rejected, body!.Action.Status);
        Assert.Empty(await app.AllTasksAsync(http));
        var result = Assert.Single(LastToolResults(model.Calls[1]));
        Assert.Equal("toolu_w1", result.ToolUseId);
        Assert.True(result.IsError);
        Assert.Equal("rejected", Payload(result).GetProperty("status").GetString());

        var confirmAfterReject = await http.PostAsync($"/api/actions/{chat.PendingActions[0].Id}/confirm", null,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, confirmAfterReject.StatusCode);
        Assert.Empty(await app.AllTasksAsync(http));
    }

    [Fact]
    public async Task Double_confirm_executes_once()
    {
        var model = new ScriptedModel(
            _ => Tools(Call("toolu_w1", ToolNames.CreateTask, CreateInput("Pagar boleto"))),
            _ => Say("Feito."));
        using var app = new TestApp(model);
        var http = app.CreateClient();
        var chat = await app.ChatAsync(http, "anota pagar boleto");
        var id = chat.PendingActions[0].Id;

        var first = await app.ResolveAsync(http, id, "confirm");
        var second = await app.ResolveAsync(http, id, "confirm");

        Assert.Equal(HttpStatusCode.OK, first.Response.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.Response.StatusCode);
        Assert.Single(await app.AllTasksAsync(http));
        Assert.Equal(2, model.Calls.Count);
    }

    [Fact]
    public async Task Parallel_confirms_execute_once()
    {
        var model = new ScriptedModel(
            _ => Tools(Call("toolu_w1", ToolNames.CreateTask, CreateInput("Pagar boleto"))),
            _ => Say("Feito."));
        using var app = new TestApp(model);
        var http = app.CreateClient();
        var chat = await app.ChatAsync(http, "anota pagar boleto");
        var id = chat.PendingActions[0].Id;

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => app.ResolveAsync(http, id, "confirm")));

        Assert.Single(results, r => r.Response.StatusCode == HttpStatusCode.OK);
        Assert.Single(await app.AllTasksAsync(http));
    }

    [Fact]
    public async Task Stale_version_is_rejected_and_reported_to_the_model()
    {
        var model = new ScriptedModel(
            _ => Tools(Call("toolu_c1", ToolNames.CompleteTask, new { id = 1 })),
            _ => Say("A tarefa mudou, não concluí."));
        using var app = new TestApp(model);
        var http = app.CreateClient();
        var ct = TestContext.Current.CancellationToken;
        var task = await app.SeedAsync(http, "Enviar relatório");

        var chat = await app.ChatAsync(http, "conclua a 1");
        Assert.Equal(1, chat.PendingActions[0].Targets.Single().Version);

        // Someone edits the task between the proposal and the confirmation.
        (await http.PutAsJsonAsync($"/api/tasks/{task.Id}",
            new { title = "Enviar relatório final", version = 1 }, TestApp.Json, ct)).EnsureSuccessStatusCode();

        var (_, body) = await app.ResolveAsync(http, chat.PendingActions[0].Id, "confirm");

        Assert.Equal(ActionStatus.Stale, body!.Action.Status);
        var current = Assert.Single(await app.AllTasksAsync(http));
        Assert.False(current.Done);
        Assert.Equal("Enviar relatório final", current.Title);
        var result = Assert.Single(LastToolResults(model.Calls[1]));
        Assert.Equal("toolu_c1", result.ToolUseId);
        Assert.True(result.IsError);
        Assert.Equal("stale", Payload(result).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Expired_action_cannot_be_confirmed()
    {
        var model = new ScriptedModel(
            _ => Tools(Call("toolu_d1", ToolNames.DeleteTask, new { ids = new[] { 1 } })),
            _ => Say("A proposta expirou."));
        using var app = new TestApp(model);
        var http = app.CreateClient();
        await app.SeedAsync(http, "Tarefa antiga");
        var chat = await app.ChatAsync(http, "apaga a 1");

        app.Clock.Advance(TimeSpan.FromMinutes(11));
        var (res, body) = await app.ResolveAsync(http, chat.PendingActions[0].Id, "confirm");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(ActionStatus.Expired, body!.Action.Status);
        Assert.Single(await app.AllTasksAsync(http));
        var result = Assert.Single(LastToolResults(model.Calls[1]));
        Assert.Equal("toolu_d1", result.ToolUseId);
        Assert.Equal("expired", Payload(result).GetProperty("status").GetString());

        var again = await app.ResolveAsync(http, chat.PendingActions[0].Id, "confirm");
        Assert.Equal(HttpStatusCode.Conflict, again.Response.StatusCode);
    }

    [Fact]
    public async Task Mixed_turn_returns_all_results_in_one_message_in_order()
    {
        var model = new ScriptedModel(
            _ => Tools(
                Call("toolu_a", ToolNames.ListTasks, new { status = "all", search = (string?)null, due_before = (string?)null }),
                Call("toolu_b", ToolNames.CreateTask, CreateInput("Primeira")),
                Call("toolu_c", ToolNames.CreateTask, CreateInput("Segunda"))),
            _ => Say("Pronto."));
        using var app = new TestApp(model);
        var http = app.CreateClient();

        var chat = await app.ChatAsync(http, "cria duas tarefas");
        Assert.Equal(2, chat.PendingActions.Count);

        var first = await app.ResolveAsync(http, chat.PendingActions[0].Id, "confirm");
        Assert.Null(first.Body!.Chat); // still waiting on the second action
        Assert.Single(model.Calls);

        var second = await app.ResolveAsync(http, chat.PendingActions[1].Id, "reject");
        Assert.NotNull(second.Body!.Chat);

        var resultMessage = model.Calls[1][^1];
        Assert.Equal(AgentMessage.User, resultMessage.Role);
        Assert.Equal(["toolu_a", "toolu_b", "toolu_c"], resultMessage.Content.Select(b => b.ToolUseId));
        Assert.All(resultMessage.Content, b => Assert.False(string.IsNullOrEmpty(b.ToolUseId)));
        Assert.Equal(["Primeira"], (await app.AllTasksAsync(http)).Select(t => t.Title));
    }

    [Fact]
    public async Task New_message_is_blocked_while_actions_are_pending()
    {
        var model = new ScriptedModel(_ => Tools(Call("toolu_w1", ToolNames.CreateTask, CreateInput("X"))));
        using var app = new TestApp(model);
        var http = app.CreateClient();
        var chat = await app.ChatAsync(http, "cria X");

        var res = await http.PostAsJsonAsync("/api/chat", new { conversationId = chat.ConversationId, message = "e agora?" },
            TestApp.Json, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task Refusal_stops_before_running_tools()
    {
        var model = new ScriptedModel(_ => new ModelTurn(
            [Call("toolu_x", ToolNames.DeleteTask, new { ids = new[] { 1 } })], StopReasons.Refusal));
        using var app = new TestApp(model);
        var http = app.CreateClient();
        await app.SeedAsync(http, "Tarefa");

        var chat = await app.ChatAsync(http, "apaga tudo");

        Assert.Empty(chat.PendingActions);
        Assert.Empty(chat.Steps);
        Assert.Contains("recusou", chat.Messages.Single().Text);
    }

    [Fact]
    public async Task Max_tokens_stops_before_running_tools()
    {
        var model = new ScriptedModel(_ => new ModelTurn(
            [Call("toolu_x", ToolNames.CreateTask, new { title = "meio" })], StopReasons.MaxTokens));
        using var app = new TestApp(model);
        var http = app.CreateClient();

        var chat = await app.ChatAsync(http, "cria algo");

        Assert.Empty(chat.PendingActions);
        Assert.Empty(chat.Steps);
    }

    [Fact]
    public async Task Invalid_write_arguments_become_error_results_without_pending_action()
    {
        var model = new ScriptedModel(
            _ => Tools(Call("toolu_c1", ToolNames.CompleteTask, new { id = 999 })),
            _ => Say("Não achei essa tarefa."));
        using var app = new TestApp(model);
        var http = app.CreateClient();

        var chat = await app.ChatAsync(http, "conclua a 999");

        Assert.Empty(chat.PendingActions);
        Assert.Equal("error", chat.Steps.Single().Outcome);
        var result = Assert.Single(LastToolResults(model.Calls[1]));
        Assert.Equal("toolu_c1", result.ToolUseId);
        Assert.True(result.IsError);
    }

    [Fact]
    public async Task Postpone_preview_shows_old_and_new_dates()
    {
        var model = new ScriptedModel(_ => Tools(Call("toolu_u1", ToolNames.UpdateTask,
            new { id = 1, title = (string?)null, notes = (string?)null, due_date = "2026-11-20", priority = (string?)null })));
        using var app = new TestApp(model);
        var http = app.CreateClient();
        await app.SeedAsync(http, "Dentista", new DateOnly(2026, 11, 12));

        var chat = await app.ChatAsync(http, "adia o dentista para dia 20");

        Assert.Equal("Alterar a tarefa #1 \"Dentista\": prazo de 12/11/2026 para 20/11/2026.",
            chat.PendingActions.Single().Preview);
        Assert.Equal(new DateOnly(2026, 11, 12), (await app.AllTasksAsync(http)).Single().DueDate);
    }

    [Fact]
    public async Task Conversation_can_be_reloaded()
    {
        var model = new ScriptedModel(_ => Tools(Call("toolu_w1", ToolNames.CreateTask, CreateInput("X"))));
        using var app = new TestApp(model);
        var http = app.CreateClient();
        var chat = await app.ChatAsync(http, "cria X");

        var conversation = await http.GetFromJsonAsync<ConversationDto>(
            $"/api/conversations/{chat.ConversationId}", TestApp.Json, TestContext.Current.CancellationToken);

        Assert.Equal("cria X", conversation!.Messages.Single().Text);
        Assert.Equal(chat.PendingActions[0].Id, conversation.PendingActions.Single().Id);
    }
}
