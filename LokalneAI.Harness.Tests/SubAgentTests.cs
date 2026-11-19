using LokalneAI.Harness.Llm;
using LokalneAI.Harness.Profiles;
using LokalneAI.Harness.Session;
using LokalneAI.Harness.Tools;

namespace LokalneAI.Harness.Tests;

/// <summary>Podagenci: narzędzie zlec_podagentowi, równoległe zlecenia w kroku, przebieg sesji podagenta (SubAgentRunner), limity kroków.</summary>
public sealed class SubAgentTests
{
    private const string Delegate = "{\"krok\":\"wywolania\",\"wywolania\":["
        + "{\"narzedzie\":\"zlec_podagentowi\",\"argumenty\":{\"zadanie\":\"policz pliki w A\",\"umiejetnosci\":\"pliki\"}},"
        + "{\"narzedzie\":\"zlec_podagentowi\",\"argumenty\":{\"zadanie\":\"policz pliki w B\",\"kontekst\":\"folder B\"}}]}";

    private const string Answer = "{\"krok\":\"odpowiedz\",\"tekst\":\"A ma 3 pliki, B ma 5.\"}";

    private static HarnessProfile TeklaLike(FinishMode finish = FinishMode.Auto) => new HarnessProfile(
        "test",
        "Zamieniasz polecenia na wywołania.",
        new[]
        {
            new ToolDefinition("wstaw_slup", "Wstawia słup.",
                "{\"type\":\"object\",\"properties\":{\"x_mm\":{\"type\":\"number\"},\"profil\":{\"type\":\"string\"}},\"required\":[\"x_mm\",\"profil\"],\"additionalProperties\":false}",
                mutates: true),
        },
        new ProfileSettings { Finish = finish });

    /// <summary>Udawany gospodarz: liczy równoległość, odpowiada po chwili tekstem z zadania.</summary>
    private sealed class FakeHost : ISubAgentHost
    {
        private int _active;

        public List<SubAgentRequest> Requests { get; } = new List<SubAgentRequest>();

        public int MaxConcurrent { get; private set; }

        public TimeSpan Delay { get; set; } = TimeSpan.FromMilliseconds(150);

        public IReadOnlyList<SubAgentSkill> Skills { get; } = new[] { new SubAgentSkill("pliki", "foldery i pliki"), new SubAgentSkill("rfem", "modele RFEM 5") };

        public string Describe() => "ten komputer (RTX 4060 (8 GB))";

        public async Task<SubAgentResult> RunAsync(SubAgentRequest request, Action<HarnessEvent> report, CancellationToken cancellationToken)
        {
            lock (Requests)
            {
                Requests.Add(request);
            }

            var now = Interlocked.Increment(ref _active);
            MaxConcurrent = Math.Max(MaxConcurrent, now);
            try
            {
                await Task.Delay(Delay, cancellationToken);
                return new SubAgentResult { Success = true, Text = "wynik: " + request.Task, Machine = "ten komputer", Model = "udawany", Duration = Delay };
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }
    }

    /// <summary>Model, który odpowiada dopiero po chwili (do prób przerywania).</summary>
    private sealed class SlowLlm : ILlmClient
    {
        public TimeSpan Delay { get; set; } = TimeSpan.FromSeconds(5);

        public string ModelName => "powolny";

        public int ContextLength => 8192;

        public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
        {
            await Task.Delay(Delay, cancellationToken);
            return new LlmResponse { Content = "{\"krok\":\"odpowiedz\",\"tekst\":\"po czasie\"}", PromptTokens = 100, OutputTokens = 10 };
        }
    }

    [Fact]
    public void Gospodarz_DodajeNarzedzieISekcjePodagenci()
    {
        var host = new FakeHost();
        using var with = new HarnessSession(new ScriptedLlm(), TeklaLike(), new SimulatedModelExecutor(), new SessionOptions { SubAgents = host });
        using var without = new HarnessSession(new ScriptedLlm(), TeklaLike(), new SimulatedModelExecutor());

        Assert.Contains("## Podagenci", with.SystemPrompt);
        Assert.Contains("zlec_podagentowi", with.SystemPrompt);
        Assert.Contains("- pliki — foldery i pliki", with.SystemPrompt);
        Assert.Contains("Komputery podagentów: ten komputer (RTX 4060 (8 GB))", with.SystemPrompt);
        Assert.Contains("zlec_podagentowi", with.StepSchemaJson);
        Assert.Contains(with.Profile.Tools, t => t.Name == BuiltInTools.DelegateName && t.IsBuiltIn);
        Assert.DoesNotContain("Podagenci", without.SystemPrompt);
        Assert.DoesNotContain("zlec_podagentowi", without.StepSchemaJson);
        Assert.Same(with.Profile, with.Profile.WithSubAgents());
    }

    [Fact]
    public async Task DwaZleceniaWKroku_LiczaSieRownolegle_AWynikiWracajaWKolejnosci()
    {
        var host = new FakeHost();
        var llm = new ScriptedLlm(Delegate, Answer);
        using var session = new HarnessSession(llm, TeklaLike(FinishMode.Model), new SimulatedModelExecutor(), new SessionOptions { SubAgents = host });
        var events = new List<HarnessEvent>();
        session.Progress += (_, e) => { lock (events) { events.Add(e); } };

        var turn = await session.SendAsync("policz pliki w A i B");

        Assert.Equal(TurnKind.Answer, turn.Kind);
        Assert.Equal(2, host.Requests.Count);
        Assert.Equal(2, host.MaxConcurrent);
        Assert.Equal(2, turn.Calls.Count);
        Assert.All(turn.Calls, c => Assert.True(c.Success));
        Assert.All(turn.Calls, c => Assert.True(c.BuiltIn));
        Assert.StartsWith("Podagent 1 (ten komputer, udawany", turn.Calls[0].Result);
        Assert.Contains("wynik: policz pliki w A", turn.Calls[0].Result);
        Assert.StartsWith("Podagent 2 (", turn.Calls[1].Result);
        Assert.Contains("wynik: policz pliki w B", turn.Calls[1].Result);
        Assert.Equal(new[] { "pliki" }, host.Requests.Single(r => r.Number == 1).Skills);
        Assert.Null(host.Requests.Single(r => r.Number == 2).Skills);
        Assert.Equal("folder B", host.Requests.Single(r => r.Number == 2).Context);
        // Wyniki w następnym zapytaniu do modelu, po kolei.
        var results = llm.Requests[1].Messages.Last().Content;
        Assert.True(results.IndexOf("1. zlec_podagentowi → Podagent 1", StringComparison.Ordinal) < results.IndexOf("2. zlec_podagentowi → Podagent 2", StringComparison.Ordinal));
        // Oba wywołania wystartowały przed pierwszym zakończeniem.
        var started = events.Select((e, i) => (e, i)).Where(p => p.e.Kind == HarnessEventKind.ToolStarted).Select(p => p.i).ToList();
        var finished = events.Select((e, i) => (e, i)).Where(p => p.e.Kind == HarnessEventKind.ToolFinished).Select(p => p.i).ToList();
        Assert.Equal(2, started.Count);
        Assert.True(started[1] < finished[0]);
    }

    [Fact]
    public async Task NieznanaUmiejetnosc_WracaDoModeluJakoBlad()
    {
        var host = new FakeHost();
        var llm = new ScriptedLlm(
            "{\"krok\":\"wywolania\",\"wywolania\":[{\"narzedzie\":\"zlec_podagentowi\",\"argumenty\":{\"zadanie\":\"x\",\"umiejetnosci\":\"excel, pliki\"}}]}",
            "{\"krok\":\"pytanie\",\"tekst\":\"Czym?\"}");
        using var session = new HarnessSession(llm, TeklaLike(FinishMode.Model), new SimulatedModelExecutor(), new SessionOptions { SubAgents = host });

        var turn = await session.SendAsync("zrób");

        Assert.Empty(host.Requests);
        Assert.False(turn.Calls.Single().Success);
        Assert.Contains("nie ma umiejętności excel", turn.Calls.Single().Result);
        Assert.Contains("Dostępne: pliki, rfem", turn.Calls.Single().Result);
    }

    [Fact]
    public async Task PusteZadanie_WracaDoModeluJakoBlad()
    {
        var host = new FakeHost();
        var llm = new ScriptedLlm(
            "{\"krok\":\"wywolania\",\"wywolania\":[{\"narzedzie\":\"zlec_podagentowi\",\"argumenty\":{\"zadanie\":\"  \"}}]}",
            Answer);
        using var session = new HarnessSession(llm, TeklaLike(FinishMode.Model), new SimulatedModelExecutor(), new SessionOptions { SubAgents = host });

        var turn = await session.SendAsync("zrób");

        Assert.Empty(host.Requests);
        Assert.Contains("puste zadanie", turn.Calls.Single().Result);
    }

    [Fact]
    public async Task BezGospodarza_ModelNieMaNarzedzia()
    {
        var llm = new ScriptedLlm(Delegate, Answer);
        using var session = new HarnessSession(llm, TeklaLike(FinishMode.Model), new SimulatedModelExecutor());

        var turn = await session.SendAsync("policz");

        Assert.All(turn.Calls, c => Assert.False(c.Success));
        Assert.Contains("Nie ma narzędzia „zlec_podagentowi”", turn.Calls[0].Result);
    }

    [Fact]
    public async Task Runner_ProwadziSesjePodagenta_IPrzekazujeZdarzenia()
    {
        var child = new HarnessSession(
            new ScriptedLlm("{\"krok\":\"wywolania\",\"wywolania\":[{\"narzedzie\":\"wstaw_slup\",\"argumenty\":{\"x_mm\":6000,\"profil\":\"HEB300\"}}]}"),
            TeklaLike(), new SimulatedModelExecutor());
        var request = new SubAgentRequest { Number = 3, Task = "wstaw słup HEB300 w x=6000", Context = "hala A" };
        var info = new SubAgentInfo { Number = 3, Machine = "PC-ODS12", Model = "gemma4:12b", Skills = "tekla" };
        var reported = new List<HarnessEvent>();

        var result = await SubAgentRunner.RunAsync(child, request, info, reported.Add, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("id=1001", result.Text);
        Assert.Equal("PC-ODS12", result.Machine);
        Assert.Equal(TurnKind.Answer, result.Turn!.Kind);
        Assert.Equal(HarnessEventKind.AgentStarted, reported.First().Kind);
        Assert.Same(info, reported.First().Agent);
        Assert.Equal(HarnessEventKind.AgentFinished, reported.Last().Kind);
        Assert.Contains(reported, e => e.Kind == HarnessEventKind.AgentProgress && e.Inner?.Kind == HarnessEventKind.ToolStarted && e.Inner.Call.Tool == "wstaw_slup");
        Assert.Contains(reported, e => e.Kind == HarnessEventKind.AgentProgress && e.Inner?.Kind == HarnessEventKind.ModelThinking && e.Step == 1);
        Assert.False(info.IsRunning);
        Assert.True(info.Success);
        Assert.Equal("gotowe", info.Status);
        Assert.Equal(1, info.Step);
        Assert.Null(info.Cancel);
        // Polecenie dla podagenta: zadanie, zasady i dane.
        var prompt = SubAgentRunner.BuildTask(request).Replace("\r\n", "\n");
        Assert.Contains("wstaw słup HEB300 w x=6000", prompt);
        Assert.Contains("Dane do zadania:\nhala A", prompt);
        Assert.Contains("Nie zadawaj pytań", prompt);
        Assert.Contains(child.History, m => m.Role == "user" && m.Content.Contains("Dane do zadania"));
    }

    [Fact]
    public async Task Runner_PytaniePodagenta_ToNieudanyWynik()
    {
        var child = new HarnessSession(new ScriptedLlm("{\"krok\":\"pytanie\",\"tekst\":\"Jaki profil?\"}"), TeklaLike(), new SimulatedModelExecutor());

        var result = await SubAgentRunner.RunAsync(child, new SubAgentRequest { Number = 1, Task = "wstaw słup" }, null, null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("Podagent pyta zamiast odpowiedzieć: Jaki profil?", result.Text);
        Assert.Equal(TurnKind.Question, result.Turn!.Kind);
    }

    [Fact]
    public async Task Runner_PrzerwanieKafelkiem_DajeNieudanyWynik()
    {
        var child = new HarnessSession(new SlowLlm(), TeklaLike(), new SimulatedModelExecutor());
        var info = new SubAgentInfo { Number = 1 };
        var started = new TaskCompletionSource<bool>();
        Action<HarnessEvent> report = e =>
        {
            if (e.Kind == HarnessEventKind.AgentStarted)
            {
                started.TrySetResult(true);
            }
        };

        var run = SubAgentRunner.RunAsync(child, new SubAgentRequest { Number = 1, Task = "czekaj" }, info, report, CancellationToken.None);
        await started.Task;
        Assert.NotNull(info.Cancel);
        info.Cancel!();
        var result = await run;

        Assert.False(result.Success);
        Assert.Contains("przerwany przez użytkownika", result.Text);
        Assert.False(info.IsRunning);
        Assert.False(info.Success);
    }

    [Fact]
    public async Task Runner_StopCalejTury_RzucaWyjatek()
    {
        var child = new HarnessSession(new SlowLlm(), TeklaLike(), new SimulatedModelExecutor());
        using var turn = new CancellationTokenSource();
        var info = new SubAgentInfo { Number = 1 };
        var finished = new List<HarnessEvent>();

        var run = SubAgentRunner.RunAsync(child, new SubAgentRequest { Number = 1, Task = "czekaj" }, info, e => { if (e.Kind == HarnessEventKind.AgentFinished) { finished.Add(e); } }, turn.Token);
        turn.CancelAfter(50);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Single(finished);
        Assert.Equal("przerwany (Stop)", info.Status);
    }

    [Fact]
    public async Task StopTury_PrzerywaPodagentow()
    {
        var host = new FakeHost { Delay = TimeSpan.FromSeconds(10) };
        var llm = new ScriptedLlm(Delegate, Answer);
        using var session = new HarnessSession(llm, TeklaLike(FinishMode.Model), new SimulatedModelExecutor(), new SessionOptions { SubAgents = host });
        using var cts = new CancellationTokenSource(200);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.SendAsync("policz", cts.Token));
        Assert.Equal(2, host.Requests.Count);
    }

    [Fact]
    public void FormatResult_NaglowekZKomputeremModelemICzasem_IUciecie()
    {
        var ok = new SubAgentResult { Success = true, Text = "3 pliki", Machine = "PC-ODS12", Model = "gemma4:12b", Duration = TimeSpan.FromSeconds(12.4) };
        var bad = new SubAgentResult { Success = false, Text = "Podagent pyta zamiast odpowiedzieć: który folder?" };

        Assert.Equal("Podagent 2 (PC-ODS12, gemma4:12b, 12 s): 3 pliki", SubAgentRunner.FormatResult(ok, 2, 0));
        Assert.Equal("BŁĄD podagent 2: Podagent pyta zamiast odpowiedzieć: który folder?", SubAgentRunner.FormatResult(bad, 2, 6000));
        var cut = SubAgentRunner.FormatResult(new SubAgentResult { Success = true, Text = new string('x', 100) }, 1, 20);
        Assert.Contains(new string('x', 20) + "… (wynik ucięty do 20 znaków)", cut);
        Assert.StartsWith("BŁĄD podagent 4: brak wyniku.", SubAgentRunner.FormatResult(null, 4, 0));
    }

    [Theory]
    [InlineData("pliki, rfem", new[] { "pliki", "rfem" })]
    [InlineData("Pliki;OFFICE pliki", new[] { "pliki", "office" })]
    [InlineData("", new string[0])]
    [InlineData(null, new string[0])]
    public void ParseSkills_PoPrzecinkachSrednikachISpacjach_BezPowtorzen(string text, string[] expected)
        => Assert.Equal(expected, SubAgentRunner.ParseSkills(text));

    [Fact]
    public void Observe_StatusIKrokZeZdarzen()
    {
        var info = new SubAgentInfo();
        info.Observe(new HarnessEvent(HarnessEventKind.ModelThinking, "Model pracuje (krok 2 z 12)…") { Step = 2 });
        Assert.Equal(2, info.Step);
        Assert.Equal("Model pracuje (krok 2 z 12)…", info.Status);
        info.Observe(new HarnessEvent(HarnessEventKind.ToolStarted, "x", new ExecutedCall { Tool = "czytaj_plik" }));
        Assert.Equal("→ czytaj_plik", info.Status);
        info.Observe(new HarnessEvent(HarnessEventKind.ToolFinished, "x", new ExecutedCall { Tool = "czytaj_plik", Success = false }));
        Assert.Equal("czytaj_plik ✗", info.Status);
        Assert.Equal("Podagent 0 · ? · ?", info.Describe());
    }

    [Fact]
    public async Task ZdarzeniaKroku_NiosaNumerKroku_ILimitWTekscie()
    {
        var llm = new ScriptedLlm(
            "{\"krok\":\"wywolania\",\"wywolania\":[{\"narzedzie\":\"wstaw_slup\",\"argumenty\":{\"x_mm\":1,\"profil\":\"HEB300\"}}]}",
            Answer);
        var profile = TeklaLike(FinishMode.Model);
        using var session = new HarnessSession(llm, profile, new SimulatedModelExecutor());
        var events = new List<HarnessEvent>();
        session.Progress += (_, e) => events.Add(e);

        await session.SendAsync("wstaw");

        var steps = events.Where(e => e.Kind == HarnessEventKind.ModelStep).Select(e => e.Step).ToList();
        Assert.Equal(new[] { 1, 2 }, steps);
        Assert.Contains(events, e => e.Kind == HarnessEventKind.ModelThinking && e.Step == 2 && e.Text.Contains("(krok 2 z " + profile.Settings.MaxSteps + ")"));
    }

    [Fact]
    public void LimitKrokow_Domyslnie12_AProfilePodwojone()
    {
        Assert.Equal(12, new ProfileSettings().MaxSteps);
        Assert.Equal(10, HarnessProfile.LoadSettings(Paths.Profile("tekla")).MaxSteps);
        Assert.Equal(12, HarnessProfile.LoadSettings(Paths.Profile("komputer")).MaxSteps);
        Assert.Equal(6, HarnessProfile.LoadSettings(Paths.Profile("tlumaczenie")).MaxSteps);
        foreach (var folder in Directory.GetDirectories(Path.Combine(Paths.Root, "profile")).Where(d => File.Exists(Path.Combine(d, "profil.json"))))
        {
            Assert.True(HarnessProfile.LoadSettings(folder).MaxSteps >= 6, Path.GetFileName(folder) + ": max_krokow < 6");
        }
    }
}
