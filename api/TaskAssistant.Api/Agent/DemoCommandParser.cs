using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TaskAssistant.Api.Agent;

public record DemoCommand(string Tool, object Input);

// Rule-based parser for the offline demo. It understands a handful of fixed
// Portuguese commands and turns each one into the same tool call a real model
// would make, so the confirmation flow can be tried without an API key.
public static partial class DemoCommandParser
{
    public const string Help =
        "Modo demonstração: sem chave de API, entendo só comandos fixos. Exemplos: " +
        "listar | listar concluídas | ver 3 | criar \"pagar boleto\" para amanhã | " +
        "criar \"dentista\" para 15/11 | concluir 3 | adiar 3 para 20/11 | excluir 3 | excluir 3, 4 e 5";

    public static DemoCommand? Parse(string text, DateOnly today)
    {
        var raw = text.Trim().TrimEnd('.', '!', '?');
        var norm = Normalize(raw);

        if (ListRegex().Match(norm) is { Success: true } list)
        {
            var status = list.Groups["filter"].Value switch
            {
                "concluidas" or "feitas" => "done",
                "todas" => "all",
                _ => "open",
            };
            return new(ToolNames.ListTasks, new { status, search = (string?)null, due_before = (string?)null });
        }

        if (GetRegex().Match(norm) is { Success: true } get)
            return new(ToolNames.GetTask, new { id = int.Parse(get.Groups["id"].Value, CultureInfo.InvariantCulture) });

        if (CreateRegex().Match(raw) is { Success: true } create)
        {
            var title = create.Groups["title"].Value.Trim();
            if (title.Length == 0) return null;
            DateOnly? due = null;
            if (create.Groups["when"].Success)
            {
                due = ParseDate(Normalize(create.Groups["when"].Value), today);
                if (due is null) return null;
            }
            return new(ToolNames.CreateTask, new
            {
                title,
                notes = (string?)null,
                due_date = due?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                priority = "normal",
            });
        }

        if (CompleteRegex().Match(norm) is { Success: true } complete)
            return new(ToolNames.CompleteTask, new { id = int.Parse(complete.Groups["id"].Value, CultureInfo.InvariantCulture) });

        if (PostponeRegex().Match(norm) is { Success: true } postpone)
        {
            var date = ParseDate(postpone.Groups["when"].Value, today);
            if (date is null) return null;
            return new(ToolNames.UpdateTask, new
            {
                id = int.Parse(postpone.Groups["id"].Value, CultureInfo.InvariantCulture),
                title = (string?)null,
                notes = (string?)null,
                due_date = date.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                priority = (string?)null,
            });
        }

        if (DeleteRegex().Match(norm) is { Success: true } delete)
        {
            var ids = IdRegex().Matches(delete.Groups["ids"].Value)
                .Select(m => int.Parse(m.Value, CultureInfo.InvariantCulture))
                .Distinct()
                .ToArray();
            return ids.Length == 0 ? null : new(ToolNames.DeleteTask, new { ids });
        }

        return null;
    }

    public static DateOnly? ParseDate(string when, DateOnly today)
    {
        when = when.Trim();
        switch (when)
        {
            case "hoje": return today;
            case "amanha": return today.AddDays(1);
            case "depois de amanha": return today.AddDays(2);
        }

        var m = DateRegex().Match(when);
        if (!m.Success) return null;
        var day = int.Parse(m.Groups["d"].Value, CultureInfo.InvariantCulture);
        var month = int.Parse(m.Groups["m"].Value, CultureInfo.InvariantCulture);
        var hasYear = m.Groups["y"].Success;
        var year = hasYear ? int.Parse(m.Groups["y"].Value, CultureInfo.InvariantCulture) : today.Year;
        if (year < 100) year += 2000;
        if (month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month)) return null;

        var date = new DateOnly(year, month, day);
        // "15/01" written in November means next January.
        if (!hasYear && date < today) date = date.AddYears(1);
        return date;
    }

    private static string Normalize(string s)
    {
        var decomposed = s.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return Regex.Replace(sb.ToString().Normalize(NormalizationForm.FormC), @"\s+", " ").Trim();
    }

    [GeneratedRegex(@"^(listar|liste|lista|mostrar|mostre)( (as )?tarefas)?( (?<filter>abertas|pendentes|concluidas|feitas|todas))?$")]
    private static partial Regex ListRegex();

    [GeneratedRegex(@"^(ver|mostrar|mostre|abrir)( a)?( tarefa)? #?(?<id>\d+)$")]
    private static partial Regex GetRegex();

    [GeneratedRegex(@"^(criar|crie|adicionar|adicione|nova)( tarefa)?\s+[""“”](?<title>[^""“”]+)[""“”](\s+para\s+(?<when>.+))?$", RegexOptions.IgnoreCase)]
    private static partial Regex CreateRegex();

    [GeneratedRegex(@"^(concluir|conclua|completar|complete|finalizar|finalize)( a)?( tarefa)? #?(?<id>\d+)$")]
    private static partial Regex CompleteRegex();

    [GeneratedRegex(@"^(adiar|adie)( a)?( tarefa)? #?(?<id>\d+) para (?<when>.+)$")]
    private static partial Regex PostponeRegex();

    [GeneratedRegex(@"^(excluir|exclua|apagar|apague|remover|remova)( as| a)?( tarefas?)? (?<ids>#?\d+((\s*,\s*|\s+e\s+|\s+)#?\d+)*)$")]
    private static partial Regex DeleteRegex();

    [GeneratedRegex(@"\d+")]
    private static partial Regex IdRegex();

    [GeneratedRegex(@"^(?<d>\d{1,2})/(?<m>\d{1,2})(/(?<y>\d{2}|\d{4}))?$")]
    private static partial Regex DateRegex();
}
