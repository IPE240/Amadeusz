using System.Text;
using System.Text.Json;
using LokalneAI.Harness.Office;
using LokalneAI.Harness.Profiles;
using LokalneAI.Harness.Session;
using LokalneAI.Harness.Tools;
using LokalneAI.Harness.Validation;

namespace LokalneAI.Harness.Tests;

/// <summary>Profil „office”: wczytuje się, a przykłady i testy pasują do katalogu narzędzi.</summary>
public sealed class OfficeProfileTests
{
    private static readonly HarnessProfile Profile = HarnessProfile.Load(Paths.Profile("office"));

    [Fact]
    public void PrzykladyITestyPasujaDoNarzedzi()
    {
        var problems = new List<string>();
        foreach (var example in Profile.Examples.Where(e => e.CallsJson != null))
        {
            foreach (var call in Paths.Json(example.CallsJson).EnumerateArray())
            {
                problems.AddRange(Check(call, "przyklady.jsonl: " + example.User, strict: true));
            }
        }

        foreach (var line in File.ReadAllLines(Path.Combine(Profile.Folder, "testy.jsonl"), Encoding.UTF8).Where(l => l.Trim().Length > 0))
        {
            var test = Paths.Json(line);
            foreach (var step in test.GetProperty("kroki").EnumerateArray())
            {
                foreach (var call in step.GetProperty("oczekiwane").EnumerateArray())
                {
                    problems.AddRange(Check(call, "testy.jsonl: " + test.GetProperty("id").GetString(), strict: false));
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void ProfilBezChmuryIInternetu_ZmianyZPytaniem()
    {
        // Treść dokumentów i maili nie może wyjść z biura: bez pola „chmura” (tylko modele lokalne) i bez internetu.
        Assert.Empty(Profile.Settings.CloudProviders);
        Assert.False(Profile.Settings.Internet);
        Assert.Equal(FinishMode.Mixed, Profile.Settings.Finish);
        foreach (var name in new[] { "word_dopisz", "word_zamien", "excel_zapisz", "excel_wstaw_wiersze", "powerpoint_dodaj_slajd", "office_zapisz_jako" })
        {
            Assert.True(Profile.FindTool(name)!.Mutates, name + " ma zmieniać dokument");
        }

        Assert.True(Profile.FindTool("office_otworz")!.FinishesTurn);
        Assert.True(Profile.FindTool("outlook_szkic")!.FinishesTurn);
        Assert.Contains(Profile.Examples, e => e.Question != null && e.Question.Contains("Nie wysyłam"));
    }

    private static IEnumerable<string> Check(JsonElement call, string where, bool strict)
    {
        var name = call.GetProperty("narzedzie").GetString();
        var tool = Profile.FindTool(name);
        if (tool == null)
        {
            return new[] { where + ": nie ma narzędzia " + name };
        }

        if (strict)
        {
            return SchemaValidator.Validate(call.GetProperty("argumenty"), tool.Parameters).Select(e => where + ": " + e);
        }

        // Oczekiwane w testach to podzbiór argumentów — sprawdzamy tylko typy podanych.
        var properties = tool.Parameters.GetProperty("properties");
        var problems = new List<string>();
        foreach (var argument in call.GetProperty("argumenty").EnumerateObject())
        {
            if (!properties.TryGetProperty(argument.Name, out var schema))
            {
                problems.Add(where + ": " + name + " nie ma parametru " + argument.Name);
                continue;
            }

            problems.AddRange(SchemaValidator.Validate(argument.Value, schema).Select(e => where + ": " + e));
        }

        return problems;
    }
}

/// <summary>Decyzje wykonawcy Office’a bez uruchamiania Office’a: co bez pytania, co z pytaniem, co odrzucone.</summary>
public sealed class OfficeDecisionTests : IDisposable
{
    private static readonly HarnessProfile Profile = HarnessProfile.Load(Paths.Profile("office"));
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lokalneai_office_" + Guid.NewGuid().ToString("N"));

    public OfficeDecisionTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "R02"));
        File.WriteAllText(Path.Combine(_root, "notatka.docx"), "x");
        File.WriteAllText(Path.Combine(_root, "zestawienie.xlsx"), "x");
        File.WriteAllText(Path.Combine(_root, "R02", "raport.pdf"), "x");
    }

    public void Dispose() => Directory.Delete(_root, true);

    private OfficeExecutor Executor() => new OfficeExecutor(new OfficeOptions { AllowedRoots = new List<string> { _root }, WorkingFolder = _root, ShowAfterEdit = false });

    private static ToolCall Call(string tool, object args) => new ToolCall(Profile.FindTool(tool), Paths.Json(JsonSerializer.Serialize(args)), 1, 1);

    [Fact]
    public void Czytanie_BezPytania_BrakPlikuISpozaFolderow_Odrzucone()
    {
        var executor = Executor();

        Assert.Equal(ConfirmationKind.None, executor.Decide(Call("word_czytaj", new { sciezka = "notatka.docx" })).Kind);
        Assert.Equal(ConfirmationKind.None, executor.Decide(Call("excel_czytaj", new { sciezka = Path.Combine(_root, "zestawienie.xlsx") })).Kind);
        Assert.Equal(ConfirmationKind.None, executor.Decide(Call("office_otworz", new { sciezka = "R02\\raport.pdf" })).Kind);

        var missing = executor.Decide(Call("word_czytaj", new { sciezka = "nie_ma.docx" }));
        Assert.Equal(ConfirmationKind.Reject, missing.Kind);
        Assert.Contains("nie ma pliku", missing.Message);

        var outside = executor.Decide(Call("word_czytaj", new { sciezka = "C:\\Windows\\win.ini" }));
        Assert.Equal(ConfirmationKind.Reject, outside.Kind);
        Assert.Contains("poza dozwolonymi", outside.Message);
    }

    [Fact]
    public void ZlyProgramDlaRozszerzenia_Odrzucone()
    {
        var decision = Executor().Decide(Call("word_czytaj", new { sciezka = "zestawienie.xlsx" }));

        Assert.Equal(ConfirmationKind.Reject, decision.Kind);
        Assert.Contains("Excel", decision.Message);
        Assert.Contains("excel_czytaj", decision.Message);

        var pdf = Executor().Decide(Call("word_dopisz", new { sciezka = "R02\\raport.pdf", tekst = "a" }));
        Assert.Equal(ConfirmationKind.Reject, pdf.Kind);
        Assert.Contains("nie jest dokumentem", pdf.Message);
    }

    [Fact]
    public void Zmiany_ZPytaniemZTrescia()
    {
        var executor = Executor();

        var replace = executor.Decide(Call("word_zamien", new { sciezka = "notatka.docx", szukaj = "R01", zamien_na = "R02" }));
        Assert.Equal(ConfirmationKind.Ask, replace.Kind);
        Assert.False(replace.Dangerous);
        Assert.Contains("„R01” → „R02”", replace.Message);
        Assert.Contains("wszystkie wystąpienia", replace.Message);

        var append = executor.Decide(Call("word_dopisz", new { sciezka = "notatka.docx", tekst = "Uwagi\\nDruga linia", gdzie = "po_akapicie", akapit = 3, styl = "naglowek2" }));
        Assert.Equal(ConfirmationKind.Ask, append.Kind);
        Assert.StartsWith("Dopisać", append.Message);
        Assert.Contains("po akapicie 3", append.Message);
        Assert.Contains("styl: naglowek2", append.Message);
        Assert.Contains("zamienione na nowe linie: 1", append.Message);

        var create = executor.Decide(Call("word_dopisz", new { sciezka = "R02\\nowy.docx", tekst = "Tytuł" }));
        Assert.Equal(ConfirmationKind.Ask, create.Kind);
        Assert.StartsWith("Utworzyć nowy dokument", create.Message);

        var cell = executor.Decide(Call("excel_zapisz", new { sciezka = "zestawienie.xlsx", komorka = "b10", wartosc = "=SUMA(B2:B9)", arkusz = "Stal" }));
        Assert.Equal(ConfirmationKind.Ask, cell.Kind);
        Assert.Contains("arkusz Stal, B10 = =SUMA(B2:B9)", cell.Message);

        var rows = executor.Decide(Call("excel_wstaw_wiersze", new { sciezka = "profile.xlsx", komorka = "A1", wiersze = new object[] { new object[] { "Profil", "Masa" }, new object[] { "HEB300", 117 } } }));
        Assert.Equal(ConfirmationKind.Ask, rows.Kind);
        Assert.StartsWith("Utworzyć nowy skoroszyt", rows.Message);
        Assert.Contains("2 wierszy od A1", rows.Message);
        Assert.Contains("HEB300 | 117", rows.Message);

        var slide = executor.Decide(Call("powerpoint_dodaj_slajd", new { sciezka = "przeglad.pptx", tytul = "Harmonogram", tekst = "a\nb" }));
        Assert.Equal(ConfirmationKind.Ask, slide.Kind);
        Assert.Contains("Tytuł: Harmonogram", slide.Message);
    }

    [Fact]
    public void ZmianyZBrakamiISpozaFolderow_Odrzucone()
    {
        var executor = Executor();

        Assert.Equal(ConfirmationKind.Reject, executor.Decide(Call("word_dopisz", new { sciezka = "Brak\\nowy.docx", tekst = "a" })).Kind);    // folder nie istnieje
        Assert.Equal(ConfirmationKind.Reject, executor.Decide(Call("word_dopisz", new { sciezka = "notatka.docx", tekst = " " })).Kind);          // pusty tekst
        Assert.Equal(ConfirmationKind.Reject, executor.Decide(Call("word_zamien", new { sciezka = "notatka.docx", szukaj = "", zamien_na = "x" })).Kind);
        var badCell = executor.Decide(Call("excel_zapisz", new { sciezka = "zestawienie.xlsx", komorka = "B2:B9", wartosc = "1" }));
        Assert.Equal(ConfirmationKind.Reject, badCell.Kind);
        Assert.Contains("excel_wstaw_wiersze", badCell.Message);
        Assert.Equal(ConfirmationKind.Reject, executor.Decide(Call("excel_wstaw_wiersze", new { sciezka = "zestawienie.xlsx", komorka = "A1", wiersze = new object[0] })).Kind);
        Assert.Equal(ConfirmationKind.Reject, executor.Decide(Call("excel_zapisz", new { sciezka = "C:\\Windows\\x.xlsx", komorka = "A1", wartosc = "1" })).Kind);
    }

    [Fact]
    public void ZapiszJako_IstniejacyCelNiebezpieczny_ZleRozszerzenieOdrzucone()
    {
        var executor = Executor();

        var fresh = executor.Decide(Call("office_zapisz_jako", new { sciezka = "notatka.docx", nowa_sciezka = "notatka.pdf" }));
        Assert.Equal(ConfirmationKind.Ask, fresh.Kind);
        Assert.False(fresh.Dangerous);
        Assert.StartsWith("Zapisać kopię", fresh.Message);

        var overwrite = executor.Decide(Call("office_zapisz_jako", new { sciezka = "notatka.docx", nowa_sciezka = "R02\\raport.pdf" }));
        Assert.Equal(ConfirmationKind.Ask, overwrite.Kind);
        Assert.True(overwrite.Dangerous);
        Assert.StartsWith("Nadpisać", overwrite.Message);

        var bad = executor.Decide(Call("office_zapisz_jako", new { sciezka = "notatka.docx", nowa_sciezka = "notatka.exe" }));
        Assert.Equal(ConfirmationKind.Reject, bad.Kind);
        Assert.Contains("rozszerzenie", bad.Message);
    }

    [Fact]
    public async Task Outlook_SzkicBezPytania_ZalacznikSpozaFolderowOdrzucony_CzytajPrzedListaOdrzucone()
    {
        var executor = Executor();

        Assert.Equal(ConfirmationKind.None, executor.Decide(Call("outlook_szkic", new { @do = "a@b.pl", temat = "T", tresc = "x", zalaczniki = new[] { "R02\\raport.pdf" } })).Kind);
        var outside = executor.Decide(Call("outlook_szkic", new { @do = "a@b.pl", temat = "T", tresc = "x", zalaczniki = new[] { "C:\\Windows\\win.ini" } }));
        Assert.Equal(ConfirmationKind.Reject, outside.Kind);
        Assert.Contains("poza dozwolonymi", outside.Message);
        Assert.Equal(ConfirmationKind.Reject, executor.Decide(Call("outlook_szkic", new { @do = "a@b.pl", temat = "", tresc = "" })).Kind);
        Assert.Equal(ConfirmationKind.None, executor.Decide(Call("outlook_odebrane", new { ile = 5 })).Kind);

        var read = executor.Decide(Call("outlook_czytaj", new { numer = 1 }));
        Assert.Equal(ConfirmationKind.Reject, read.Kind);
        Assert.Contains("najpierw wywołaj outlook_odebrane", read.Message);
        var result = await executor.ExecuteAsync(Call("outlook_czytaj", new { numer = 1 }), CancellationToken.None);
        Assert.False(result.Success);
        Assert.Contains("outlook_odebrane", result.Text);
    }

    [Fact]
    public async Task SzukajPlikowIPokazZawartosc_PrzezWykonawcePlikow()
    {
        var executor = Executor();

        Assert.Equal(ConfirmationKind.None, executor.Decide(Call("szukaj_plikow", new { wzorzec = "*.docx" })).Kind);
        var found = await executor.ExecuteAsync(Call("szukaj_plikow", new { wzorzec = "raport" }), CancellationToken.None);
        var listed = await executor.ExecuteAsync(Call("pokaz_zawartosc", new { wzorzec = "*.xlsx" }), CancellationToken.None);

        Assert.True(found.Success, found.Text);
        Assert.Contains("R02\\raport.pdf", found.Text);
        Assert.Contains("zestawienie.xlsx", listed.Text);
    }

    [Fact]
    public void FormatowanieWartosciExcela()
    {
        Assert.Equal("12,5", ExcelAutomation.Format(12.5));
        Assert.Equal("117", ExcelAutomation.Format(117.0));
        Assert.Equal("2026-11-17", ExcelAutomation.Format(new DateTime(2026, 11, 17)));
        Assert.Equal("2026-11-17 09:30", ExcelAutomation.Format(new DateTime(2026, 11, 17, 9, 30, 0)));
        Assert.Equal("PRAWDA", ExcelAutomation.Format(true));
        Assert.Equal("#BŁĄD(#NAZWA?)", ExcelAutomation.Format(-2146826259));
        Assert.Equal("", ExcelAutomation.Format(null));
        Assert.Equal("AA", ExcelAutomation.ColumnLetter(27));
        Assert.Equal("Z", ExcelAutomation.ColumnLetter(26));
        Assert.Matches(ExcelAutomation.CellAddress, "AB123");
        Assert.DoesNotMatch(ExcelAutomation.CellAddress, "A1:B2");
        Assert.Matches(ExcelAutomation.RangeAddress, "A1:B2");
        Assert.Equal(2, WordAutomation.CountOccurrences("R01 i r01 i R02", "r01", matchCase: false));
        Assert.Equal(1, WordAutomation.CountOccurrences("R01 i r01 i R02", "r01", matchCase: true));
        Assert.Equal(OfficeKind.Word, OfficeHost.KindFor("a.DOCX"));
        Assert.Equal(OfficeKind.Excel, OfficeHost.KindFor("a.csv"));
        Assert.Null(OfficeHost.KindFor("a.pdf"));
    }
}

/// <summary>
/// Próby na prawdziwym Microsoft 365 (COM): Word, Excel i PowerPoint w folderze tymczasowym, bez pokazywania okien
/// (<c>ShowAfterEdit = false</c>). Na komputerze bez Office’a testy przechodzą bez sprawdzania. Outlooka nie ruszamy
/// (szkic otworzyłby okno w poczcie użytkownika).
/// </summary>
public sealed class OfficeLiveTests : IDisposable
{
    private static readonly HarnessProfile Profile = HarnessProfile.Load(Paths.Profile("office"));
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lokalneai_office_live_" + Guid.NewGuid().ToString("N"));

    public OfficeLiveTests() => Directory.CreateDirectory(_root);

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
                Thread.Sleep(500); // Office bywa jeszcze w folderze
            }
        }
    }

    private OfficeExecutor Executor() => new OfficeExecutor(new OfficeOptions { AllowedRoots = new List<string> { _root }, WorkingFolder = _root, ShowAfterEdit = false });

    private static ToolCall Call(string tool, object args) => new ToolCall(Profile.FindTool(tool), Paths.Json(JsonSerializer.Serialize(args)), 1, 1);

    private static async Task<ToolResult> Run(OfficeExecutor executor, string tool, object args)
    {
        var call = Call(tool, args);
        var decision = executor.Decide(call);
        Assert.NotEqual(ConfirmationKind.Reject, decision.Kind);
        var result = await executor.ExecuteAsync(call, CancellationToken.None);
        Assert.True(result.Success, tool + ": " + result.Text);
        return result;
    }

    [Fact]
    public async Task Word_NowyDokumentCzytanieZamianaIPdf()
    {
        if (!Com.IsInstalled("Word.Application"))
        {
            return;
        }

        var executor = Executor();
        var created = await Run(executor, "word_dopisz", new { sciezka = "notatka.docx", tekst = "Spotkanie 2026-11-17", styl = "naglowek1" });
        var appended = await Run(executor, "word_dopisz", new { sciezka = "notatka.docx", tekst = "Termin oddania: 30.11\nBrak decyzji o kotwach R01", styl = "lista" });
        var inserted = await Run(executor, "word_dopisz", new { sciezka = "notatka.docx", tekst = "Obecni: zespół ODS", gdzie = "po_akapicie", akapit = 1 });
        var text = await Run(executor, "word_czytaj", new { sciezka = "notatka.docx" });
        var replaced = await Run(executor, "word_zamien", new { sciezka = "notatka.docx", szukaj = "r01", zamien_na = "R02" });
        var after = await Run(executor, "word_czytaj", new { sciezka = "notatka.docx", od_akapitu = 4, ile_akapitow = 1 });
        var pdf = await Run(executor, "office_zapisz_jako", new { sciezka = "notatka.docx", nowa_sciezka = "notatka.pdf" });
        var missing = await executor.ExecuteAsync(Call("word_zamien", new { sciezka = "notatka.docx", szukaj = "nie ma takiego tekstu", zamien_na = "x" }), CancellationToken.None);

        Assert.Contains("utworzono dokument", created.Text);
        Assert.Contains("akapity 1–1", created.Text);
        Assert.Contains("akapity 2–3 (razem akapitów: 3)", appended.Text);
        Assert.Contains("akapity 2–2 (razem akapitów: 4)", inserted.Text);
        Assert.Contains("akapitów: 4", text.Text);
        Assert.Contains("1: Spotkanie 2026-11-17", text.Text);
        Assert.Contains("2: Obecni: zespół ODS", text.Text);
        Assert.Contains("4: Brak decyzji o kotwach R01", text.Text);
        Assert.Contains("zamieniono 1× „r01” → „R02”", replaced.Text);
        Assert.Contains("4: Brak decyzji o kotwach R02", after.Text);
        Assert.DoesNotContain("1: Spotkanie", after.Text);
        Assert.Contains("notatka.pdf", pdf.Text);
        Assert.True(new FileInfo(Path.Combine(_root, "notatka.pdf")).Length > 1000);
        Assert.False(missing.Success);
        Assert.Contains("nie ma tekstu", missing.Text);
    }

    [Fact]
    public async Task Excel_NowySkoroszytFormulaICzytanie()
    {
        if (!Com.IsInstalled("Excel.Application"))
        {
            return;
        }

        var executor = Executor();
        var created = await Run(executor, "excel_wstaw_wiersze", new
        {
            sciezka = "profile.xlsx",
            komorka = "A1",
            wiersze = new object[] { new object[] { "Profil", "Masa kg/m", "Data" }, new object[] { "HEB300", 117, "2026-11-17" }, new object[] { "IPE200", "22,4", "" } },
        });
        var sum = await Run(executor, "excel_zapisz", new { sciezka = "profile.xlsx", komorka = "B4", wartosc = "=SUMA(B2:B3)" });
        var english = await Run(executor, "excel_zapisz", new { sciezka = "profile.xlsx", komorka = "B5", wartosc = "=SUM(B2:B3)" });
        var text = await Run(executor, "excel_zapisz", new { sciezka = "profile.xlsx", komorka = "A4", wartosc = "Razem" });
        var table = await Run(executor, "excel_czytaj", new { sciezka = "profile.xlsx" });
        var formulas = await Run(executor, "excel_czytaj", new { sciezka = "profile.xlsx", zakres = "A4:B5", formuly = true });
        var wrongSheet = await executor.ExecuteAsync(Call("excel_czytaj", new { sciezka = "profile.xlsx", arkusz = "Beton" }), CancellationToken.None);
        var badFormula = await executor.ExecuteAsync(Call("excel_zapisz", new { sciezka = "profile.xlsx", komorka = "C9", wartosc = "=NIEMATAKIEJ(1)" }), CancellationToken.None);

        Assert.Contains("wpisano 9 komórek", created.Text);
        Assert.Contains("A1:C3", created.Text);
        Assert.Contains("B4 = 139,4", sum.Text);
        Assert.Contains("B5 = 139,4", english.Text);
        Assert.Contains("angielskimi", english.Text);
        Assert.Contains("A4 = Razem", text.Text);
        Assert.Contains("wierszy 5, kolumn 3", table.Text);
        Assert.Contains("| A | B | C", table.Text);
        Assert.Contains("2 | HEB300 | 117 | 2026-11-17", table.Text);
        Assert.Contains("3 | IPE200 | 22,4 |", table.Text);
        Assert.Contains("4 | Razem | 139,4", table.Text);
        Assert.Contains("=SUMA(B2:B3)", formulas.Text);
        Assert.False(wrongSheet.Success);
        Assert.Contains("Arkusze:", wrongSheet.Text);
        Assert.False(badFormula.Success);
        Assert.Contains("nie przyjął formuły", badFormula.Text);
    }

    [Fact]
    public async Task PowerPoint_NowaPrezentacjaICzytanie()
    {
        if (!Com.IsInstalled("PowerPoint.Application"))
        {
            return;
        }

        var executor = Executor();
        var created = await Run(executor, "powerpoint_dodaj_slajd", new { sciezka = "przeglad.pptx", tytul = "Harmonogram", tekst = "Projekt wykonawczy do 30.11\nWarsztat od 15.12" });
        var second = await Run(executor, "powerpoint_dodaj_slajd", new { sciezka = "przeglad.pptx", tytul = "Ryzyka", tekst = "Brak decyzji o kotwach" });
        var text = await Run(executor, "powerpoint_czytaj", new { sciezka = "przeglad.pptx" });
        var pdf = await Run(executor, "office_zapisz_jako", new { sciezka = "przeglad.pptx", nowa_sciezka = "przeglad.pdf" });

        Assert.Contains("utworzono prezentację", created.Text);
        Assert.Contains("dodano slajd 1 „Harmonogram” (slajdów: 1)", created.Text);
        Assert.Contains("dodano slajd 2 „Ryzyka” (slajdów: 2)", second.Text);
        Assert.Contains("slajdów: 2", text.Text);
        Assert.Contains("Slajd 1:", text.Text);
        Assert.Contains("Harmonogram", text.Text);
        Assert.Contains("Projekt wykonawczy do 30.11 / Warsztat od 15.12", text.Text);
        Assert.Contains("Slajd 2:", text.Text);
        Assert.Contains("Ryzyka", text.Text);
        Assert.True(new FileInfo(Path.Combine(_root, "przeglad.pdf")).Length > 1000, pdf.Text);
    }

    [Fact]
    public async Task Sesja_CzytanieBezPytania_ZmianaZPytaniemIKoniecAuto()
    {
        if (!Com.IsInstalled("Word.Application"))
        {
            return;
        }

        var asked = new List<string>();
        var llm = new ScriptedLlm(
            "{\"krok\":\"wywolania\",\"wywolania\":[{\"narzedzie\":\"word_dopisz\",\"argumenty\":{\"sciezka\":\"pismo.docx\",\"tekst\":\"Dzień dobry,\\nw załączeniu raport R02.\"}}]}",
            "{\"krok\":\"wywolania\",\"wywolania\":[{\"narzedzie\":\"word_czytaj\",\"argumenty\":{\"sciezka\":\"pismo.docx\"}}]}",
            "{\"krok\":\"odpowiedz\",\"tekst\":\"Pismo ma 2 akapity.\"}");
        using var session = new HarnessSession(llm, Profile, Executor(), new SessionOptions
        {
            ConfirmMutation = (call, token) =>
            {
                asked.Add(call.ConfirmationMessage);
                return Task.FromResult(true);
            },
        });

        var write = await session.SendAsync("napisz pismo.docx: Dzień dobry, w załączeniu raport R02.");
        var read = await session.SendAsync("ile akapitów ma pismo?");

        // Zmiana: pytanie, a po zgodzie koniec bez kolejnego wołania modelu (tryb „mieszane”).
        Assert.Single(asked);
        Assert.StartsWith("Utworzyć nowy dokument Worda", asked[0]);
        Assert.Contains("w załączeniu raport R02.", asked[0]);
        Assert.Equal(1, write.ModelCalls);
        Assert.Equal(TurnKind.Answer, write.Kind);
        Assert.True(write.Calls[0].Success, write.Calls[0].Result);
        // Czytanie: bez pytania, model widzi wynik i odpowiada.
        Assert.Equal(2, read.ModelCalls);
        Assert.Equal("Pismo ma 2 akapity.", read.Text);
        Assert.Contains("akapitów: 2", read.Calls[0].Result);
    }
}
