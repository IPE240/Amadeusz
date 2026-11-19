using System.Security.Cryptography;
using System.Text.Json;

namespace LokalneAI.Core.Storage;

/// <summary>Grupa rozmów zakładki Code = folder roboczy. Rozmowy grupy leżą w jej <c>.amadeusz\</c>.</summary>
public sealed class CodeGroupSetting
{
    /// <summary>Nazwa w panelu rozmów (domyślnie nazwa folderu).</summary>
    public string Name { get; set; } = "";

    /// <summary>Folder grupy: tu pracuje asystent (dozwolony folder i folder bieżący) i tu, w <c>.amadeusz\</c>, są rozmowy.</summary>
    public string Folder { get; set; } = "";

    /// <summary>Grupa zwinięta w panelu rozmów.</summary>
    public bool Collapsed { get; set; }
}

/// <summary>Jedna rozmowa zakładki Code zapisana w pliku <c>&lt;folder grupy&gt;\.amadeusz\&lt;id&gt;.json</c>.</summary>
public sealed class CodeConversation
{
    /// <summary>Wersja formatu pliku.</summary>
    public int Version { get; set; } = 1;

    /// <summary>Id = nazwa pliku bez <c>.json</c>, np. <c>2026-11-17_143012_a3f9</c>.</summary>
    public string Id { get; set; } = "";

    /// <summary>Tytuł w panelu (domyślnie początek pierwszego polecenia).</summary>
    public string Title { get; set; } = "";

    public DateTime Created { get; set; }

    public DateTime Updated { get; set; }

    /// <summary>
    /// Kiedy użytkownik ostatnio wysłał polecenie (Enter) — po tym sortuje się lista rozmów. Zapis rozmowy bez nowego polecenia
    /// (przejście do innej, zmiana nazwy, przeniesienie) go nie zmienia. Null w plikach sprzed tej zmiany — wtedy <see cref="Updated"/>.
    /// </summary>
    public DateTime? LastPrompt { get; set; }

    /// <summary>Data do sortowania listy rozmów: ostatnie polecenie, a w starszych plikach ostatni zapis.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public DateTime Activity => LastPrompt ?? Updated;

    /// <summary>Profile, z którymi rozmowa była ostatnio prowadzona (np. <c>tekla</c>, <c>komputer</c>).</summary>
    public List<string> Profiles { get; set; } = [];

    /// <summary>Model ostatniej tury (do opisu).</summary>
    public string? Model { get; set; }

    /// <summary>To, co widać w oknie: polecenia, wywołania z wynikami, odpowiedzi.</summary>
    public List<CodeConversationEntry> Entries { get; set; } = [];

    /// <summary>To, co dostaje model przy wznowieniu: wiadomości <c>user</c>/<c>assistant</c> bez polecenia systemowego.</summary>
    public List<CodeConversationMessage> History { get; set; } = [];
}

/// <summary>Wpis widoczny w oknie rozmowy.</summary>
public sealed class CodeConversationEntry
{
    /// <summary>Rodzaj wpisu panelu: <c>User</c>, <c>Call</c>, <c>Answer</c>, <c>Question</c>, <c>Warning</c>, <c>Error</c>, <c>Info</c>.</summary>
    public string Kind { get; set; } = "";

    public string Text { get; set; } = "";

    public string? Detail { get; set; }

    /// <summary>Wynik wywołania: true/false; null = nie dotyczy.</summary>
    public bool? Success { get; set; }

    /// <summary>Tokeny i czasy przy wpisie (zakładka Code z „Pokaż tokeny i czasy”); null = brak.</summary>
    public string? Metrics { get; set; }

    /// <summary>Wpisy podrzędne (rodzaj <c>Agent</c> = podagent: jego polecenie, wywołania, kroki i odpowiedź); null = zwykły wpis.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public List<CodeConversationEntry>? Children { get; set; }
}

/// <summary>Wiadomość dla modelu.</summary>
public sealed class CodeConversationMessage
{
    /// <summary><c>user</c> albo <c>assistant</c>.</summary>
    public string Role { get; set; } = "";

    public string Content { get; set; } = "";
}

/// <summary>Rozmowa na liście w panelu (bez treści).</summary>
public sealed record CodeConversationInfo(string Id, string Title, DateTime Updated, string Path);

/// <summary>
/// Pliki rozmów zakładki Code. Zasada: rozmowa leży wyłącznie w folderze swojej grupy, w podfolderze <c>.amadeusz</c>
/// (tworzonym przy pierwszym zapisie, z <c>.gitignore</c>, żeby rozmowy — także z danymi z bazy CDE — nie trafiły do repozytorium).
/// Nic poza tym folderem: ustawienia programu pamiętają tylko listę folderów grup.
/// </summary>
public static class CodeConversationStore
{
    /// <summary>Nazwa podfolderu z rozmowami w folderze grupy.</summary>
    public const string FolderName = ".amadeusz";

    /// <summary>Podfolder <c>.amadeusz</c> na rozmowy przeniesione do kosza.</summary>
    public const string TrashName = "kosz";

    /// <summary>Folder grupy „Niezgrupowane”, gdy użytkownik nie wskazał innego: <c>Dokumenty\Amadeusz</c>.</summary>
    public static string DefaultUngroupedFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Amadeusz");

    /// <summary><c>&lt;folder grupy&gt;\.amadeusz</c>.</summary>
    public static string StoreFolder(string groupFolder) => Path.Combine(Expand(groupFolder), FolderName);

    /// <summary>Plik rozmowy o tym id w grupie.</summary>
    public static string PathFor(string groupFolder, string id) => Path.Combine(StoreFolder(groupFolder), id + ".json");

    /// <summary>Nowe id: data i godzina (pliki sortują się same) plus losowy dopisek.</summary>
    public static string NewId(DateTime? now = null)
        => (now ?? DateTime.Now).ToString("yyyy-MM-dd_HHmmss", System.Globalization.CultureInfo.InvariantCulture) + "_"
            + Convert.ToHexString(RandomNumberGenerator.GetBytes(2)).ToLowerInvariant();

    /// <summary>Folder z rozwiniętymi zmiennymi środowiskowymi (np. <c>%USERPROFILE%</c>), pełna ścieżka.</summary>
    public static string Expand(string folder) => Path.GetFullPath(Environment.ExpandEnvironmentVariables((folder ?? "").Trim()));

    /// <summary>Rozmowy grupy, najnowsze pierwsze. Brak folderu = pusta lista; uszkodzone pliki są pomijane.</summary>
    public static IReadOnlyList<CodeConversationInfo> List(string groupFolder)
    {
        var folder = StoreFolder(groupFolder);
        if (!Directory.Exists(folder))
        {
            return [];
        }

        PurgeTrash(groupFolder);
        var list = new List<CodeConversationInfo>();
        foreach (var file in Directory.GetFiles(folder, "*.json"))
        {
            var conversation = Load(file);
            if (conversation is not null)
            {
                list.Add(new CodeConversationInfo(Path.GetFileNameWithoutExtension(file), conversation.Title, conversation.Activity, file));
            }
        }

        return list.OrderByDescending(c => c.Updated).ToList();
    }

    /// <summary>Wczytuje rozmowę; null, gdy pliku nie ma albo jest uszkodzony.</summary>
    public static CodeConversation? Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var conversation = JsonSerializer.Deserialize<CodeConversation>(File.ReadAllText(path), Json.Pretty);
            if (conversation is null)
            {
                return null;
            }

            conversation.Id = Path.GetFileNameWithoutExtension(path);
            conversation.Profiles ??= [];
            conversation.Entries ??= [];
            conversation.History ??= [];
            conversation.Title ??= "";
            return conversation;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Zapisuje rozmowę w <c>&lt;folder grupy&gt;\.amadeusz\&lt;id&gt;.json</c> (przez plik tymczasowy). Tworzy folder
    /// <c>.amadeusz</c> z <c>.gitignore</c>, jeśli go nie ma. Zwraca ścieżkę pliku.
    /// </summary>
    public static string Save(string groupFolder, CodeConversation conversation)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        if (string.IsNullOrWhiteSpace(conversation.Id))
        {
            conversation.Id = NewId();
        }

        var folder = EnsureStoreFolder(groupFolder);
        var path = Path.Combine(folder, conversation.Id + ".json");
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(conversation, Json.Pretty));
        File.Move(temporary, path, overwrite: true);
        return path;
    }

    /// <summary>
    /// Przenosi rozmowę do <c>.amadeusz\kosz\</c>. Rozmowy leżą tam <see cref="ConversationTrash.KeepDays"/> dni
    /// (zabezpieczenie przed pomyłką), potem program je kasuje (<see cref="PurgeTrash"/>). Zwraca nową ścieżkę albo null, gdy pliku nie było.
    /// </summary>
    public static string? MoveToTrash(string groupFolder, string id)
    {
        var path = PathFor(groupFolder, id);
        if (!File.Exists(path))
        {
            return null;
        }

        var target = ConversationTrash.Move(path, TrashFolder(groupFolder));
        PurgeTrash(groupFolder);
        return target;
    }

    /// <summary><c>&lt;folder grupy&gt;\.amadeusz\kosz</c>.</summary>
    public static string TrashFolder(string groupFolder) => Path.Combine(StoreFolder(groupFolder), TrashName);

    /// <summary>Kasuje z kosza grupy rozmowy starsze niż termin (przy wczytaniu listy grupy).</summary>
    public static int PurgeTrash(string groupFolder) => ConversationTrash.Purge(TrashFolder(groupFolder));

    /// <summary>Przenosi plik rozmowy do innej grupy (folderu). Zwraca nową ścieżkę.</summary>
    public static string Move(string fromGroupFolder, string toGroupFolder, string id)
    {
        var source = PathFor(fromGroupFolder, id);
        var folder = EnsureStoreFolder(toGroupFolder);
        var target = Path.Combine(folder, id + ".json");
        if (File.Exists(target))
        {
            throw new IOException("W grupie docelowej jest już rozmowa " + id + ".");
        }

        File.Move(source, target);
        return target;
    }

    private static string EnsureStoreFolder(string groupFolder)
    {
        var folder = StoreFolder(groupFolder);
        if (!Directory.Exists(folder))
        {
            Directory.CreateDirectory(folder);
        }

        var ignore = Path.Combine(folder, ".gitignore");
        if (!File.Exists(ignore))
        {
            File.WriteAllText(ignore, "# Rozmowy Amadeusza (mogą zawierać dane z bazy CDE i wyniki poleceń) — nie do repozytorium.\n*\n");
        }

        return folder;
    }
}
