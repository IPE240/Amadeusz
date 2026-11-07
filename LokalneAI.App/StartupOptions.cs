namespace LokalneAI.App;

/// <summary>
/// Argumenty uruchomienia. Zwykle puste; <c>--autotest</c> służy do kontroli okna bez klikania:
/// <code>LokalneAI.exe --autotest "pytanie" --wynik C:\folder [--url http://...] [--model nazwa] [--rozwin]</code>
/// Wtedy okno zadaje pytanie, zapisuje <c>okno.png</c> i <c>wynik.json</c> i się zamyka,
/// nie zmieniając ustawień ani historii użytkownika. Pytanie „-” = tylko zrzut okna po starcie,
/// <c>--rozwin</c> = zrzut z rozwiniętymi ustawieniami.
/// </summary>
public sealed record StartupOptions(string? AutoTestQuestion, string? OutputDir, string? Url, string? Model, bool ExpandSettings = false)
{
    public bool IsAutoTest => AutoTestQuestion is not null;

    /// <summary><c>--harness tekla</c>: kontrola okna pół-harnessu; polecenia w <c>--autotest</c> rozdziela „||”.</summary>
    public string? Harness { get; init; }

    /// <summary><c>--polaczenie plik.json</c> dla profilu z bazą (tylko w kontroli).</summary>
    public string? HarnessConnection { get; init; }

    /// <summary><c>--ja uuid</c> — person_id pytającego (tylko w kontroli).</summary>
    public string? HarnessPerson { get; init; }

    /// <summary>
    /// <c>--foldery "C:;C:"</c> — dozwolone foldery profili „pliki” i „komputer” w kontroli. Bez tego kontrola pracuje
    /// w folderze tymczasowym, bo zgody zatwierdza sama.
    /// </summary>
    public string? HarnessRoots { get; init; }

    public static StartupOptions Parse(IReadOnlyList<string> args)
    {
        string? question = null, output = null, url = null, model = null, harness = null, connection = null, person = null, roots = null;
        var expand = false;
        for (var i = 0; i < args.Count; i++)
        {
            var value = i + 1 < args.Count ? args[i + 1] : null;
            switch (args[i])
            {
                case "--autotest":
                    question = value ?? "-";
                    i++;
                    break;
                case "--wynik":
                    output = value;
                    i++;
                    break;
                case "--url":
                    url = value;
                    i++;
                    break;
                case "--model":
                    model = value;
                    i++;
                    break;
                case "--rozwin":
                    expand = true;
                    break;
                case "--harness":
                    harness = value;
                    i++;
                    break;
                case "--polaczenie":
                    connection = value;
                    i++;
                    break;
                case "--ja":
                    person = value;
                    i++;
                    break;
                case "--foldery":
                    roots = value;
                    i++;
                    break;
            }
        }

        return new StartupOptions(question, output, url, model, expand) { Harness = harness, HarnessConnection = connection, HarnessPerson = person, HarnessRoots = roots };
    }
}
