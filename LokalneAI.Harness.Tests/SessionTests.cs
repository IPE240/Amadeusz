using System.Text.Json;
using LokalneAI.Harness.Profiles;
using LokalneAI.Harness.Session;
using LokalneAI.Harness.Tools;

namespace LokalneAI.Harness.Tests;

public class SessionTests
{
    private static HarnessProfile TeklaLike(FinishMode finish = FinishMode.Auto) => new HarnessProfile(
        "test",
        "Zamieniasz polecenia na wywołania.",
        new[]
        {
            new ToolDefinition("wstaw_slup", "Wstawia słup.",
                "{\"type\":\"object\",\"properties\":{\"x_mm\":{\"type\":\"number\"},\"profil\":{\"type\":\"string\"},\"poziom_gora_m\":{\"type\":\"number\",\"minimum\":-50,\"maximum\":300}},\"required\":[\"x_mm\",\"profil\"],\"additionalProperties\":false}",
                mutates: true),
            new ToolDefinition("zmien_profil", "Zmienia profil.",
                "{\"type\":\"object\",\"properties\":{\"id\":{\"type\":\"integer\"},\"profil\":{\"type\":\"string\"}},\"required\":[\"id\",\"profil\"],\"additionalProperties\":false}",
                mutates: true),
        },
        new ProfileSettings { Finish = finish, ReferencePreviousId = true });

    private static HarnessProfile CdeLike() => new HarnessProfile(
        "cde",
        "Odpowiadasz na pytania o dane.",
        new[]
        {
            new ToolDefinition("zapytanie_sql", "Jedno zapytanie SELECT.",
                "{\"type\":\"object\",\"properties\":{\"sql\":{\"type\":\"string\"},\"cel\":{\"type\":\"string\"}},\"required\":[\"sql\",\"cel\"],\"additionalProperties\":false}"),
        },
        new ProfileSettings
        {
            CheckNumbers = true,
            SqlTools = new List<string> { "zapytanie_sql" },
            BuiltInTools = new List<string> { "kalkulator" },
            SqlRules = new List<SqlRule> { new SqlRule { When = "v_my_hours", Requires = "{{ja}}", Message = "filtruj po {{ja}}" } },
            ForbiddenRelations = new List<string> { "people.", "timesheet.entry" },
        });

    [Fact]
    public async Task Tekla_WstawISpytajOPoprawe_TrzymaIdWSesji()
    {
        var llm = new ScriptedLlm(
            "{\"krok\":\"wywolania\",\"wywolania\":[{\"narzedzie\":\"wstaw_slup\",\"argumenty\":{\"x_mm\":6000,\"profil\":\"HEB300\",\"poziom_gora_m\":3.53}}]}",
            "{\"krok\":\"wywolania\",\"wywolania\":[{\"narzedzie\":\"zmien_profil\",\"argumenty\":{\"id\":1001,\"profil\":\"HEB320\"}}]}");
        var model = new SimulatedModelExecutor();
        using var session = new HarnessSession(llm, TeklaLike(), model);

        var first = await session.SendAsync("wstaw słup HEB300 w x=6000 do 3,53");
        var second = await session.SendAsync("zmień go na HEB320");

        Assert.Equal(TurnKind.Answer, first.Kind);
        Assert.Equal(1, first.ModelCalls);
        Assert.Contains("id=1001", first.Text);
        Assert.Equal("HEB320", model.Objects[1001]["profil"]);
        Assert.True(second.Calls.Single().Success);
        // Druga tura widzi wynik pierwszej (id=1001) w historii.
        Assert.Contains(llm.Requests[1].Messages, m => m.Content.Contains("id=1001"));
        Assert.False(session.Context.IsEstimate);
        Assert.Equal(1284, session.Context.UsedTokens);
    }

    [Fact]
    public async Task IdZero_OznaczaElementZPoprzedniegoWywolania()
    {
        var llm = new ScriptedLlm(
            "{\"krok\":\"wywolania\",\"wywolania\":[{\"narzedzie\":\"wstaw_slup\",\"argumenty\":{\"x_mm\":0,\"profil\":\"HEB300\"}},{\"narzedzie\":\"zmien_profil\",\"argumenty\":{\"id\":0,\"profil\":\"HEB400\"}}]}");
        var model = new SimulatedModelExecutor();
        using var session = new HarnessSession(llm, TeklaLike(), model);

        var turn = await session.SendAsync("wstaw słup i od razu zmień na HEB400");

        Assert.Equal("{\"id\":1001,\"profil\":\"HEB400\"}", turn.Calls[1].ArgumentsJson);
        Assert.Equal("HEB400", model.Objects[1001]["profil"]);
    }

    [Fact]
    public async Task ZleArgumenty_WracajaDoModelu_ANieDoGospodarza()
    {
        var llm = new ScriptedLlm(
            "{\"krok\":\"wywolania\",\"wywolania\":[{\"narzedzie\":\"wstaw_slup\",\"argumenty\":{\"x_mm\":0,\"profil\":\"HEB300\",\"poziom_gora_m\":3530}}]}",
            "{\"krok\":\"wywolania\",\"wywolania\":[{\"narzedzie\":\"wstaw_slup\",\"argumenty\":{\"x_mm\":0,\"profil\":\"HEB300\",\"poziom_gora_m\":3.53}}]}");
        var model = new SimulatedModelExecutor();
        using var session = new HarnessSession(llm, TeklaLike(), model);

        var turn = await session.SendAsync("wstaw słup do 3,53");

        Assert.False(turn.Calls[0].Success);
        Assert.Contains("większe niż 300", turn.Calls[0].Result);
        Assert.True(turn.Calls[1].Success);
        Assert.Single(model.Objects);
        Assert.Contains(llm.Requests[1].Messages, m => m.Content.Contains("BŁĄD"));
    }

    [Fact]
    public async Task OdmowaUzytkownika_NieWykonujePolecenia()
    {
        var llm = new ScriptedLlm(
            "{\"krok\":\"wywolania\",\"wywolania\":[{\"narzedzie\":\"wstaw_slup\",\"argumenty\":{\"x_mm\":0,\"profil\":\"HEB300\"}}]}",
            "{\"krok\":\"odpowiedz\",\"tekst\":\"Nie wstawiono słupa.\"}");
        var model = new SimulatedModelExecutor();
        var options = new SessionOptions { ConfirmMutation = (call, ct) => Task.FromResult(false) };
        using var session = new HarnessSession(llm, TeklaLike(), model, options);

        var turn = await session.SendAsync("wstaw słup");

        Assert.Empty(model.Objects);
        Assert.Contains("ODRZUCONE", turn.Calls[0].Result);
        Assert.Equal("Nie wstawiono słupa.", turn.Text);
    }

    [Fact]
    public async Task Cde_PodstawiaJaISprawdzaLiczbyOdpowiedzi()
    {
        string executedSql = null;
        var executor = new DelegateExecutor((call, ct) =>
        {
            executedSql = call.GetString("sql");
            return Task.FromResult(ToolResult.Ok("godziny_liczone\n845.00"));
        });
        var llm = new ScriptedLlm(
            "{\"krok\":\"wywolania\",\"wywolania\":[{\"narzedzie\":\"zapytanie_sql\",\"argumenty\":{\"sql\":\"SELECT SUM(h.hours_counted) FROM timesheet.v_my_hours h WHERE h.person_id = '{{ja}}'\",\"cel\":\"moje godziny\"}}]}",
            "{\"krok\":\"odpowiedz\",\"tekst\":\"Przepracowałeś 62,25 h.\"}",
            "{\"krok\":\"odpowiedz\",\"tekst\":\"Przepracowałeś 845,00 h.\"}");
        var options = new SessionOptions { Variables = new Dictionary<string, string> { ["ja"] = "00000000-0000-4000-8000-000000000042" } };
        using var session = new HarnessSession(llm, CdeLike(), executor, options);

        var turn = await session.SendAsync("Ile przepracowałem godzin?");

        Assert.Contains("'00000000-0000-4000-8000-000000000042'", executedSql);
        Assert.Equal("Przepracowałeś 845,00 h.", turn.Text);
        Assert.True(turn.NumbersVerified);
        Assert.Equal(3, turn.ModelCalls);
        Assert.Contains(turn.Warnings, w => w.Contains("62,25"));
    }

    [Theory]
    [InlineData("SELECT SUM(h.hours_counted) FROM timesheet.v_my_hours h", "filtruj po")]
    [InlineData("SELECT p.display_name FROM people.person p", "niedostępna")]
    [InlineData("SELECT * FROM timesheet.entry", "niedostępna")]
    [InlineData("DELETE FROM project.project", "tylko zapytania SELECT")]
    public async Task Cde_ZakazaneZapytania_NieTrafiajaDoBazy(string sql, string message)
    {
        var executed = 0;
        var executor = new DelegateExecutor((call, ct) => { executed++; return Task.FromResult(ToolResult.Ok("x")); });
        var step = "{\"krok\":\"wywolania\",\"wywolania\":[{\"narzedzie\":\"zapytanie_sql\",\"argumenty\":{\"sql\":" + JsonSerializer.Serialize(sql) + ",\"cel\":\"test\"}}]}";
        var llm = new ScriptedLlm(step, "{\"krok\":\"pytanie\",\"tekst\":\"Doprecyzuj.\"}");
        using var session = new HarnessSession(llm, CdeLike(), executor,
            new SessionOptions { Variables = new Dictionary<string, string> { ["ja"] = "u" } });

        var turn = await session.SendAsync("pytanie");

        Assert.Equal(0, executed);
        Assert.False(turn.Calls[0].Success);
        Assert.Contains(message, turn.Calls[0].Result, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(TurnKind.Question, turn.Kind);
    }

    [Fact]
    public async Task Kalkulator_JestWbudowany()
    {
        var llm = new ScriptedLlm(
            "{\"krok\":\"wywolania\",\"wywolania\":[{\"narzedzie\":\"kalkulator\",\"argumenty\":{\"wyrazenie\":\"7,5+8+6,5+4,25+8+7,75+3,5+9+2,25+6\"}}]}",
            "{\"krok\":\"odpowiedz\",\"tekst\":\"Suma to 62,75 h.\"}");
        using var session = new HarnessSession(llm, CdeLike(), null);

        var turn = await session.SendAsync("zsumuj godziny");

        Assert.Equal("Suma to 62,75 h.", turn.Text);
        Assert.Contains("= 62,75", turn.Calls[0].Result);
        Assert.True(turn.NumbersVerified);
    }

    [Fact]
    public async Task NiepoprawnyKrok_ModelDostajeUwageIProbujeDalej()
    {
        var llm = new ScriptedLlm("to nie json", "{\"krok\":\"pytanie\",\"tekst\":\"Jaki profil?\"}");
        using var session = new HarnessSession(llm, TeklaLike(), new SimulatedModelExecutor());

        var turn = await session.SendAsync("wstaw słup");

        Assert.Equal(TurnKind.Question, turn.Kind);
        Assert.Equal("Jaki profil?", turn.Text);
        Assert.Contains(turn.Warnings, w => w.Contains("niepoprawny krok"));
    }

    [Fact]
    public async Task LimitKrokow_KonczyTureBledem()
    {
        var call = "{\"krok\":\"wywolania\",\"wywolania\":[{\"narzedzie\":\"zmien_profil\",\"argumenty\":{\"id\":5,\"profil\":\"HEB300\"}}]}";
        var profile = TeklaLike(FinishMode.Model);
        var llm = new ScriptedLlm(Enumerable.Repeat(call, 10).ToArray());
        using var session = new HarnessSession(llm, profile, new SimulatedModelExecutor());

        var turn = await session.SendAsync("zmień");

        Assert.Equal(TurnKind.Failed, turn.Kind);
        Assert.Equal(profile.Settings.MaxSteps, turn.ModelCalls);
    }

    [Fact]
    public async Task Dispose_KasujeHistorieIZamykaSesje()
    {
        var llm = new ScriptedLlm("{\"krok\":\"odpowiedz\",\"tekst\":\"ok\"}");
        var session = new HarnessSession(llm, TeklaLike(), new SimulatedModelExecutor());
        await session.SendAsync("cześć");
        Assert.NotEmpty(session.History);

        session.Dispose();

        Assert.Empty(session.History);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.SendAsync("jeszcze"));
    }

    [Fact]
    public async Task PelnyKontekst_SkracaStarszaCzescSesji()
    {
        var answers = Enumerable.Range(0, 12).Select(i => "{\"krok\":\"odpowiedz\",\"tekst\":\"" + new string('x', 400) + "\"}").ToArray();
        var llm = new ScriptedLlm(answers) { ContextLength = 1200, PromptTokens = 0 };
        using var session = new HarnessSession(llm, TeklaLike(), new SimulatedModelExecutor(), new SessionOptions { KeepRecentMessages = 4 });
        var warnings = new List<string>();

        for (var i = 0; i < 12; i++)
        {
            warnings.AddRange((await session.SendAsync("polecenie numer " + i + " " + new string('y', 300))).Warnings);
        }

        Assert.Contains(warnings, w => w.Contains("skrócona"));
        Assert.True(session.History.Count <= 4 + 2 + 2);
        Assert.StartsWith("Skrót wcześniejszej", session.History[0].Content);
    }

    [Fact]
    public void Schemat_ZawieraWszystkieNarzedziaITrzyWarianty()
    {
        var schema = StepSchema.Build(TeklaLike().Tools, 4, withPlan: false);
        var root = Paths.Json(schema);

        var variants = root.GetProperty("anyOf");
        Assert.Equal(3, variants.GetArrayLength());
        Assert.Contains("\"wstaw_slup\"", schema);
        Assert.Contains("\"zmien_profil\"", schema);
        Assert.Contains("\"maxItems\":4", schema);
    }
}
