using System.Diagnostics;
using System.Text.Json;
using LokalneAI.Harness.Profiles;
using LokalneAI.Harness.Session;
using LokalneAI.Harness.Tools;

namespace LokalneAI.Harness.Tests;

/// <summary>Ocena poleceń PowerShella: bez pytania / z pytaniem / odrzucone.</summary>
public sealed class PowerShellPolicyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lokalneai_ps_" + Guid.NewGuid().ToString("N"));

    public PowerShellPolicyTests() => Directory.CreateDirectory(Path.Combine(_root, "Projekty"));

    public void Dispose() => Directory.Delete(_root, true);

    private CommandAnalysis Analyze(string script, string folder = null)
        => PowerShellPolicy.Analyze(script, folder ?? _root, new[] { _root }, new[] { Environment.GetFolderPath(Environment.SpecialFolder.Windows) });

    [Theory]
    [InlineData("Get-ChildItem")]
    [InlineData("dir")]
    [InlineData("ls -Recurse *.pdf")]
    [InlineData("(Get-ChildItem -Recurse -File -Filter *.pdf).Count")]
    [InlineData("Get-ChildItem | Sort-Object LastWriteTime -Descending | Select-Object -First 5 Name, LastWriteTime")]
    [InlineData("[math]::Round((Get-ChildItem -Recurse -File | Measure-Object Length -Sum).Sum / 1MB, 1)")]
    [InlineData("Select-String -Path *.txt -Pattern 'WSP'")]
    [InlineData("cd Projekty; Get-ChildItem")]
    [InlineData("Get-Content notatki.txt 2>$null")]
    [InlineData("Get-ChildItem | Where-Object { $_.Length -gt 1000 } | ForEach-Object { $_.Name.ToUpper() }")]
    [InlineData("Get-ChildItem | Select-Object Name, @{n='kB';e={[math]::Round($_.Length/1KB)}}")]
    [InlineData("foreach ($f in Get-ChildItem) { $f.Name }")]
    [InlineData("Get-ChildItem -Recurse | Group-Object Extension | Sort-Object Count -Descending")]
    public void TylkoCzyta_BezPytania(string script)
    {
        var analysis = Analyze(script);

        Assert.True(analysis.Verdict == ConfirmationKind.None, string.Join("; ", analysis.AskReasons) + analysis.Reason);
    }

    [Fact]
    public void SciezkaWDozwolonymFolderze_BezPytania()
    {
        Assert.Equal(ConfirmationKind.None, Analyze("Get-ChildItem '" + Path.Combine(_root, "Projekty") + "'").Verdict);
        Assert.Equal(ConfirmationKind.None, Analyze("Get-ChildItem ..", Path.Combine(_root, "Projekty")).Verdict);
    }

    [Theory]
    [InlineData("New-Item -ItemType Directory Raporty", false)]
    [InlineData("mkdir Raporty", false)]
    [InlineData("md Raporty", false)]
    [InlineData("Copy-Item a.txt b.txt", false)]
    [InlineData("Get-ChildItem *.tmp | Remove-Item", true)]
    [InlineData("del stary.txt", true)]
    [InlineData("gci | % Delete", false)]
    [InlineData("$f = Get-Item a.txt; $f.Delete()", false)]
    [InlineData("notepad", false)]
    [InlineData("git status", false)]
    [InlineData("Get-ChildItem > lista.txt", false)]
    [InlineData("\"$(Remove-Item a.txt)\"", false)]
    [InlineData("[IO.File]::WriteAllText('a.txt','b')", false)]
    [InlineData("Invoke-WebRequest https://example.com -OutFile a.html", false)]
    [InlineData("Start-Process notepad", false)]
    [InlineData("Get-ChildItem | Set-Content lista.txt", false)]
    public void Zmienia_ZPytaniem(string script, bool deletes)
    {
        var analysis = Analyze(script);

        Assert.Equal(ConfirmationKind.Ask, analysis.Verdict);
        Assert.Equal(deletes, analysis.Deletes);
    }

    [Fact]
    public void ProgramZFolderuProgramow_ZPytaniem()
    {
        var notepad = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "notepad.exe");

        Assert.Equal(ConfirmationKind.Ask, Analyze("& '" + notepad + "'").Verdict);
        // Ten sam folder jako zwykła ścieżka (czytanie) — poza dozwolonymi.
        Assert.Equal(ConfirmationKind.Reject, Analyze("Get-ChildItem '" + Path.GetDirectoryName(notepad) + "'").Verdict);
    }

    [Theory]
    [InlineData("Get-ChildItem C:\\Windows")]
    [InlineData("Get-ChildItem $env:WINDIR")]
    [InlineData("Get-ChildItem ~")]
    [InlineData("cd ..\\..")]
    [InlineData("Get-Content \\\\192.168.50.10\\dane\\a.txt")]
    [InlineData("Copy-Item a.txt D:\\")]
    [InlineData("Format-Volume -DriveLetter D")]
    [InlineData("shutdown /s /t 0")]
    [InlineData("Stop-Computer")]
    [InlineData("Remove-Item * -Recurse -Force")]
    [InlineData("powershell -EncodedCommand ZQBjAGgAbwA=")]
    public void Odrzucone(string script)
    {
        var analysis = Analyze(script);

        Assert.Equal(ConfirmationKind.Reject, analysis.Verdict);
        Assert.StartsWith("BŁĄD", analysis.Reason);
    }

    [Fact]
    public void KasowanieCalegoDozwolonegoFolderu_Odrzucone()
    {
        Assert.Equal(ConfirmationKind.Reject, Analyze("Remove-Item '" + _root + "' -Recurse").Verdict);
        Assert.Equal(ConfirmationKind.Reject, Analyze("Remove-Item . -Recurse").Verdict);
        // Konkretny podfolder — z pytaniem.
        Assert.Equal(ConfirmationKind.Ask, Analyze("Remove-Item Projekty -Recurse").Verdict);
    }

    [Theory]
    [InlineData("Set-Content skrypt.py \"import os\\nprint(os.getcwd())\"")]
    [InlineData("'linia 1\\nlinia 2' | Out-File notatka.txt")]
    [InlineData("Write-Output \"a = 1\\nb = 2\" > wynik.txt")]
    [InlineData("Set-Content -Path 'Projekty\\skrypt.bat' -Value \"@echo off\\r\\necho test\"")]
    public void ZapisZUkosnikiemN_OdrzuconyZPodpowiedzia(string script)
    {
        var analysis = Analyze(script);

        Assert.Equal(ConfirmationKind.Reject, analysis.Verdict);
        Assert.Contains("zapisz_plik", analysis.Reason);
    }

    [Theory]
    [InlineData("Set-Content 'Projekty\\notatki.txt' 'abc'")]
    [InlineData("New-Item -ItemType Directory 'Projekty\\nowy'")]
    [InlineData("(Get-Content a.txt -Raw) -replace '[\\r\\n]+', ' ' | Set-Content b.txt")]
    [InlineData("Set-Content a.py @'\nprint(\"a\\nb\")\n'@")]
    public void UkosnikNWSciezceRegexieIHereStringu_ZwyklePytanie(string script)
    {
        var analysis = Analyze(script);

        Assert.True(analysis.Verdict == ConfirmationKind.Ask, analysis.Verdict + ": " + analysis.Reason);
    }

    [Fact]
    public void UkosnikNWPelnejSciezce_ZwyklePytanie()
        => Assert.Equal(ConfirmationKind.Ask, Analyze("Set-Content '" + Path.Combine(_root, "nowy.txt") + "' 'abc'").Verdict);
}

/// <summary>Wykonawca na prawdziwym Windows PowerShellu i w sesji z profilem „komputer”.</summary>
public sealed class ComputerExecutorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lokalneai_komp_" + Guid.NewGuid().ToString("N"));

    public ComputerExecutorTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        for (var i = 0; i < 5; i++)
        {
            try
            {
                Directory.Delete(_root, true);
                return;
            }
            catch (IOException)
            {
                Thread.Sleep(300); // PowerShell bywa jeszcze w folderze
            }
        }
    }

    private FileSystemExecutor Executor(int timeoutSeconds = 60)
        => new FileSystemExecutor(new FileSystemOptions
        {
            AllowedRoots = new List<string> { _root },
            WorkingFolder = _root,
            CommandTimeout = TimeSpan.FromSeconds(timeoutSeconds),
        });

    private static readonly HarnessProfile Profile = HarnessProfile.Load(Paths.Profile("komputer"));

    private static ToolCall Call(string tool, object args)
        => new ToolCall(Profile.FindTool(tool), Paths.Json(JsonSerializer.Serialize(args)), 1, 1);

    private static ToolCall Ps(string script) => Call("powershell", new { polecenie = script, opis = "test" });

    [Fact]
    public async Task PowerShell_PolskieZnakiICdZostaje()
    {
        var executor = Executor();
        var call = Ps("New-Item -ItemType Directory 'Żółw ąę' | Out-Null; cd 'Żółw ąę'; 'zażółć gęślą jaźń'");

        Assert.Equal(ConfirmationKind.Ask, executor.Decide(call).Kind);
        var result = await executor.ExecuteAsync(call, CancellationToken.None);

        Assert.True(result.Success, result.Text);
        Assert.Contains("zażółć gęślą jaźń", result.Text);
        Assert.Equal(Path.Combine(_root, "Żółw ąę"), executor.WorkingFolder, StringComparer.OrdinalIgnoreCase);

        // Kolejne polecenie startuje w nowym folderze.
        var where = await executor.ExecuteAsync(Ps("(Get-Location).Path"), CancellationToken.None);
        Assert.Contains("Żółw ąę", where.Text);
    }

    [Fact]
    public async Task PowerShell_BladWracaJakoBlad()
    {
        var result = await Executor().ExecuteAsync(Ps("Get-Item nie_ma_takiego.txt"), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("[błąd]", result.Text);
        Assert.DoesNotContain("CLIXML", result.Text);
    }

    [Fact]
    public async Task PowerShell_KodWyjsciaProgramu()
    {
        var result = await Executor().ExecuteAsync(Ps("cmd /c exit 3"), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("kod wyjścia programu: 3", result.Text);
    }

    [Fact]
    public async Task PowerShell_LimitCzasuPrzerywa()
    {
        var clock = Stopwatch.StartNew();
        var result = await Executor(timeoutSeconds: 3).ExecuteAsync(Ps("Start-Sleep -Seconds 60; 'nie powinno'"), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("przekroczyło limit", result.Text);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20), clock.Elapsed.ToString());
    }

    [Fact]
    public async Task PowerShell_PozaFolderem_NieWykonuje()
    {
        var executor = Executor();
        var call = Ps("New-Item -ItemType Directory C:\\Windows\\lokalneai_test");

        Assert.Equal(ConfirmationKind.Reject, executor.Decide(call).Kind);
        var result = await executor.ExecuteAsync(call, CancellationToken.None);
        Assert.False(result.Success);
        Assert.Contains("poza dozwolone", result.Text);
    }

    [Fact]
    public async Task CzytajISzukaj()
    {
        Directory.CreateDirectory(Path.Combine(_root, "R02", "Rysunki"));
        File.WriteAllLines(Path.Combine(_root, "R02", "Rysunki", "B-101_R02.txt"), Enumerable.Range(1, 500).Select(i => "linia " + i + " zażółć"));
        File.WriteAllBytes(Path.Combine(_root, "R02", "plan.pdf"), new byte[] { 0x25, 0x50, 0x44, 0x46, 0, 1, 2 });
        var executor = Executor();

        var found = await executor.ExecuteAsync(Call("szukaj_plikow", new { wzorzec = "R02" }), CancellationToken.None);
        var text = await executor.ExecuteAsync(Call("czytaj_plik", new { sciezka = "R02\\Rysunki\\B-101_R02.txt", od_linii = 10, ile_linii = 5 }), CancellationToken.None);
        var binary = await executor.ExecuteAsync(Call("czytaj_plik", new { sciezka = "R02\\plan.pdf" }), CancellationToken.None);

        Assert.Contains("B-101_R02.txt", found.Text);
        Assert.Contains("[folder] R02", found.Text);
        Assert.Contains("10: linia 10 zażółć", text.Text);
        Assert.Contains("14: linia 14", text.Text);
        Assert.DoesNotContain("15: linia 15", text.Text);
        Assert.Contains("linii: 500", text.Text);
        Assert.Contains("binarny", binary.Text);
        Assert.Equal(ConfirmationKind.None, executor.Decide(Call("czytaj_plik", new { sciezka = "x.txt" })).Kind);
    }

    [Fact]
    public async Task ZapiszPlik_NoweLiniePolskieLiteryISkryptDziala()
    {
        var executor = Executor();
        var call = Call("zapisz_plik", new { sciezka = "Skrypty\\info.ps1", tresc = "$a = 'zażółć'\nWrite-Output \"Napis: $a\"\n" });

        var decision = executor.Decide(call);
        var result = await executor.ExecuteAsync(call, CancellationToken.None);
        var run = await executor.ExecuteAsync(Ps("& '.\\Skrypty\\info.ps1'"), CancellationToken.None);

        Assert.Equal(ConfirmationKind.Ask, decision.Kind);
        Assert.False(decision.Dangerous);
        Assert.Contains("Zapisać nowy plik", decision.Message);
        Assert.Contains("Write-Output", decision.Message);
        Assert.True(result.Success, result.Text);
        Assert.Contains("utworzono folder", result.Text);
        var bytes = File.ReadAllBytes(Path.Combine(_root, "Skrypty", "info.ps1"));
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
        Assert.Equal("$a = 'zażółć'\r\nWrite-Output \"Napis: $a\"\r\n", File.ReadAllText(Path.Combine(_root, "Skrypty", "info.ps1")));
        Assert.True(run.Success, run.Text);
        Assert.Contains("Napis: zażółć", run.Text);
    }

    [Fact]
    public async Task ZapiszPlik_UkosnikNOdModelu_ZamienionyASciezkaZostaje()
    {
        var executor = Executor();
        var call = Call("zapisz_plik", new { sciezka = "a.py", tresc = "import os\\nprint(1)\\nsciezka = 'C:\\ODS\\nowy'" });

        var decision = executor.Decide(call);
        var result = await executor.ExecuteAsync(call, CancellationToken.None);

        Assert.Contains("zamienione na nowe linie: 2", decision.Message);
        Assert.True(result.Success, result.Text);
        Assert.Contains("zamienione na nowe linie (2×)", result.Text);
        Assert.Equal("import os\nprint(1)\nsciezka = 'C:\\ODS\\nowy'", File.ReadAllText(Path.Combine(_root, "a.py")));
    }

    [Fact]
    public async Task ZapiszPlik_IstniejacyTylkoZNadpiszIPytaniemOstrzegawczym()
    {
        var file = Path.Combine(_root, "a.txt");
        File.WriteAllText(file, "stare");
        var executor = Executor();
        var withoutFlag = Call("zapisz_plik", new { sciezka = "a.txt", tresc = "nowe" });
        var withFlag = Call("zapisz_plik", new { sciezka = "a.txt", tresc = "nowe", nadpisz = true });

        Assert.Equal(ConfirmationKind.Reject, executor.Decide(withoutFlag).Kind);
        Assert.False((await executor.ExecuteAsync(withoutFlag, CancellationToken.None)).Success);
        Assert.Equal("stare", File.ReadAllText(file));
        var decision = executor.Decide(withFlag);
        Assert.Equal(ConfirmationKind.Ask, decision.Kind);
        Assert.True(decision.Dangerous);
        Assert.Contains("Nadpisać", decision.Message);
        Assert.True((await executor.ExecuteAsync(withFlag, CancellationToken.None)).Success);
        Assert.Equal("nowe", File.ReadAllText(file));
    }

    [Fact]
    public void ZapiszPlik_PozaFolderemIFolderZamiastPliku_Odrzucone()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Raporty"));
        var executor = Executor();

        Assert.Equal(ConfirmationKind.Reject, executor.Decide(Call("zapisz_plik", new { sciezka = "C:\\Windows\\x.txt", tresc = "x" })).Kind);
        Assert.Equal(ConfirmationKind.Reject, executor.Decide(Call("zapisz_plik", new { sciezka = "Raporty", tresc = "x" })).Kind);
    }

    [Fact]
    public void Uruchom_DokumentBezPytaniaProgramZPytaniemSpozaOdrzucone()
    {
        File.WriteAllText(Path.Combine(_root, "raport.pdf"), "x");
        var executor = Executor();

        Assert.Equal(ConfirmationKind.None, executor.Decide(Call("uruchom", new { program = "raport.pdf" })).Kind);
        Assert.Equal(ConfirmationKind.Ask, executor.Decide(Call("uruchom", new { program = "notepad" })).Kind);
        Assert.Equal(ConfirmationKind.Ask, executor.Decide(Call("uruchom", new
        {
            program = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "notepad.exe"),
        })).Kind);
        Assert.Equal(ConfirmationKind.Reject, executor.Decide(Call("uruchom", new
        {
            program = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "win.ini"),
        })).Kind);
    }

    [Fact]
    public void Truncate_PoczatekIKoniec()
    {
        var text = string.Join("\n", Enumerable.Range(1, 2000).Select(i => "wiersz " + i));

        var cut = FileSystemExecutor.Truncate(text, 1000);

        Assert.True(cut.Length < 1100);
        Assert.StartsWith("wiersz 1\n", cut);
        Assert.EndsWith("wiersz 2000", cut);
        Assert.Contains("pominięto", cut);
    }

    [Fact]
    public async Task Sesja_CzytanieBezPytaniaModelOdpowiada_ZmianaZPytaniemKoniecAuto()
    {
        File.WriteAllText(Path.Combine(_root, "a.pdf"), "x");
        var asked = new List<string>();
        var llm = new ScriptedLlm(
            "{\"krok\":\"wywolania\",\"wywolania\":[{\"narzedzie\":\"powershell\",\"argumenty\":{\"polecenie\":\"(Get-ChildItem -Filter *.pdf).Count\",\"opis\":\"liczy PDF\"}}]}",
            "{\"krok\":\"odpowiedz\",\"tekst\":\"W folderze jest 1 plik PDF.\"}",
            "{\"krok\":\"wywolania\",\"wywolania\":[{\"narzedzie\":\"powershell\",\"argumenty\":{\"polecenie\":\"New-Item -ItemType Directory Raporty | Out-Null\",\"opis\":\"tworzy folder Raporty\"}}]}");
        using var session = new HarnessSession(llm, Profile, Executor(), new SessionOptions
        {
            ConfirmMutation = (call, token) =>
            {
                asked.Add(call.ConfirmationMessage);
                return Task.FromResult(true);
            },
        });

        var count = await session.SendAsync("ile jest PDF-ów?");
        var create = await session.SendAsync("utwórz folder Raporty");

        // Czytanie: bez pytania, a po wyniku model pisze odpowiedź (tryb „mieszane”).
        Assert.Equal("W folderze jest 1 plik PDF.", count.Text);
        Assert.Equal(2, count.ModelCalls);
        // Zmiana: z pytaniem, a po zgodzie koniec bez kolejnego wywołania modelu.
        Assert.Single(asked);
        Assert.Contains("New-Item -ItemType Directory Raporty", asked[0]);
        Assert.Contains("tworzy folder Raporty", asked[0]);
        Assert.Equal(1, create.ModelCalls);
        Assert.True(Directory.Exists(Path.Combine(_root, "Raporty")));
    }

    [Fact]
    public async Task Sesja_OdrzuconePrzezProgram_BezPytaniaUzytkownika()
    {
        var asked = 0;
        var llm = new ScriptedLlm(
            "{\"krok\":\"wywolania\",\"wywolania\":[{\"narzedzie\":\"powershell\",\"argumenty\":{\"polecenie\":\"Remove-Item C:\\\\Windows\\\\Temp\\\\x.txt\",\"opis\":\"kasuje\"}}]}",
            "{\"krok\":\"odpowiedz\",\"tekst\":\"Nie mogę — to poza dozwolonymi folderami.\"}");
        using var session = new HarnessSession(llm, Profile, Executor(), new SessionOptions
        {
            ConfirmMutation = (call, token) =>
            {
                asked++;
                return Task.FromResult(true);
            },
        });

        var turn = await session.SendAsync("usuń C:\\Windows\\Temp\\x.txt");

        Assert.Equal(0, asked);
        Assert.False(turn.Calls.Single().Success);
        Assert.Contains("poza dozwolone", turn.Calls.Single().Result);
        Assert.Equal(TurnKind.Answer, turn.Kind);
    }

    [Fact]
    public async Task Sesja_WykonawcaBezDecyzji_DzialaPoStaremu()
    {
        // Wykonawca bez IConfirmingExecutor: pytanie według „zmienia_model” z profilu.
        var asked = 0;
        var llm = new ScriptedLlm("{\"krok\":\"wywolania\",\"wywolania\":[{\"narzedzie\":\"powershell\",\"argumenty\":{\"polecenie\":\"dir\",\"opis\":\"lista\"}}]}");
        var executor = new DelegateExecutor((call, token) => Task.FromResult(ToolResult.Ok("OK")));
        using var session = new HarnessSession(llm, Profile, executor, new SessionOptions
        {
            ConfirmMutation = (call, token) =>
            {
                asked++;
                return Task.FromResult(true);
            },
        });

        await session.SendAsync("dir");

        Assert.Equal(1, asked);
    }
}

public sealed class EscapedNewlinesTests
{
    [Theory]
    [InlineData("a\\nb", "a\nb")]
    [InlineData("a\\r\\nb", "a\nb")]
    [InlineData("print(\"x\\\\ny\")", "print(\"x\\\\ny\")")]
    [InlineData("a\nb\\nc", "a\nb\\nc")]
    [InlineData("cd C:\\ODS\\nowy", "cd C:\\ODS\\nowy")]
    [InlineData("type Raporty\\notatki.txt", "type Raporty\\notatki.txt")]
    [InlineData("bez zmian", "bez zmian")]
    public void Fix_TylkoNoweLiniePozaSciezkami(string input, string expected)
        => Assert.Equal(expected, EscapedNewlines.Fix(input, out _));
}

public sealed class RepairEscapesTests
{
    [Theory]
    [InlineData("w folderze Projekty\t1095_WSP.", "w folderze Projekty\\1095_WSP.")]
    [InlineData("C:\tODS", "C:\\ODS")]
    [InlineData("C:\\ODS\bin", "C:\\ODS\\in")]
    [InlineData("Kolumny:\n- a\n\t- b", "Kolumny:\n- a\n\t- b")]
    [InlineData("zwykły tekst", "zwykły tekst")]
    [InlineData("Nie mam dostępu do C:\\\\Windows.", "Nie mam dostępu do C:\\Windows.")]
    [InlineData("udział \\\\serwer\\projekty", "udział \\\\serwer\\projekty")]
    public void TabulatorMiedzyLiterami_ToUkosnik(string input, string expected)
        => Assert.Equal(expected, HarnessSession.RepairEscapes(input));
}

public sealed class SourcesWriteTests : IDisposable
{
    private readonly string _profile = Path.Combine(Path.GetTempPath(), "lokalneai_zrodla_" + Guid.NewGuid().ToString("N"));

    public SourcesWriteTests() => Directory.CreateDirectory(_profile);

    public void Dispose() => Directory.Delete(_profile, true);

    [Fact]
    public void ZapisIOdczyt_TeSamePola()
    {
        var git = new KnowledgeSource { Type = "git", Repo = "https://github.com/Firma/Repo.git", Branch = "main", TokenEnv = "TOKEN" };
        git.Files.Add("README.md");
        git.Files.Add("docs/**/*.md");
        var url = new KnowledgeSource { Type = "url", Url = "https://raw.githubusercontent.com/Firma/Repo/main/opis.md" };

        KnowledgeSync.WriteSources(_profile, new[] { git, url });
        var read = KnowledgeSync.ReadSources(_profile);

        Assert.Equal(2, read.Count);
        Assert.Equal("git", read[0].Type);
        Assert.Equal("https://github.com/Firma/Repo.git", read[0].Repo);
        Assert.Equal("main", read[0].Branch);
        Assert.Equal("TOKEN", read[0].TokenEnv);
        Assert.Equal(new[] { "README.md", "docs/**/*.md" }, read[0].Files);
        Assert.Equal("url", read[1].Type);
        Assert.Equal(url.Url, read[1].Url);
        Assert.Empty(read[1].Files);
    }
}
