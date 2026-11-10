using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LokalneAI.Core.Storage;

/// <summary>
/// Klucze API modeli w chmurze i hasło bazy CDE (klucz <c>cde</c>), zaszyfrowane kontem Windows (DPAPI, zakres: bieżący użytkownik).
/// Plik <c>%APPDATA%\LokalneAI\klucze.json</c> odczyta tylko ten użytkownik na tym komputerze; skopiowany gdzie indziej jest bezużyteczny.
/// Kluczy nie zapisuj w ustawieniach, historii, repozytorium ani w wynikach testów.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CloudKeyStore(string? path = null)
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("LokalneAI.klucze-api.v1");

    public static string DefaultPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LokalneAI", "klucze.json");

    public string Path { get; } = path ?? DefaultPath;

    /// <summary>Klucz dostawcy (<c>anthropic</c>, <c>openai</c>, <c>google</c>); null = brak albo nie da się odszyfrować.</summary>
    public string? Get(string provider)
    {
        if (!ReadAll().TryGetValue(provider, out var sealedKey))
        {
            return null;
        }

        try
        {
            var plain = ProtectedData.Unprotect(Convert.FromBase64String(sealedKey), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception e) when (e is CryptographicException or FormatException)
        {
            return null;
        }
    }

    public bool Has(string provider) => Get(provider) is { Length: > 0 };

    /// <summary>Zapisuje klucz; pusty albo null usuwa go.</summary>
    public void Set(string provider, string? key)
    {
        var all = ReadAll();
        var trimmed = key?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            all.Remove(provider);
        }
        else
        {
            var sealedKey = ProtectedData.Protect(Encoding.UTF8.GetBytes(trimmed), Entropy, DataProtectionScope.CurrentUser);
            all[provider] = Convert.ToBase64String(sealedKey);
        }

        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var temp = Path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(all, Json.Pretty));
        File.Move(temp, Path, overwrite: true);
    }

    private Dictionary<string, string> ReadAll()
    {
        try
        {
            if (File.Exists(Path))
            {
                return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path))
                    ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // Uszkodzony plik: jak brak kluczy (użytkownik wpisze je jeszcze raz).
        }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }
}
