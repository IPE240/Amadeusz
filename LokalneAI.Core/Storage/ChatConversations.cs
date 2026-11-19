using System.Text.Json;

namespace LokalneAI.Core.Storage;

/// <summary>
/// Kosz rozmów: plik przeniesiony do kosza dostaje datę przeniesienia (czas modyfikacji), a pliki starsze niż
/// <see cref="KeepDays"/> dni są kasowane przy następnym otwarciu listy. Zabezpieczenie przed kliknięciem × przez pomyłkę.
/// </summary>
public static class ConversationTrash
{
    /// <summary>Ile dni rozmowa leży w koszu, zanim program ją skasuje.</summary>
    public const int KeepDays = 7;

    /// <summary>Przenosi plik do folderu kosza (bez nadpisywania) i zapisuje datę przeniesienia. Zwraca nową ścieżkę.</summary>
    public static string Move(string file, string trashFolder, string? name = null)
    {
        Directory.CreateDirectory(trashFolder);
        var baseName = Path.GetFileNameWithoutExtension(name ?? file);
        var target = Path.Combine(trashFolder, baseName + ".json");
        for (var n = 2; File.Exists(target); n++)
        {
            target = Path.Combine(trashFolder, baseName + "_" + n + ".json");
        }

        File.Move(file, target);
        File.SetLastWriteTime(target, DateTime.Now); // od teraz liczy się termin w koszu
        return target;
    }

    /// <summary>Kasuje z kosza rozmowy przeniesione tam ponad <paramref name="days"/> dni temu. Zwraca liczbę skasowanych; błędy pomija.</summary>
    public static int Purge(string trashFolder, int days = KeepDays, DateTime? now = null)
    {
        if (!Directory.Exists(trashFolder))
        {
            return 0;
        }

        var limit = (now ?? DateTime.Now).AddDays(-days);
        var removed = 0;
        foreach (var file in Directory.GetFiles(trashFolder, "*.json"))
        {
            try
            {
                if (File.GetLastWriteTime(file) < limit)
                {
                    File.Delete(file);
                    removed++;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // plik zajęty albo bez uprawnień — spróbujemy następnym razem
            }
        }

        return removed;
    }
}

/// <summary>
/// Rozmowy zakładki Czat: <c>Dokumenty\Amadeusz\Czat\</c>. Grupa = podfolder (<c>Czat\&lt;grupa&gt;\</c>),
/// „Niezgrupowane” = sam <c>Czat\</c>; plik rozmowy <c>&lt;id&gt;.json</c> w formacie <see cref="CodeConversation"/>.
/// Kosz: <c>Czat\kosz\</c>, kasowany po <see cref="ConversationTrash.KeepDays"/> dniach. Czat nie pracuje na plikach,
/// więc nie potrzebuje folderów roboczych jak Code — wszystko w jednym, widocznym miejscu.
/// </summary>
public static class ChatConversationStore
{
    /// <summary>Podfolder kosza.</summary>
    public const string TrashName = "kosz";

    /// <summary><c>Dokumenty\Amadeusz\Czat</c>.</summary>
    public static string DefaultRoot => Path.Combine(CodeConversationStore.DefaultUngroupedFolder, "Czat");

    /// <summary>Grupy (podfoldery bez kosza), alfabetycznie: nazwa i pełna ścieżka.</summary>
    public static IReadOnlyList<(string Name, string Folder)> Groups(string root)
        => Directory.Exists(root)
            ? Directory.GetDirectories(root)
                .Where(d => !string.Equals(Path.GetFileName(d), TrashName, StringComparison.OrdinalIgnoreCase))
                .Where(d => !Path.GetFileName(d).StartsWith('.'))
                .OrderBy(d => Path.GetFileName(d), StringComparer.CurrentCultureIgnoreCase)
                .Select(d => (Path.GetFileName(d), d))
                .ToList()
            : [];

    /// <summary>Plik rozmowy w folderze grupy.</summary>
    public static string PathFor(string folder, string id) => Path.Combine(folder, id + ".json");

    /// <summary>Rozmowy w folderze grupy, najnowsze pierwsze (uszkodzone pliki pomija).</summary>
    public static IReadOnlyList<CodeConversationInfo> List(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return [];
        }

        var list = new List<CodeConversationInfo>();
        foreach (var file in Directory.GetFiles(folder, "*.json"))
        {
            if (CodeConversationStore.Load(file) is { } conversation)
            {
                list.Add(new CodeConversationInfo(Path.GetFileNameWithoutExtension(file), conversation.Title, conversation.Activity, file));
            }
        }

        return list.OrderByDescending(c => c.Updated).ToList();
    }

    /// <summary>Zapisuje rozmowę w folderze grupy (przez plik tymczasowy). Zwraca ścieżkę.</summary>
    public static string Save(string folder, CodeConversation conversation)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        if (string.IsNullOrWhiteSpace(conversation.Id))
        {
            conversation.Id = CodeConversationStore.NewId();
        }

        Directory.CreateDirectory(folder);
        var path = PathFor(folder, conversation.Id);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(conversation, Json.Pretty));
        File.Move(temporary, path, overwrite: true);
        return path;
    }

    /// <summary>Przenosi rozmowę do <c>Czat\kosz\</c>; przy okazji kasuje z kosza rozmowy starsze niż termin. Null, gdy pliku nie było.</summary>
    public static string? MoveToTrash(string root, string folder, string id)
    {
        var path = PathFor(folder, id);
        if (!File.Exists(path))
        {
            return null;
        }

        var trash = Path.Combine(root, TrashName);
        var target = ConversationTrash.Move(path, trash);
        ConversationTrash.Purge(trash);
        return target;
    }

    /// <summary>Przenosi rozmowę do innej grupy. Zwraca nową ścieżkę.</summary>
    public static string Move(string fromFolder, string toFolder, string id)
    {
        Directory.CreateDirectory(toFolder);
        var target = PathFor(toFolder, id);
        if (File.Exists(target))
        {
            throw new IOException("W grupie docelowej jest już rozmowa " + id + ".");
        }

        File.Move(PathFor(fromFolder, id), target);
        return target;
    }

    /// <summary>Nowa grupa (podfolder). Nazwa bez znaków niedozwolonych w nazwie folderu. Zwraca folder.</summary>
    public static string CreateGroup(string root, string name)
    {
        var folder = Path.Combine(root, SafeName(name));
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>Zmienia nazwę grupy (folderu). Zwraca nowy folder.</summary>
    public static string RenameGroup(string folder, string name)
    {
        var target = Path.Combine(Path.GetDirectoryName(folder)!, SafeName(name));
        if (!string.Equals(folder, target, StringComparison.OrdinalIgnoreCase))
        {
            Directory.Move(folder, target);
        }

        return target;
    }

    /// <summary>Usuwa grupę: jej rozmowy idą do kosza (z nazwą grupy w nazwie pliku), pusty folder znika.</summary>
    public static void DeleteGroup(string root, string folder)
    {
        var trash = Path.Combine(root, TrashName);
        foreach (var file in Directory.GetFiles(folder, "*.json"))
        {
            ConversationTrash.Move(file, trash, Path.GetFileName(folder) + "_" + Path.GetFileName(file));
        }

        if (!Directory.EnumerateFileSystemEntries(folder).Any())
        {
            Directory.Delete(folder);
        }
    }

    /// <summary>Kasuje z kosza rozmowy starsze niż termin (przy otwarciu panelu).</summary>
    public static int PurgeTrash(string root) => ConversationTrash.Purge(Path.Combine(root, TrashName));

    private static string SafeName(string name)
    {
        var clean = string.Concat((name ?? "").Trim().Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim('.', ' ');
        if (clean.Length == 0 || string.Equals(clean, TrashName, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Nazwa grupy nie może być pusta ani „" + TrashName + "”.", nameof(name));
        }

        return clean;
    }
}
