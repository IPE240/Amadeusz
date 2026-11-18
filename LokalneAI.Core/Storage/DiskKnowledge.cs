namespace LokalneAI.Core.Storage;

/// <summary>Plik wiedzy z dysku gotowy do wczytania: ścieżka do czytania i nazwa źródła dla modelu.</summary>
public sealed record DiskKnowledgeFile(string Path, string Source);

/// <summary>Wynik zebrania plików wiedzy z dysku: pliki i uwagi dla człowieka (np. „dysk niedostępny — kopia z …”).</summary>
public sealed record DiskKnowledgeResult(IReadOnlyList<DiskKnowledgeFile> Files, IReadOnlyList<string> Notes);

/// <summary>
/// Wiedza umiejętności spoza jej folderu — pliki albo foldery na dysku, np. wspólny glosariusz biura na serwerze
/// (<see cref="AppSettings.DiskKnowledge"/>; ścieżki są tylko w ustawieniach tego komputera, nie w repozytorium).
/// Każde udane czytanie odświeża kopię w <c>%LOCALAPPDATA%\LokalneAI\wiedza_z_dysku\&lt;umiejętność&gt;\</c>; gdy dysk
/// jest niedostępny (laptop poza biurem), program używa tej kopii.
/// </summary>
public static class DiskKnowledge
{
    /// <summary>Rozszerzenia czytane z folderów: notatki i glosariusze.</summary>
    public static readonly string[] Extensions = [".md", ".txt", ".tsv", ".csv"];

    /// <summary>Najwięcej plików z jednego folderu.</summary>
    public const int MaxFilesPerFolder = 300;

    /// <summary>Folder kopii; null = <c>%LOCALAPPDATA%\LokalneAI\wiedza_z_dysku</c>.</summary>
    public static string DefaultCacheRoot => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LokalneAI", "wiedza_z_dysku");

    /// <summary>
    /// Zbiera pliki z <paramref name="paths"/> (plik albo folder; zmienne środowiskowe dozwolone). Dysk sieciowy, który nie
    /// odpowiada w <paramref name="timeout"/>, traktuje jak niedostępny i bierze kopię.
    /// </summary>
    public static DiskKnowledgeResult Collect(string skill, IEnumerable<string>? paths, string? cacheRoot = null, TimeSpan? timeout = null)
    {
        var files = new List<DiskKnowledgeFile>();
        var notes = new List<string>();
        var cache = System.IO.Path.Combine(cacheRoot ?? DefaultCacheRoot, Safe(skill));
        foreach (var raw in (paths ?? []).Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            var path = Environment.ExpandEnvironmentVariables(raw.Trim().Trim('"'));
            var target = System.IO.Path.Combine(cache, Key(path));
            var found = Probe(path, timeout ?? TimeSpan.FromSeconds(3));
            if (found is not null)
            {
                try
                {
                    var fresh = Copy(path, found.Value, target);
                    files.AddRange(fresh);
                    if (fresh.Count == 0)
                    {
                        notes.Add(raw + ": brak plików " + string.Join(", ", Extensions) + ".");
                    }

                    continue;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    notes.Add(raw + ": nie mogę odczytać (" + e.Message + ").");
                }
            }

            var cached = Directory.Exists(target)
                ? Directory.GetFiles(target, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList()
                : [];
            if (cached.Count > 0)
            {
                var when = cached.Max(File.GetLastWriteTime);
                files.AddRange(cached.Select(f => new DiskKnowledgeFile(f, SourceName(path, f, target))));
                notes.Add(raw + " niedostępne — używam kopii z " + when.ToString("dd.MM.yyyy HH:mm") + ".");
            }
            else if (found is null)
            {
                notes.Add(raw + " niedostępne, a kopii jeszcze nie ma.");
            }
        }

        return new DiskKnowledgeResult(files, notes);
    }

    /// <summary>true = plik, false = folder, null = nie ma albo dysk nie odpowiada.</summary>
    private static bool? Probe(string path, TimeSpan timeout)
    {
        var check = Task.Run(() => File.Exists(path) ? true : Directory.Exists(path) ? false : (bool?)null);
        return check.Wait(timeout) ? check.Result : null;
    }

    /// <summary>Kopiuje plik albo pliki folderu do kopii (odświeża ją w całości) i zwraca pliki z kopii.</summary>
    private static List<DiskKnowledgeFile> Copy(string path, bool isFile, string target)
    {
        List<string> sources = isFile
            ? [path]
            : Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Where(f => Extensions.Contains(System.IO.Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .Where(f => !string.Equals(System.IO.Path.GetFileName(f), "CZYTAJ.md", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .Take(MaxFilesPerFolder)
                .ToList();
        var fresh = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(target)!, System.IO.Path.GetFileName(target) + ".nowa");
        if (Directory.Exists(fresh))
        {
            Directory.Delete(fresh, true);
        }

        var result = new List<DiskKnowledgeFile>();
        foreach (var source in sources)
        {
            var relative = isFile ? System.IO.Path.GetFileName(source) : System.IO.Path.GetRelativePath(path, source);
            var copy = System.IO.Path.Combine(fresh, relative);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(copy)!);
            File.Copy(source, copy, true);
        }

        // Podmiana kopii dopiero po udanym skopiowaniu wszystkiego — przerwane czytanie nie psuje poprzedniej kopii.
        if (Directory.Exists(target))
        {
            Directory.Delete(target, true);
        }

        Directory.CreateDirectory(fresh);
        Directory.Move(fresh, target);
        foreach (var file in Directory.GetFiles(target, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            result.Add(new DiskKnowledgeFile(file, SourceName(path, file, target)));
        }

        return result;
    }

    /// <summary>Nazwa źródła dla modelu: „dysk/glosariusz.txt” (bez adresu serwera).</summary>
    private static string SourceName(string path, string file, string target)
        => "dysk/" + System.IO.Path.GetRelativePath(target, file).Replace('\\', '/');

    /// <summary>Nazwa podfolderu kopii dla ścieżki: nazwa pliku/folderu + skrót ścieżki (różne serwery nie mieszają się).</summary>
    private static string Key(string path)
    {
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(path.ToLowerInvariant())))[..8];
        return Safe(System.IO.Path.GetFileName(path.TrimEnd('\\', '/'))) + "_" + hash;
    }

    private static string Safe(string name)
        => string.Concat((name ?? "").Select(c => System.IO.Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}
