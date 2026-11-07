using System.Text;
using System.Text.Json;

namespace LokalneAI.Core.Storage;

/// <summary>Jedno pytanie z odpowiedzią — wiersz pliku historii.</summary>
public sealed class HistoryEntry
{
    public DateTimeOffset Time { get; set; }

    public string Model { get; set; } = "";

    public double Temperature { get; set; }

    public int ContextLength { get; set; }

    public string? SystemPrompt { get; set; }

    public bool RememberedConversation { get; set; }

    public string Question { get; set; } = "";

    public string Answer { get; set; } = "";

    /// <summary><c>ok</c>, <c>przerwano</c> albo <c>blad</c>.</summary>
    public string Outcome { get; set; } = "ok";

    public string? Error { get; set; }

    public int? PromptTokens { get; set; }

    public int? OutputTokens { get; set; }

    public double? TokensPerSecond { get; set; }

    public double? Seconds { get; set; }

    public double? GpuShare { get; set; }
}

/// <summary>
/// Historia pytań w <c>%LOCALAPPDATA%\LokalneAI\historia.jsonl</c> (jeden JSON w linii).
/// Zostaje tylko na tym PC; posłuży do budowy zestawu pytań testowych.
/// </summary>
public sealed class HistoryLog(string? path = null)
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static string DefaultPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LokalneAI", "historia.jsonl");

    public string Path { get; } = path ?? DefaultPath;

    public void Append(HistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.AppendAllText(Path, JsonSerializer.Serialize(entry, Json.Line) + "\n", Utf8NoBom);
    }
}
