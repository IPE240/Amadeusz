using System.Net;
using System.Text;
using LokalneAI.Harness.Profiles;
using LokalneAI.Harness.Session;
using LokalneAI.Harness.Tools;

namespace LokalneAI.Harness.Tests;

/// <summary>
/// Zgłoszenie 2026-11-19: „Amadeusz za łatwo się poddaje”. Brak dostępu do folderu ma kończyć się pytaniem użytkownika
/// (i ponowieniem polecenia po zgodzie), a puste wyszukiwanie — kolejnymi próbami innymi słowami.
/// </summary>
public class GiveUpTests : IDisposable
{
    private readonly string _allowed = Path.Combine(Path.GetTempPath(), "lokalneai_giveup_" + Guid.NewGuid().ToString("N"), "dozwolony");
    private readonly string _outside = Path.Combine(Path.GetTempPath(), "lokalneai_giveup_" + Guid.NewGuid().ToString("N"), "poza");

    public GiveUpTests()
    {
        Directory.CreateDirectory(_allowed);
        Directory.CreateDirectory(_outside);
        File.WriteAllText(Path.Combine(_outside, "raport.txt"), "x");
    }

    public void Dispose()
    {
        foreach (var folder in new[] { _allowed, _outside })
        {
            try
            {
                Directory.Delete(Path.GetDirectoryName(folder)!, true);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public void BrakujaceFoldery_ZBledowWykonawcow()
    {
        var file = Path.Combine(_outside, "raport.txt");
        Assert.Equal([_outside], FolderAccess.MissingFolders("BŁĄD: ścieżka " + file + " jest poza dozwolonymi folderami (C:\\x)."));
        Assert.Equal([_outside], FolderAccess.MissingFolders("BŁĄD: " + _outside + " jest poza dozwolonymi folderami."));
        Assert.Equal([_outside, @"C:\Inny"], FolderAccess.MissingFolders(
            "BŁĄD: polecenie sięga poza dozwolone foldery: " + file + @"; C:\Inny. Dozwolone: C:\x. Folder można dodać w ustawieniach programu."));
        Assert.Empty(FolderAccess.MissingFolders("BŁĄD: nie ma pliku C:\\x\\a.txt"));
        Assert.True(FolderAccess.IsBroad(@"C:\"));
        Assert.False(FolderAccess.IsBroad(@"C:\Projekty"));
    }

    private ScriptedLlm ListOutside() => new(
        "{\"krok\":\"wywolania\",\"wywolania\":[{\"narzedzie\":\"pokaz_zawartosc\",\"argumenty\":{\"sciezka\":\"" + _outside.Replace("\\", "\\\\") + "\"}}]}",
        "{\"krok\":\"odpowiedz\",\"tekst\":\"Gotowe.\"}",
        "{\"krok\":\"wywolania\",\"wywolania\":[{\"narzedzie\":\"pokaz_zawartosc\",\"argumenty\":{\"sciezka\":\"" + _outside.Replace("\\", "\\\\") + "\"}}]}",
        "{\"krok\":\"odpowiedz\",\"tekst\":\"Gotowe.\"}");

    [Fact]
    public async Task FolderPozaDozwolonymi_ProgramPytaIPoZgodziePonawia()
    {
        var roots = new List<string> { _allowed };
        var executor = new FileSystemExecutor(new FileSystemOptions { AllowedRoots = roots, WorkingFolder = _allowed });
        var asked = new List<string>();
        using var session = new HarnessSession(ListOutside(), HarnessProfile.Load(Paths.Profile("pliki")), executor, new SessionOptions
        {
            ConfirmMutation = (call, _) =>
            {
                asked.Add(call.AccessFolder ?? "");
                return Task.FromResult(call.AccessFolder != null);
            },
        });

        var result = await session.SendAsync("co jest w folderze poza?");

        Assert.Equal([_outside], asked);
        Assert.Contains(roots, r => string.Equals(r, _outside, StringComparison.OrdinalIgnoreCase));
        var call = Assert.Single(result.Calls);
        Assert.True(call.Success, call.Result);
        Assert.Contains("raport.txt", call.Result);
    }

    [Fact]
    public async Task FolderPozaDozwolonymi_OdmowaNiePytaDrugiRaz()
    {
        var executor = new FileSystemExecutor(new FileSystemOptions { AllowedRoots = new List<string> { _allowed }, WorkingFolder = _allowed });
        var asked = 0;
        using var session = new HarnessSession(ListOutside(), HarnessProfile.Load(Paths.Profile("pliki")), executor, new SessionOptions
        {
            ConfirmMutation = (call, _) =>
            {
                asked++;
                return Task.FromResult(false);
            },
        });

        var first = await session.SendAsync("co jest w folderze poza?");
        var second = await session.SendAsync("a teraz?");

        Assert.Equal(1, asked);
        Assert.StartsWith("ODRZUCONE — użytkownik nie dał dostępu", Assert.Single(first.Calls).Result);
        Assert.StartsWith("ODRZUCONE — użytkownik nie dał dostępu", Assert.Single(second.Calls).Result);
    }

    [Fact]
    public async Task FolderPozaDozwolonymi_BezOknaZgody_ModelMaZapytac()
    {
        var executor = new FileSystemExecutor(new FileSystemOptions { AllowedRoots = new List<string> { _allowed }, WorkingFolder = _allowed });
        using var session = new HarnessSession(ListOutside(), HarnessProfile.Load(Paths.Profile("pliki")), executor);

        var result = await session.SendAsync("co jest w folderze poza?");

        var call = Assert.Single(result.Calls);
        Assert.False(call.Success);
        Assert.EndsWith(FolderAccess.AskUserHint, call.Result);
    }

    [Theory]
    [InlineData("Niestety nie znalazłem informacji o tym profilu.", true)]
    [InlineData("Nie udało mi się znaleźć tej normy w internecie.", true)]
    [InlineData("Nie mam dostępu do folderu C:\\Projekty.", true)]
    [InlineData("Brak wyników dla tej frazy.", true)]
    [InlineData("Pręt 14 ma przekrój QRO 100x4.", false)]
    [InlineData("Najnowsza wersja Tekli to 2025.", false)]
    public void PoddanieSie(string answer, bool expected) => Assert.Equal(expected, GiveUpGuard.LooksLikeGivingUp(answer));

    [Fact]
    public void Tura_InternetDoTrzechProb_DostepRaz()
    {
        var turn = new GiveUpGuard.Turn();
        var calls = new List<ExecutedCall> { Search("cena S355"), };
        var first = turn.Check("Nie znalazłem ceny.", calls, true, false, false);
        Assert.NotNull(first);
        Assert.Contains("„cena S355”", first);

        calls.AddRange([Search("steel price S355"), Search("S355 cena tona")]);
        Assert.Null(turn.Check("Nie znalazłem ceny.", calls, true, false, false));            // trzy różne frazy — wolno się poddać

        Assert.Equal(GiveUpGuard.AccessRetryText, turn.Check("Nie mam dostępu do tego folderu.", [], false, false, true));
        Assert.Null(turn.Check("Nie mam dostępu do tego folderu.", [], false, false, true));   // tylko raz w turze
        Assert.Null(new GiveUpGuard.Turn().Check("Nie mam dostępu do tego folderu.", [], false, false, false)); // program nie umie zapytać
    }

    private static ExecutedCall Search(string phrase)
        => new() { Tool = BuiltInTools.WebSearchName, ArgumentsJson = "{\"fraza\":\"" + phrase + "\"}", Success = true, Result = "Nic nie znaleziono dla: " + phrase };

    [Fact]
    public async Task PusteWyszukiwanie_ModelSzukaDalejInnymiSlowami()
    {
        var llm = new ScriptedLlm(
            "{\"krok\":\"wywolania\",\"wywolania\":[{\"narzedzie\":\"szukaj_w_internecie\",\"argumenty\":{\"fraza\":\"cena stali S355 tona listopad 2026\"}}]}",
            "{\"krok\":\"odpowiedz\",\"tekst\":\"Nie znalazłem aktualnej ceny.\"}",
            "{\"krok\":\"wywolania\",\"wywolania\":[{\"narzedzie\":\"szukaj_w_internecie\",\"argumenty\":{\"fraza\":\"S355 steel price\"}}]}",
            "{\"krok\":\"odpowiedz\",\"tekst\":\"Cena: patrz wyniki.\"}");
        using var web = new WebAccess(new WebAccessOptions
        {
            Handler = new FakeHttp(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html><body>brak</body></html>", Encoding.UTF8, "text/html") }),
            Resolve = _ => Task.FromResult(new[] { IPAddress.Parse("93.184.216.34") }),
        });
        var settings = new ProfileSettings { Internet = true, MaxSteps = 6 };
        using var session = new HarnessSession(llm, new HarnessProfile("czat", "Odpowiadasz.", [], settings).WithInternet(), null, new SessionOptions { Web = web });

        var result = await session.SendAsync("ile kosztuje tona S355?");

        Assert.Equal(4, llm.Requests.Count);
        Assert.Contains("nie poddawaj się", llm.Requests[2].Messages.Last().Content);
        Assert.Contains("„cena stali S355 tona listopad 2026”", llm.Requests[2].Messages.Last().Content);
        Assert.Contains(result.Warnings, w => w.Contains("za szybko się poddał"));
        Assert.Equal("Cena: patrz wyniki.", result.Text);
    }
}
