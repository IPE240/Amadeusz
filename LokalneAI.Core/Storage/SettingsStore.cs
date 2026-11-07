using System.Text.Json;

namespace LokalneAI.Core.Storage;

/// <summary>Odczyt i zapis <c>%APPDATA%\LokalneAI\ustawienia.json</c>.</summary>
public sealed class SettingsStore(string? path = null)
{
    public static string DefaultPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LokalneAI", "ustawienia.json");

    public string Path { get; } = path ?? DefaultPath;

    /// <summary>Opis błędu ostatniego odczytu; null = odczyt bez problemu albo brak pliku.</summary>
    public string? LoadError { get; private set; }

    /// <summary>Wczytuje ustawienia. Brak albo uszkodzenie pliku daje ustawienia domyślne.</summary>
    public AppSettings Load()
    {
        LoadError = null;
        try
        {
            if (!File.Exists(Path))
            {
                return new AppSettings();
            }

            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path), Json.Pretty) ?? new AppSettings();
            return settings.Normalize();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            LoadError = e.Message;
            return new AppSettings();
        }
    }

    /// <summary>Zapisuje przez plik tymczasowy, żeby przerwany zapis nie zostawił połowy pliku.</summary>
    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var temporary = Path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Json.Pretty));
        File.Move(temporary, Path, overwrite: true);
    }
}
