using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LokalneAI.Core.Storage;

/// <summary>
/// Amadeusz o sobie, w postaci dla modelu (narzędzia umiejętności <c>amadeusz</c>): ustawienia bez danych logowania i osobowych,
/// końcówka dziennika, wybór umiejętności. Tylko odczyt — nic tu nie zapisuje.
/// </summary>
public static class SelfView
{
    /// <summary>
    /// Fragmenty nazw pól, których model nie dostaje: stare połączenie z bazą CDE (serwer, użytkownik, person_id — <c>cde…</c>)
    /// i wszystko, co wygląda na hasło, klucz albo poświadczenie.
    /// </summary>
    private static readonly string[] HiddenParts = ["person", "password", "haslo", "secret", "token", "credential", "apikey", "klucz"];

    /// <summary>Czy pole ustawień jest ukryte przed modelem.</summary>
    public static bool IsHiddenField(string name)
        => name.StartsWith("cde", StringComparison.OrdinalIgnoreCase)
           || HiddenParts.Any(part => name.Contains(part, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Ustawienia jako JSON (nazwy pól jak w <c>ustawienia.json</c>) bez pól ukrytych (<see cref="IsHiddenField"/>); <paramref name="fields"/>
    /// — nazwy pól po przecinku (puste = wszystkie). Dłuższy wynik jest ucinany do <paramref name="limit"/> znaków.
    /// </summary>
    public static string SettingsForModel(AppSettings settings, string? fields, int limit = 6000)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var node = JsonSerializer.SerializeToNode(settings, Json.Pretty) as JsonObject ?? new JsonObject();
        foreach (var key in node.Select(p => p.Key).Where(IsHiddenField).ToList())
        {
            node.Remove(key);
        }

        var wanted = SplitNames(fields);
        var notes = new List<string>();
        if (wanted.Count > 0)
        {
            var picked = new JsonObject();
            foreach (var name in wanted)
            {
                var match = node.FirstOrDefault(p => string.Equals(p.Key, name, StringComparison.OrdinalIgnoreCase));
                if (match.Key is null)
                {
                    notes.Add(IsHiddenField(name) ? name + " — pole ukryte (dane logowania)" : name + " — nie ma takiego pola");
                    continue;
                }

                picked[match.Key] = match.Value?.DeepClone();
            }

            node = picked;
        }

        var text = node.ToJsonString(Json.Pretty);
        if (text.Length > limit)
        {
            text = text[..limit] + "\n… (ucięte — podaj nazwy pól w „pola”)";
        }

        return (notes.Count == 0 ? "" : "Uwaga: " + string.Join("; ", notes) + ".\n") + text;
    }

    /// <summary>
    /// Ostatnie <paramref name="lines"/> niepustych linii pliku dziennika (czytanie bez blokowania zapisu programu); najwyżej
    /// <paramref name="limit"/> znaków od końca. Brak pliku = opis zamiast błędu.
    /// </summary>
    public static string LogTail(string path, int lines, int limit = 6000)
    {
        if (!File.Exists(path))
        {
            return "Dziennik jest pusty — nie ma pliku " + System.IO.Path.GetFileName(path) + ".";
        }

        var count = Math.Clamp(lines, 1, 200);
        var last = new Queue<string>();
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var reader = new StreamReader(stream, Encoding.UTF8))
        {
            while (reader.ReadLine() is { } line)
            {
                if (line.Trim().Length == 0)
                {
                    continue;
                }

                last.Enqueue(line);
                if (last.Count > count)
                {
                    last.Dequeue();
                }
            }
        }

        if (last.Count == 0)
        {
            return "Dziennik " + System.IO.Path.GetFileName(path) + " jest pusty.";
        }

        var text = string.Join("\n", last);
        return text.Length > limit ? "… " + text[^limit..] : text;
    }

    /// <summary>Nazwy rozdzielone przecinkiem, średnikiem, plusem albo spacją, bez powtórzeń (wielkość liter bez znaczenia).</summary>
    public static List<string> SplitNames(string? text)
        => (text ?? "").Split([',', ';', '+', ' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Select(n => n.Trim().Trim('"', '\'', '„', '”'))
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Nowy wybór umiejętności: <c>ustaw</c> (domyślnie) — dokładnie <paramref name="names"/>, <c>dodaj</c> — zaznaczone i te,
    /// <c>usun</c> — zaznaczone bez tych. Kolejność jak w <paramref name="order"/> (lista umiejętności w oknie).
    /// </summary>
    public static List<string> ApplySkillMode(IReadOnlyList<string> current, IReadOnlyList<string> names, string? mode, IReadOnlyList<string> order)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(names);
        ArgumentNullException.ThrowIfNull(order);
        var chosen = (mode ?? "ustaw").Trim().ToLowerInvariant() switch
        {
            "dodaj" => current.Concat(names),
            "usun" or "usuń" => current.Where(c => !names.Contains(c, StringComparer.OrdinalIgnoreCase)),
            _ => names,
        };
        var set = chosen.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return order.Where(set.Contains).ToList();
    }
}
