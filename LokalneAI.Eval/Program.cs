using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using LokalneAI.Harness.Llm;
using LokalneAI.Harness.Postgres;
using LokalneAI.Harness.Profiles;
using LokalneAI.Harness.Session;
using LokalneAI.Harness.Tools;
using LokalneAI.Harness.Validation;

// LokalneAI.Eval — sprawdza model na zestawie testów profilu.
//
//   LokalneAI.Eval rozmiar --profil profile\tekla [--model X] [--ctx 8192]
//   LokalneAI.Eval test --profil profile\tekla --model X [--ctx 8192] [--limit N] [--wyniki folder]
//   LokalneAI.Eval test --profil profile\cde --model X --polaczenie polaczenie.local.json --ja <uuid>
//   LokalneAI.Eval wiedza --profil profile\cde                       (aktualizacja wiedzy z zrodla.json)
//   LokalneAI.Eval szukaj --profil profile\tekla --fraza "kotwy M24" (co dostałby model z szukaj_wiedzy)
//   LokalneAI.Eval straznik --plik korpus.jsonl                      (strażnik bezpieczeństwa na liście wywołań; nic nie wykonuje)
//   LokalneAI.Eval atak --plik scenariusze.jsonl --model X            (wstrzyknięcia na prawdziwym modelu, wykonawca-atrapa)
//
// Wyniki: <wyniki>\<profil>_<model>_<czas>.json (pełne odpowiedzi — zostają na tym PC) i podsumowanie na ekranie.
Console.OutputEncoding = Encoding.UTF8;
var options = Args.Parse(args);
if (options.Command is not ("rozmiar" or "test" or "wiedza" or "szukaj" or "straznik" or "atak"))
{
    Console.WriteLine("Użycie: LokalneAI.Eval (rozmiar|test|wiedza|szukaj) --profil <folder> [--model X] [--url http://127.0.0.1:11434] [--ctx 8192]");
    Console.WriteLine("        [--limit N] [--wyniki folder] [--polaczenie plik.json] [--ja uuid] [--plan] [--temp 0.1] [--fraza \"słowa\"]");
    Console.WriteLine("        LokalneAI.Eval straznik --plik korpus.jsonl | atak --plik scenariusze.jsonl --model X [--tylko W01,W02]");
    return 1;
}

if (options.Command == "straznik")
{
    // Strażnik bezpieczeństwa na liście wywołań (korpus JSONL) — nic nie jest wykonywane.
    return SafetyEval.Corpus(options.File, options.Results);
}

if (options.Command == "atak")
{
    // Scenariusze wstrzyknięć na prawdziwym modelu; wykonawca-atrapa niczego nie wykonuje.
    return await SafetyEval.AttackAsync(options.File, options);
}

if (options.Command == "szukaj")
{
    // Dokładnie to, co dostałby model z narzędzia szukaj_wiedzy — do sprawdzania notatek w wiedza\.
    var searched = HarnessProfile.Load(options.Profile);
    var hits = searched.Knowledge.Search(options.Phrase ?? "");
    Console.WriteLine($"{searched.Name}: fragmentów wiedzy {searched.Knowledge.ChunkCount}, trafień {hits.Count} dla „{options.Phrase}”");
    if (!searched.Settings.BuiltInTools.Contains(BuiltInTools.KnowledgeName))
    {
        Console.WriteLine($"UWAGA: profil nie ma narzędzia {BuiltInTools.KnowledgeName} (\"narzedzia_wbudowane\" w profil.json), więc model nie sięga do wiedzy.");
    }

    foreach (var hit in hits)
    {
        Console.WriteLine();
        Console.WriteLine(hit);
    }

    return hits.Count > 0 ? 0 : 3;
}

if (options.Command == "wiedza")
{
    // Aktualizacja dokumentacji profilu ze źródeł w zrodla.json (np. w Harmonogramie zadań co noc).
    var cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LokalneAI", "repo");
    var sync = await KnowledgeSync.SyncAsync(options.Profile, cache);
    Console.WriteLine($"{options.Profile}: {sync}");
    return sync.Errors.Count == 0 ? 0 : 2;
}

var profile = HarnessProfile.Load(options.Profile);
if (options.Plan)
{
    profile.Settings.Plan = true;
}

using var llm = new OllamaLlmClient(new OllamaOptions
{
    Url = options.Url,
    Model = options.Model,
    ContextLength = options.Context,
    Temperature = options.Temperature,
    KeepAlive = "10m",
});

if (options.Command == "rozmiar")
{
    return await Size.RunAsync(profile, llm);
}

return await Evaluation.RunAsync(profile, llm, options);

internal sealed class Args
{
    public string Command { get; set; }
    public string Profile { get; set; }
    public string Model { get; set; } = "SpeakLeash/bielik-11b-v3.0-instruct:Q4_K_M";
    public string Url { get; set; } = "http://127.0.0.1:11434";
    public int Context { get; set; } = 8192;
    public int Limit { get; set; } = int.MaxValue;
    public string Results { get; set; } = "wyniki";
    public string Connection { get; set; }
    public string Me { get; set; }
    public bool Plan { get; set; }
    public double Temperature { get; set; } = 0.1;
    public string Only { get; set; }
    public string Phrase { get; set; }
    public string File { get; set; }
    public bool NoSafetyPrompt { get; set; }
    public bool ForceRead { get; set; }

    public static Args Parse(string[] args)
    {
        var a = new Args { Command = args.Length > 0 ? args[0] : null };
        for (var i = 1; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : "";
            switch (args[i])
            {
                case "--profil": a.Profile = Next(); break;
                case "--model": a.Model = Next(); break;
                case "--url": a.Url = Next(); break;
                case "--ctx": a.Context = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--limit": a.Limit = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--wyniki": a.Results = Next(); break;
                case "--polaczenie": a.Connection = Next(); break;
                case "--ja": a.Me = Next(); break;
                case "--plan": a.Plan = true; break;
                case "--temp": a.Temperature = double.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--tylko": a.Only = Next(); break;
                case "--fraza": a.Phrase = Next(); break;
                case "--plik": a.File = Next(); break;
                case "--bez-zasad": a.NoSafetyPrompt = true; break;
                case "--wymus-czytanie": a.ForceRead = true; break;
            }
        }

        return a;
    }
}

internal static class Size
{
    public static async Task<int> RunAsync(HarnessProfile profile, ILlmClient llm)
    {
        var system = PromptBuilder.BuildSystem(profile, new SessionOptions { ContextNote = "Pytający: {{ja}}." });
        int Est(string s) => (int)Math.Ceiling((s ?? "").Length / 3.2);
        Console.WriteLine($"Profil {profile.Name}: narzędzia {profile.Tools.Count}, przykłady {profile.Examples.Count}, fragmenty wiedzy {profile.Knowledge.ChunkCount}");
        Console.WriteLine($"  system.md ~{Est(profile.SystemText)} tok., karta.md ~{Est(profile.CardText)} tok., narzędzia ~{Est(string.Join("\n", profile.Tools.Select(t => t.DescribeCompact())))} tok. (pełne ~{Est(string.Join("\n", profile.Tools.Select(t => t.DescribeForPrompt())))})");
        Console.WriteLine($"  całe polecenie systemowe: {system.Length} znaków, szacunek ~{Est(system)} tok.");
        var response = await llm.CompleteAsync(new LlmRequest
        {
            Messages = new[] { new LlmMessage("system", system), new LlmMessage("user", "Odpowiedz jednym słowem: gotowy?") },
            JsonSchema = StepSchema.Build(Array.Empty<ToolDefinition>(), 1, false),
        }, CancellationToken.None);
        Console.WriteLine($"  {llm.ModelName}: prawdziwie {response.PromptTokens} tokenów wejścia (z pytaniem), kontekst {llm.ContextLength}");
        return 0;
    }
}

internal static class Evaluation
{
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static async Task<int> RunAsync(HarnessProfile profile, ILlmClient llm, Args options)
    {
        var testsPath = Path.Combine(profile.Folder, "testy.jsonl");
        var tests = File.ReadAllLines(testsPath, Encoding.UTF8).Where(l => l.Trim().Length > 0).Select(l => JsonDocument.Parse(l).RootElement.Clone())
            .Where(t => options.Only == null || options.Only.Split(',').Contains(t.GetProperty("id").GetString()))
            .Take(options.Limit).ToList();
        var isSql = profile.Settings.SqlTools.Count > 0;
        PostgresReadOnlyExecutor database = null;
        var variables = new Dictionary<string, string>();
        if (isSql)
        {
            database = new PostgresReadOnlyExecutor(new PostgresReadOnlyOptions { ConnectionString = ConnectionFromJson(options.Connection) });
            variables["ja"] = options.Me ?? throw new ArgumentException("Profil SQL wymaga --ja <uuid pytającego>.");
        }

        Console.WriteLine($"Model {llm.ModelName}, profil {profile.Name}, testów {tests.Count}, kontekst {llm.ContextLength}");
        var report = new List<object>();
        int steps = 0, strict = 0, lenient = 0, answerOk = 0;
        double seconds = 0;
        var maxContext = 0;
        var totalClock = Stopwatch.StartNew();

        foreach (var test in tests)
        {
            var id = test.GetProperty("id").GetString();
            var scripted = new ScriptedExecutor();
            IToolExecutor executor = isSql ? database : scripted;
            var sessionOptions = new SessionOptions
            {
                Variables = variables,
                ContextNote = isSql ? "Pytający ma w bazie identyfikator osoby {{ja}} — w SQL wpisuj dosłownie '{{ja}}', program podstawi wartość." : null,
            };
            using var session = new HarnessSession(llm, profile, executor, sessionOptions);
            var stepIndex = 0;
            foreach (var step in test.GetProperty("kroki").EnumerateArray())
            {
                stepIndex++;
                steps++;
                var user = step.GetProperty("uzytkownik").GetString();
                var expected = step.TryGetProperty("oczekiwane", out var e) ? e.EnumerateArray().ToList() : new List<JsonElement>();
                var results = step.TryGetProperty("wyniki", out var w) ? w.EnumerateArray().Select(x => x.GetString()).ToList() : new List<string>();
                var expectQuestion = step.TryGetProperty("pytanie", out var q) && q.ValueKind == JsonValueKind.True;
                if (!isSql)
                {
                    scripted.Expect(expected.Select((x, i) => new ExpectedCall
                    {
                        Tool = x.GetProperty("narzedzie").GetString(),
                        Arguments = x.GetProperty("argumenty").Clone(),
                        Result = i < results.Count ? results[i] : "OK",
                    }).ToList());
                }

                TurnResult turn;
                string error = null;
                var clock = Stopwatch.StartNew();
                try
                {
                    turn = await session.SendAsync(user);
                }
                catch (Exception ex) when (ex is LlmException or InvalidOperationException or Npgsql.NpgsqlException or IOException)
                {
                    turn = new TurnResult { Kind = TurnKind.Failed, Text = ex.Message };
                    error = ex.Message;
                }

                seconds += clock.Elapsed.TotalSeconds;
                maxContext = Math.Max(maxContext, session.Context.UsedTokens);
                bool okStrict, okLenient, okAnswer = true;
                string detail;
                if (isSql)
                {
                    try
                    {
                        (okStrict, okAnswer, detail) = await ScoreSqlAsync(step, turn, database, variables, expectQuestion);
                    }
                    catch (Exception ex) when (ex is Npgsql.NpgsqlException or IOException or TimeoutException)
                    {
                        // Awaria bazy przy zapytaniu wzorcowym nie przerywa całego porównania.
                        (okStrict, okAnswer, detail) = (false, false, "BŁĄD bazy przy ocenie: " + ex.Message);
                        error ??= detail;
                    }

                    okLenient = okStrict;
                }
                else
                {
                    (okStrict, okLenient, detail) = ScoreCommands(expected, expectQuestion, turn, scripted);
                }

                strict += okStrict ? 1 : 0;
                lenient += okLenient ? 1 : 0;
                answerOk += okAnswer ? 1 : 0;
                var mark = okStrict ? "OK " : okLenient ? "~  " : "ŹLE";
                Console.WriteLine($"{mark} {id}/{stepIndex} {clock.Elapsed.TotalSeconds,5:0.0}s {turn.ModelCalls} wyw. {Shorten(user, 60)} → {Shorten(detail, 110)}");
                report.Add(new
                {
                    test = id,
                    krok = stepIndex,
                    uzytkownik = user,
                    ok = okStrict,
                    ok_ostatecznie = okLenient,
                    ok_odpowiedz = okAnswer,
                    szczegoly = detail,
                    rodzaj = turn.Kind.ToString(),
                    odpowiedz = turn.Text,
                    wywolania = turn.Calls.Select(c => c.ToString()).ToList(),
                    kroki_modelu = turn.Steps,
                    ostrzezenia = turn.Warnings,
                    sekundy = Math.Round(clock.Elapsed.TotalSeconds, 2),
                    wywolania_modelu = turn.ModelCalls,
                    kontekst = session.Context.UsedTokens,
                    blad = error,
                });
            }
        }

        var summary = new
        {
            model = llm.ModelName,
            profil = profile.Name,
            kontekst = llm.ContextLength,
            plan = profile.Settings.Plan,
            krokow = steps,
            trafne = strict,
            trafne_ostatecznie = lenient,
            odpowiedzi_trafne = isSql ? answerOk : (int?)null,
            procent = steps == 0 ? 0 : Math.Round(100.0 * strict / steps, 1),
            procent_ostatecznie = steps == 0 ? 0 : Math.Round(100.0 * lenient / steps, 1),
            sredni_czas_s = steps == 0 ? 0 : Math.Round(seconds / steps, 2),
            max_kontekst = maxContext,
            czas_calkowity_min = Math.Round(totalClock.Elapsed.TotalMinutes, 1),
        };
        Directory.CreateDirectory(options.Results);
        var file = Path.Combine(options.Results, $"{profile.Name}_{Safe(llm.ModelName)}_{DateTime.Now:yyyyMMdd_HHmmss}.json");
        File.WriteAllText(file, JsonSerializer.Serialize(new { podsumowanie = summary, wyniki = report }, Pretty));
        Console.WriteLine();
        Console.WriteLine(JsonSerializer.Serialize(summary, Pretty));
        Console.WriteLine("Szczegóły: " + Path.GetFullPath(file));
        return 0;
    }

    private static (bool strict, bool lenient, string detail) ScoreCommands(List<JsonElement> expected, bool expectQuestion, TurnResult turn, ScriptedExecutor scripted)
    {
        var domain = turn.Calls.Where(c => !c.BuiltIn).ToList();
        if (expected.Count == 0)
        {
            var ok = domain.Count == 0 && turn.Kind != TurnKind.Failed;
            return (ok, ok, (ok ? "dopytał/odmówił: " : "powinien dopytać, a wywołał: ") + (domain.Count == 0 ? Shorten(turn.Text, 90) : string.Join("; ", domain.Select(c => c.Tool + c.ArgumentsJson))));
        }

        var firstStep = domain.Count == 0 ? 0 : domain.Min(c => c.Step);
        var firstCalls = domain.Where(c => c.Step == firstStep).ToList();
        var allMatched = scripted.Matched.Count == expected.Count;
        var strictOk = allMatched && firstCalls.Count == expected.Count && firstCalls.All(c => c.Success);
        var detail = domain.Count == 0
            ? "brak wywołań: " + turn.Kind + " " + Shorten(turn.Text, 80)
            : string.Join("; ", domain.Select(c => (c.Success ? "" : "✗") + c.Tool + c.ArgumentsJson));
        if (!allMatched)
        {
            detail += " | oczekiwano: " + string.Join("; ", expected.Select(x => x.GetProperty("narzedzie").GetString() + x.GetProperty("argumenty").GetRawText()));
        }

        return (strictOk, allMatched, detail);
    }

    private static async Task<(bool sqlOk, bool answerOk, string detail)> ScoreSqlAsync(JsonElement step, TurnResult turn, PostgresReadOnlyExecutor database,
        Dictionary<string, string> variables, bool expectQuestion)
    {
        if (expectQuestion)
        {
            var ok = turn.Kind == TurnKind.Question;
            return (ok, ok, (ok ? "dopytał: " : "powinien dopytać: ") + Shorten(turn.Text, 90));
        }

        var gold = step.GetProperty("sql_wzorcowy").GetString().Replace("{{ja}}", variables["ja"]);
        var goldResult = await database.QueryAsync(gold, 500, CancellationToken.None);
        var goldNumbers = goldResult.Numbers().Select(n => Math.Round(n, 2)).Distinct().ToList();
        var sqlCalls = turn.Calls.Where(c => c.Tool == "zapytanie_sql" && c.Success).ToList();
        if (sqlCalls.Count == 0)
        {
            return (false, false, "brak udanego SQL: " + turn.Kind + " " + Shorten(string.Join(" / ", turn.Calls.Select(c => c.Result)) + " " + turn.Text, 100));
        }

        var modelNumbers = sqlCalls.SelectMany(c => NumberGuard.Extract(c.Result)).Select(n => Math.Round(n, 2)).ToHashSet();
        bool sqlOk;
        if (goldNumbers.Count == 0)
        {
            sqlOk = goldResult.Rows.Count == 0
                ? sqlCalls.Any(c => c.Result.Contains("(0 wierszy)") || NumberGuard.Extract(c.Result).All(n => n == 0))
                : goldResult.Rows.SelectMany(r => r).Select(QueryResult.Format).All(v => sqlCalls.Any(c => c.Result.Contains(v)));
        }
        else
        {
            sqlOk = goldNumbers.All(modelNumbers.Contains);
        }

        var answerNumbers = NumberGuard.Extract(turn.Text).Select(n => Math.Round(n, 2)).ToHashSet();
        var keyNumbers = goldNumbers.Where(n => n != 0).Take(5).ToList();
        var answerOk = turn.Kind == TurnKind.Answer && keyNumbers.All(n => answerNumbers.Contains(n) || answerNumbers.Contains(Math.Round(n, 1)) || answerNumbers.Contains(Math.Round(n, 0)));
        var detail = (sqlOk ? "SQL zgodny" : "SQL niezgodny") + (answerOk ? ", odpowiedź zgodna" : ", odpowiedź niezgodna") + ": " + Shorten(sqlCalls.Last().ArgumentsJson, 70);
        return (sqlOk, answerOk, detail);
    }

    private static string ConnectionFromJson(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            throw new FileNotFoundException("Profil SQL wymaga --polaczenie <plik.json> (host, port, dbname, user, password).");
        }

        var json = JsonDocument.Parse(File.ReadAllText(path)).RootElement;
        string Get(string name) => json.TryGetProperty(name, out var v) ? (v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText()) : null;
        return $"Host={Get("host")};Port={Get("port")};Database={Get("dbname")};Username={Get("user")};Password={Get("password")};Timeout=15;Command Timeout=30;Application Name=LokalneAI.Eval";
    }

    private static string Shorten(string text, int max) => string.IsNullOrEmpty(text) ? "" : (text.Length <= max ? text : text[..max] + "…").Replace("\n", " ");

    private static string Safe(string name) => string.Concat(name.Select(c => char.IsLetterOrDigit(c) || c == '.' || c == '-' ? c : '_'));
}
