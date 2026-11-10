namespace LokalneAI.App;

/// <summary>
/// Argumenty uruchomienia. Zwykle puste; <c>--autotest</c> służy do kontroli okna bez klikania:
/// <code>LokalneAI.exe --autotest "pytanie" --wynik C:\folder [--url http://...] [--model nazwa] [--rozwin]</code>
/// Wtedy okno zadaje pytanie, zapisuje <c>okno.png</c> i <c>wynik.json</c> i się zamyka,
/// nie zmieniając ustawień ani historii użytkownika. Pytanie „-” = tylko zrzut okna po starcie,
/// <c>--rozwin</c> = zrzut zakładki Ustawienia (to samo co <c>--zakladka ustawienia</c>).
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

    /// <summary><c>--zakladka czat|code|ustawienia</c>: zakładka na zrzucie.</summary>
    public string? Tab { get; init; }

    /// <summary><c>--cel PC-ODS12</c>: licz na innym komputerze (tylko w tym uruchomieniu kontroli).</summary>
    public string? ComputeOn { get; init; }

    /// <summary><c>--port 11445</c>: port bramy w kontroli (bez tego dowolny wolny, żeby nie zająć portu okna użytkownika).</summary>
    public int? Port { get; init; }

    /// <summary>
    /// <c>--udostepnij</c>: w kontroli brama przyjmuje zapytania „z innych komputerów”, ale tylko z 127.0.0.1
    /// (próba sieci na jednym komputerze, bez otwierania portu w sieci i bez okienka zapory).
    /// </summary>
    public bool Share { get; init; }

    /// <summary><c>--ustawienia plik.json</c>: ustawienia z innego pliku (kontrola z modelami w chmurze bez ruszania ustawień użytkownika).</summary>
    public string? SettingsPath { get; init; }

    /// <summary><c>--klucze plik.json</c>: klucze API z innego pliku (kontrola). Bez tego — klucze użytkownika.</summary>
    public string? KeysPath { get; init; }

    public static StartupOptions Parse(IReadOnlyList<string> args)
    {
        string? question = null, output = null, url = null, model = null, harness = null, connection = null, person = null, roots = null;
        string? tab = null, computeOn = null, settingsPath = null, keysPath = null;
        int? port = null;
        var expand = false;
        var share = false;
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
                case "--zakladka":
                    tab = value;
                    i++;
                    break;
                case "--cel":
                    computeOn = value;
                    i++;
                    break;
                case "--port":
                    port = int.TryParse(value, out var number) ? number : null;
                    i++;
                    break;
                case "--udostepnij":
                    share = true;
                    break;
                case "--ustawienia":
                    settingsPath = value;
                    i++;
                    break;
                case "--klucze":
                    keysPath = value;
                    i++;
                    break;
            }
        }

        return new StartupOptions(question, output, url, model, expand)
        {
            Harness = harness,
            HarnessConnection = connection,
            HarnessPerson = person,
            HarnessRoots = roots,
            Tab = tab ?? (expand ? "ustawienia" : null),
            ComputeOn = computeOn,
            Port = port,
            Share = share,
            SettingsPath = settingsPath,
            KeysPath = keysPath,
        };
    }
}
