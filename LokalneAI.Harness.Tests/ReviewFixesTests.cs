using LokalneAI.Harness.Llm;
using LokalneAI.Harness.Profiles;
using LokalneAI.Harness.Session;
using LokalneAI.Harness.Tools;
using LokalneAI.Harness.Validation;

namespace LokalneAI.Harness.Tests;

/// <summary>Testy błędów znalezionych w przeglądzie kodu (obejścia strażnika SQL, wyjątki, wyścigi).</summary>
public class ReviewFixesTests
{
    [Theory]
    [InlineData("select E'\\'' ; commit; set transaction read write; drop table x; --'")]
    [InlineData("select 1 -- x\r; commit; drop table t")]
    [InlineData("select 1 as a$b$; commit; drop table t; -- $b$")]
    [InlineData("select \"pg_read_file\"('/etc/passwd')")]
    [InlineData("select pg_catalog.\"set_config\"('x','y',false)")]
    [InlineData("select table_to_xml('timesheet.entry', true, false, '')")]
    [InlineData("select query_to_xml_and_xmlschema('select * from people.person', true, false, '')")]
    [InlineData("select pg_try_advisory_lock(1)")]
    [InlineData("select 1 /* zagnieżdżony /* komentarz */ ; drop table t; */")]
    public void SqlGuard_ZnaneObejscia_SaOdrzucane(string sql)
    {
        // Zagnieżdżony komentarz w ostatnim przypadku jest poprawny i nic nie ukrywa — ma przejść jako SELECT 1.
        var problem = SqlGuard.Check(sql);
        if (sql.Contains("zagnieżdżony"))
        {
            Assert.Null(problem);
        }
        else
        {
            Assert.NotNull(problem);
        }
    }

    [Fact]
    public void SqlGuard_Normalize_ZdejmujeCudzyslowyISpacjePrzyKropce()
    {
        var normalized = SqlGuard.Normalize("select * from \"timesheet\" . \"Entry\" e join people/**/.person p on true");

        Assert.Contains("timesheet.entry", normalized);
        Assert.Contains("people.person", normalized);
    }

    private static HarnessProfile Cde() => new HarnessProfile(
        "cde",
        "Odpowiadasz na pytania o dane.",
        new[] { new ToolDefinition("zapytanie_sql", "SELECT.", "{\"type\":\"object\",\"properties\":{\"sql\":{\"type\":\"string\"}},\"required\":[\"sql\"],\"additionalProperties\":false}") },
        new ProfileSettings
        {
            SqlTools = new List<string> { "zapytanie_sql" },
            SqlRules = new List<SqlRule> { new SqlRule { When = "v_my_hours", Requires = "{{ja}}", RequiresPattern = @"person_id\s*=\s*'\{\{ja\}\}'", Message = "filtruj po osobie" } },
            ForbiddenRelations = new List<string> { "timesheet.entry", "people." },
        });

    [Theory]
    [InlineData("SELECT * FROM timesheet.v_my_hours h /* {{ja}} */", false)]
    [InlineData("SELECT * FROM timesheet.v_my_hours h WHERE '{{ja}}' = '{{ja}}'", false)]
    [InlineData("SELECT * FROM timesheet.v_my_hours h WHERE h.person_id = '{{ja}}'", true)]
    [InlineData("SELECT * FROM \"timesheet\" . \"entry\"", false)]
    [InlineData("SELECT * FROM people . person", false)]
    public async Task Cde_RegulyIRelacje_PoNormalizacji(string sql, bool allowed)
    {
        var executed = 0;
        var executor = new DelegateExecutor((c, ct) => { executed++; return Task.FromResult(ToolResult.Ok("x")); });
        var step = "{\"krok\":\"wywolania\",\"wywolania\":[{\"narzedzie\":\"zapytanie_sql\",\"argumenty\":{\"sql\":" + System.Text.Json.JsonSerializer.Serialize(sql) + "}}]}";
        using var session = new HarnessSession(new ScriptedLlm(step, "{\"krok\":\"odpowiedz\",\"tekst\":\"ok\"}"), Cde(), executor,
            new SessionOptions { Variables = new Dictionary<string, string> { ["ja"] = "u-1" } });

        await session.SendAsync("pytanie");

        Assert.Equal(allowed ? 1 : 0, executed);
    }

    [Theory]
    [InlineData("max(3,5)", "5")]
    [InlineData("suma(7,5; 8)", "15,5")]
    [InlineData("suma(1, 2, 3)", "6")]
    [InlineData("zaokraglij(2,5)", "2")]
    [InlineData("zaokraglij(1,23456; 30)", "1,23456")]
    public void Kalkulator_PrzecinekWFunkcjachIZaokraglenie(string expression, string expected)
        => Assert.Equal(expected, Calculator.Format(Calculator.Evaluate(expression)));

    [Fact]
    public void Kalkulator_ZbytGlebokieWyrazenie_DajeBladZamiastPrzepelnieniaStosu()
        => Assert.Throws<FormatException>(() => Calculator.Evaluate(new string('(', 5000) + "1" + new string(')', 5000)));

    [Fact]
    public async Task ZamkniecieWTrakcieTury_NiePsujeHistoriiIKonczyTure()
    {
        var gate = new TaskCompletionSource<LlmResponse>();
        var llm = new GatedLlm(gate.Task);
        var session = new HarnessSession(llm, Cde(), null);

        var turn = session.SendAsync("pytanie");
        await llm.Started.Task;
        session.Dispose();
        gate.SetResult(new LlmResponse { Content = "{\"krok\":\"pytanie\",\"tekst\":\"?\"}", PromptTokens = 10 });
        try
        {
            await turn;
        }
        catch (OperationCanceledException)
        {
            // Też poprawny koniec.
        }

        Assert.Empty(session.History);
    }

    [Fact]
    public async Task IdZero_NieWskazujeElementuZOdczytu()
    {
        var profile = new HarnessProfile("t", "x", new[]
        {
            new ToolDefinition("odczytaj", "Odczyt.", "{\"type\":\"object\",\"properties\":{\"id\":{\"type\":\"integer\"}},\"required\":[\"id\"]}"),
            new ToolDefinition("usun", "Usuwa.", "{\"type\":\"object\",\"properties\":{\"id\":{\"type\":\"integer\"}},\"required\":[\"id\"]}", mutates: true),
        }, new ProfileSettings { ReferencePreviousId = true });
        var deleted = new List<string>();
        var executor = new DelegateExecutor((c, ct) =>
        {
            if (c.Name == "usun")
            {
                deleted.Add(c.ArgumentsJson);
            }

            return Task.FromResult(ToolResult.Ok("OK id=555"));
        });
        var llm = new ScriptedLlm(
            "{\"krok\":\"wywolania\",\"wywolania\":[{\"narzedzie\":\"odczytaj\",\"argumenty\":{\"id\":555}},{\"narzedzie\":\"usun\",\"argumenty\":{\"id\":0}}]}",
            "{\"krok\":\"pytanie\",\"tekst\":\"Który element usunąć?\"}");
        using var session = new HarnessSession(llm, profile, executor);

        var turn = await session.SendAsync("odczytaj 555 i usuń to");

        Assert.Empty(deleted);
        Assert.False(turn.Calls[1].Success);
    }

    private sealed class GatedLlm : ILlmClient
    {
        private readonly Task<LlmResponse> _response;

        public GatedLlm(Task<LlmResponse> response) => _response = response;

        public TaskCompletionSource<bool> Started { get; } = new TaskCompletionSource<bool>();

        public string ModelName => "bramka";

        public int ContextLength => 8192;

        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
        {
            Started.TrySetResult(true);
            return _response;
        }
    }
}

public class TeklaBridgeReviewTests : IDisposable
{
    private readonly string _exchange = Path.Combine(Path.GetTempPath(), "lokalneai_most2_" + Guid.NewGuid().ToString("N"));

    public TeklaBridgeReviewTests() => Directory.CreateDirectory(_exchange);

    public void Dispose() => Directory.Delete(_exchange, true);

    private ToolCall Call(HarnessProfile profile) => new ToolCall(profile.FindTool("usun_element"), Paths.Json("{\"id\":7}"), 1, 1);

    [Fact]
    public async Task WynikOkZBledemSzablonu_ToBlad()
    {
        var profile = HarnessProfile.Load(Paths.Profile("tekla"));
        File.WriteAllText(Path.Combine(_exchange, "bridge_status.txt"), "CONNECTED=True");
        var executor = new TeklaBridgeExecutor(new TeklaBridgeOptions { ExchangeFolder = _exchange, ProfileFolder = profile.Folder, Timeout = TimeSpan.FromSeconds(10) });
        var bridge = Task.Run(async () =>
        {
            for (var i = 0; i < 100; i++)
            {
                var script = Directory.GetFiles(_exchange, "cmd_*.cs").FirstOrDefault();
                if (script != null)
                {
                    File.WriteAllText(Path.ChangeExtension(script, null) + ".result.txt", "OK\nBLAD: nie ma elementu id=7");
                    return;
                }

                await Task.Delay(50);
            }
        });

        var result = await executor.ExecuteAsync(Call(profile), CancellationToken.None);
        await bridge;

        Assert.False(result.Success);
        Assert.Contains("nie ma elementu", result.Text);
    }

    [Fact]
    public async Task MostNieOdpowiada_SkryptJestWycofany()
    {
        var profile = HarnessProfile.Load(Paths.Profile("tekla"));
        File.WriteAllText(Path.Combine(_exchange, "bridge_status.txt"), "CONNECTED=True");
        var executor = new TeklaBridgeExecutor(new TeklaBridgeOptions { ExchangeFolder = _exchange, ProfileFolder = profile.Folder, Timeout = TimeSpan.FromMilliseconds(600) });

        var result = await executor.ExecuteAsync(Call(profile), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("nic nie wykonano", result.Text);
        Assert.Empty(Directory.GetFiles(_exchange, "cmd_*.cs"));
    }
}
