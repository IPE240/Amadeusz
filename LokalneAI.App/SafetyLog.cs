using System.Text.Encodings.Web;
using System.Text.Json;
using LokalneAI.Harness.Tools;

namespace LokalneAI.App;

/// <summary>
/// Dziennik blokad strażnika bezpieczeństwa: <c>%LOCALAPPDATA%\LokalneAI\bezpieczenstwo.log</c>, jeden JSON w linii (czas, kategoria,
/// powód, narzędzie, początek argumentów). Pokazuje, czy ktoś próbował użyć Amadeusza przeciw komputerowi — np. wstrzyknięciem
/// w dokumencie. Plik większy niż 1 MB przechodzi do <c>bezpieczenstwo.log.old</c>.
/// </summary>
internal static class SafetyLog
{
    private const long MaxBytes = 1024 * 1024;
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string Path => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LokalneAI", "bezpieczenstwo.log");

    public static void Write(SafetyVerdict verdict, string? tool, string? arguments)
    {
        var line = JsonSerializer.Serialize(new
        {
            czas = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            kategoria = verdict.Category,
            powod = verdict.Reason,
            narzedzie = tool,
            argumenty = arguments is { Length: > 600 } ? arguments[..600] + "…" : arguments,
        }, Json);
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                if (File.Exists(Path) && new FileInfo(Path).Length > MaxBytes)
                {
                    File.Move(Path, Path + ".old", overwrite: true);
                }

                File.AppendAllText(Path, line + Environment.NewLine);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Dziennik nie może przerwać rozmowy — blokada i tak zadziałała.
        }
    }
}
