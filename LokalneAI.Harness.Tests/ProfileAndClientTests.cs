using System.Diagnostics;
using System.Text;
using System.Text.Json;
using LokalneAI.Harness.Llm;
using LokalneAI.Harness.Profiles;
using LokalneAI.Harness.Session;
using LokalneAI.Harness.Tools;
using LokalneAI.Harness.Validation;

namespace LokalneAI.Harness.Tests;

/// <summary>Sprawdza profile przygotowane przez agentów: czy się wczytują i czy testy/przykłady pasują do katalogu.</summary>
public class ProfileFilesTests
{
    [Theory]
    [InlineData("tekla", 23)]
    [InlineData("cde", 1)]
    [InlineData("mkk", 0)]
    [InlineData("office", 15)]
    public void Profil_WczytujeSieZFolderu(string name, int hostTools)
    {
        var profile = HarnessProfile.Load(Paths.Profile(name));

        Assert.Equal(hostTools, profile.Tools.Count(t => !t.IsBuiltIn));
        Assert.True(profile.Examples.Count >= 10);
        Assert.True(profile.Knowledge.ChunkCount > 0);
        Assert.False(string.IsNullOrWhiteSpace(profile.CardText));
        var system = PromptBuilder.BuildSystem(profile, new SessionOptions());
        Assert.InRange(Text.EstimateTokens(system), 1000, 7000);
        var schema = StepSchema.Build(profile.Tools, profile.Settings.MaxCallsPerStep, profile.Settings.Plan);
        Assert.Equal(JsonValueKind.Object, Paths.Json(schema).ValueKind);
    }

    [Fact]
    public void Tekla_OczekiwaneWywolaniaWTestachSaZgodneZKatalogiem()
    {
        var profile = HarnessProfile.Load(Paths.Profile("tekla"));
        var problems = new List<string>();
        foreach (var line in File.ReadAllLines(Path.Combine(profile.Folder, "testy.jsonl"), Encoding.UTF8).Where(l => l.Trim().Length > 0))
        {
            var test = Paths.Json(line);
            foreach (var step in test.GetProperty("kroki").EnumerateArray())
            {
                foreach (var expected in step.GetProperty("oczekiwane").EnumerateArray())
                {
                    var tool = profile.FindTool(expected.GetProperty("narzedzie").GetString());
                    if (tool == null)
                    {
                        problems.Add(test.GetProperty("id").GetString() + ": brak narzędzia " + expected.GetProperty("narzedzie").GetString());
                        continue;
                    }

                    // Oczekiwane zawiera podzbiór argumentów, więc sprawdzamy tylko typy i zakresy podanych (bez „required”).
                    var properties = tool.Parameters.GetProperty("properties");
                    foreach (var argument in expected.GetProperty("argumenty").EnumerateObject())
                    {
                        if (!properties.TryGetProperty(argument.Name, out var schema))
                        {
                            problems.Add(test.GetProperty("id").GetString() + ": " + tool.Name + " nie ma parametru " + argument.Name);
                            continue;
                        }

                        problems.AddRange(SchemaValidator.Validate(argument.Value, schema).Select(e => test.GetProperty("id").GetString() + ": " + e));
                    }
                }
            }
        }

        Assert.Empty(problems);
    }

    [Fact]
    public void Cde_PrzykladyIWzorceToZapytaniaDoOdczytu()
    {
        var folder = Paths.Profile("cde");
        var profile = HarnessProfile.Load(folder);
        var sqls = new List<string>();
        foreach (var line in File.ReadAllLines(Path.Combine(folder, "testy.jsonl"), Encoding.UTF8).Where(l => l.Trim().Length > 0))
        {
            foreach (var step in Paths.Json(line).GetProperty("kroki").EnumerateArray())
            {
                if (step.TryGetProperty("sql_wzorcowy", out var sql) && sql.ValueKind == JsonValueKind.String)
                {
                    sqls.Add(sql.GetString());
                }
            }
        }

        foreach (var example in profile.Examples.Where(e => e.CallsJson != null))
        {
            foreach (var call in Paths.Json(example.CallsJson).EnumerateArray())
            {
                sqls.Add(call.GetProperty("argumenty").GetProperty("sql").GetString());
            }
        }

        Assert.True(sqls.Count >= 40);
        foreach (var sql in sqls)
        {
            Assert.Null(SqlGuard.Check(sql.Replace("{{ja}}", "00000000-0000-0000-0000-000000000000")));
            foreach (var rule in profile.Settings.SqlRules.Where(r => sql.IndexOf(r.When, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                Assert.Contains(rule.Requires, sql);
            }
        }
    }
}

public class LlmClientTests
{
    [Fact]
    public async Task Ollama_WysylaSchematIKontekst_CzytaTokeny()
    {
        var http = new FakeHttp(_ => FakeHttp.Json(
            "{\"model\":\"m\",\"message\":{\"role\":\"assistant\",\"content\":\"{\\\"krok\\\":\\\"odpowiedz\\\",\\\"tekst\\\":\\\"ok\\\"}\"},\"done\":true,\"prompt_eval_count\":3284,\"eval_count\":79,\"total_duration\":4000000000,\"load_duration\":0}"));
        using var client = new OllamaLlmClient(new OllamaOptions { Url = "serwer-ai:11434", Model = "bielik", ContextLength = 16384, KeepAlive = "10m" }, http);

        var response = await client.CompleteAsync(new LlmRequest
        {
            Messages = new[] { new LlmMessage("system", "Zasady: żółw"), new LlmMessage("user", "pytanie") },
            JsonSchema = "{\"type\":\"object\"}",
        }, CancellationToken.None);

        Assert.Equal(3284, response.PromptTokens);
        Assert.Equal(79, response.OutputTokens);
        Assert.Equal("{\"krok\":\"odpowiedz\",\"tekst\":\"ok\"}", response.Content);
        Assert.Equal("/api/chat", http.Paths[0]);
        var body = Paths.Json(http.Bodies[0]);
        Assert.Equal(16384, body.GetProperty("options").GetProperty("num_ctx").GetInt32());
        Assert.Equal("object", body.GetProperty("format").GetProperty("type").GetString());
        Assert.False(body.GetProperty("stream").GetBoolean());
        Assert.Equal("10m", body.GetProperty("keep_alive").GetString());
        Assert.Contains("żółw", http.Bodies[0]);
    }

    [Fact]
    public async Task Ollama_BrakModelu_DajeZrozumialyBlad()
    {
        var http = new FakeHttp(_ => FakeHttp.Json("{\"error\":\"model 'x' not found\"}", System.Net.HttpStatusCode.NotFound));
        using var client = new OllamaLlmClient(new OllamaOptions { Model = "x" }, http);

        var error = await Assert.ThrowsAsync<LlmException>(() => client.CompleteAsync(new LlmRequest { Messages = new LlmMessage[0] }, CancellationToken.None));

        Assert.Contains("ollama pull x", error.Message);
    }

    [Fact]
    public async Task Ollama_PrzedstawiaProgramBramie()
    {
        string program = null;
        var http = new FakeHttp(request =>
        {
            program = request.Headers.TryGetValues(LocalGateway.ProgramHeader, out var values) ? values.First() : null;
            return FakeHttp.Json("{\"message\":{\"content\":\"{}\"},\"done\":true}");
        });
        using var client = new OllamaLlmClient(new OllamaOptions { Url = LocalGateway.DefaultUrl, Model = "m", ClientName = "Code: tekla — słup" }, http);

        await client.CompleteAsync(new LlmRequest { Messages = new[] { new LlmMessage("user", "x") } }, CancellationToken.None);

        Assert.Equal("Code: tekla — słup", Uri.UnescapeDataString(program));
    }

    [Fact]
    public async Task LocalGateway_NieDzialajacaBramaToFalseBezWyjatku()
        => Assert.False(await LocalGateway.IsRunningAsync(300, "http://127.0.0.1:1"));

    [Fact]
    public async Task OpenAi_WysylaResponseFormat_CzytaUsage()
    {
        var http = new FakeHttp(_ => FakeHttp.Json(
            "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"{}\"}}],\"usage\":{\"prompt_tokens\":100,\"completion_tokens\":7}}"));
        using var client = new OpenAiCompatibleLlmClient(new OpenAiCompatibleOptions { Url = "http://serwer:8080/v1", Model = "qwen", ApiKey = "tajne" }, http);

        var response = await client.CompleteAsync(new LlmRequest { Messages = new[] { new LlmMessage("user", "x") }, JsonSchema = "{\"type\":\"object\"}" }, CancellationToken.None);

        Assert.Equal(100, response.PromptTokens);
        Assert.Equal("/v1/chat/completions", http.Paths[0]);
        var body = Paths.Json(http.Bodies[0]);
        Assert.Equal("json_schema", body.GetProperty("response_format").GetProperty("type").GetString());
    }
}

public class TeklaBridgeExecutorTests : IDisposable
{
    private readonly string _exchange = Path.Combine(Path.GetTempPath(), "lokalneai_most_" + Guid.NewGuid().ToString("N"));

    public TeklaBridgeExecutorTests() => Directory.CreateDirectory(_exchange);

    public void Dispose() => Directory.Delete(_exchange, true);

    private static ToolCall Call(HarnessProfile profile, string tool, string args)
        => new ToolCall(profile.FindTool(tool), Paths.Json(args), 1, 1);

    [Fact]
    public void Fill_PodstawiaLiczbyTekstyIBrakujace()
    {
        var profile = HarnessProfile.Load(Paths.Profile("tekla"));
        var executor = new TeklaBridgeExecutor(new TeklaBridgeOptions { ProfileFolder = profile.Folder, ExchangeFolder = _exchange });

        var script = executor.Render(Call(profile, "wstaw_slup", "{\"x_mm\":6000,\"y_mm\":0,\"poziom_dol_m\":0,\"poziom_gora_m\":3.53,\"profil\":\"HEB300\"}"));

        Assert.Contains("double x = 6000, y = 0, zd = 0 * 1000.0, zg = 3.53 * 1000.0;", script);
        Assert.Contains("string profil = \"HEB300\";", script);
        Assert.Contains("T(\"\", \"S235JR\")", script);
        Assert.Contains("double.NaN", script);
        Assert.DoesNotContain("{{", script);
    }

    [Fact]
    public async Task Execute_ZapisujeSkryptICzytaWynikMostu()
    {
        var profile = HarnessProfile.Load(Paths.Profile("tekla"));
        var executor = new TeklaBridgeExecutor(new TeklaBridgeOptions { ProfileFolder = profile.Folder, ExchangeFolder = _exchange, Timeout = TimeSpan.FromSeconds(10) });
        File.WriteAllText(Path.Combine(_exchange, "bridge_status.txt"), "ALIVE=teraz\nCONNECTED=True\nMODEL=test");

        // Udawany most: czeka na cmd_*.cs i odpisuje wynik.
        var bridge = Task.Run(async () =>
        {
            for (var i = 0; i < 100; i++)
            {
                var script = Directory.GetFiles(_exchange, "cmd_*.cs").FirstOrDefault();
                if (script != null)
                {
                    File.WriteAllText(Path.ChangeExtension(script, null) + ".result.txt", "OK\nOK id=617503082 (słup HEB300)");
                    return File.ReadAllText(script);
                }

                await Task.Delay(50);
            }

            return null;
        });

        var result = await executor.ExecuteAsync(Call(profile, "zmien_profil", "{\"id\":617503082,\"profil\":\"HEB320\"}"), CancellationToken.None);
        var sent = await bridge;

        Assert.True(result.Success);
        Assert.Contains("id=617503082", result.Text);
        Assert.Contains("HEB320", sent);
        Assert.Empty(Directory.GetFiles(_exchange, "_*.tmp"));
    }

    [Fact]
    public async Task Execute_MostWylaczony_DajeBlad()
    {
        var profile = HarnessProfile.Load(Paths.Profile("tekla"));
        var executor = new TeklaBridgeExecutor(new TeklaBridgeOptions { ProfileFolder = profile.Folder, ExchangeFolder = _exchange });

        var result = await executor.ExecuteAsync(Call(profile, "usun_element", "{\"id\":1}"), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("nie działa", result.Text);
        Assert.Empty(Directory.GetFiles(_exchange, "cmd_*.cs"));
    }

    /// <summary>
    /// Wypełnia szablony argumentami z testów profilu i kompiluje je kompilatorem C# 5 z bibliotekami Tekli 2019.1
    /// — tak jak zrobi to most. Pomijany, gdy na komputerze nie ma Tekli.
    /// </summary>
    [Fact]
    public void Szablony_PoWypelnieniuKompilujaSieWCSharp5()
    {
        const string csc = @"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe";
        const string tekla = @"C:\Program Files\Tekla Structures\2019.1\nt\bin\plugins";
        if (!File.Exists(csc) || !Directory.Exists(tekla))
        {
            return;
        }

        var profile = HarnessProfile.Load(Paths.Profile("tekla"));
        var executor = new TeklaBridgeExecutor(new TeklaBridgeOptions { ProfileFolder = profile.Folder, ExchangeFolder = _exchange });
        var done = new HashSet<string>();
        var failures = new List<string>();
        foreach (var line in File.ReadAllLines(Path.Combine(profile.Folder, "testy.jsonl"), Encoding.UTF8).Where(l => l.Trim().Length > 0))
        {
            foreach (var step in Paths.Json(line).GetProperty("kroki").EnumerateArray())
            {
                foreach (var expected in step.GetProperty("oczekiwane").EnumerateArray())
                {
                    var name = expected.GetProperty("narzedzie").GetString();
                    if (!done.Add(name))
                    {
                        continue;
                    }

                    var source = Path.Combine(_exchange, name + ".cs");
                    File.WriteAllText(source, executor.Render(Call(profile, name, expected.GetProperty("argumenty").GetRawText())), new UTF8Encoding(false));
                    var refs = string.Join(" ", new[] { "Tekla.Structures", "Tekla.Structures.Model", "Tekla.Structures.Drawing", "Tekla.Structures.Datatype" }
                        .Select(r => "/r:\"" + Path.Combine(tekla, r + ".dll") + "\""));
                    var start = new ProcessStartInfo(csc, "/nologo /t:library /langversion:5 /out:\"" + Path.ChangeExtension(source, ".dll") + "\" " + refs + " \"" + source + "\"")
                    {
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true,
                    };
                    using var process = Process.Start(start);
                    var output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();
                    if (process.ExitCode != 0)
                    {
                        failures.Add(name + ": " + output);
                    }
                }
            }
        }

        Assert.True(done.Count >= 15, "za mało poleceń w testach: " + done.Count);
        Assert.Empty(failures);
    }
}
