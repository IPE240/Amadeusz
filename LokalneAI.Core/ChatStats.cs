using LokalneAI.Core.Ollama;

namespace LokalneAI.Core;

/// <summary>Statystyki jednej odpowiedzi: ile tokenów, jak szybko i gdzie liczył model.</summary>
/// <param name="PromptTokens">Tokeny wejścia (polecenie systemowe, historia, pytanie).</param>
/// <param name="OutputTokens">Tokeny odpowiedzi.</param>
/// <param name="Load">Ładowanie modelu do pamięci (zero, gdy już był załadowany).</param>
/// <param name="PromptEval">Czytanie wejścia przez model.</param>
/// <param name="Eval">Pisanie odpowiedzi.</param>
/// <param name="Total">Cały czas po stronie Ollamy.</param>
/// <param name="FirstToken">Od kliknięcia do pierwszego słowa odpowiedzi.</param>
/// <param name="GpuShare">Udział karty graficznej 0–1 (z <c>/api/ps</c>); null = nieznany.</param>
/// <param name="Device">Nazwa karty graficznej, jeśli znana.</param>
public sealed record ChatStats(
    int PromptTokens,
    int OutputTokens,
    TimeSpan Load,
    TimeSpan PromptEval,
    TimeSpan Eval,
    TimeSpan Total,
    TimeSpan? FirstToken,
    double? GpuShare,
    string? Device)
{
    public double? TokensPerSecond => OutputTokens > 0 && Eval > TimeSpan.Zero ? OutputTokens / Eval.TotalSeconds : null;

    public static ChatStats From(ChatChunk done, TimeSpan? firstToken, TimeSpan wallClock, double? gpuShare, string? device)
    {
        ArgumentNullException.ThrowIfNull(done);
        return new ChatStats(
            done.PromptEvalCount ?? 0,
            done.EvalCount ?? 0,
            FromNanoseconds(done.LoadDuration),
            FromNanoseconds(done.PromptEvalDuration),
            FromNanoseconds(done.EvalDuration),
            done.TotalDuration is > 0 ? FromNanoseconds(done.TotalDuration) : wallClock,
            firstToken,
            gpuShare,
            device);
    }

    public static TimeSpan FromNanoseconds(long? nanoseconds)
        => nanoseconds is > 0 ? TimeSpan.FromTicks(nanoseconds.Value / 100) : TimeSpan.Zero;

    /// <summary>Gdzie liczy model, np. „NVIDIA GeForce RTX 4060: 73% modelu w VRAM, reszta na CPU”.</summary>
    public static string DescribePlacement(double? gpuShare, string? device)
    {
        var card = string.IsNullOrWhiteSpace(device) ? "karta graficzna" : device;
        return gpuShare switch
        {
            null => "nie wiem, gdzie liczył model",
            >= 0.995 => $"{card}: cały model w VRAM",
            <= 0.005 => "procesor (CPU): model poza kartą graficzną",
            _ => string.Format(Formats.Pl, "{0}: {1:0}% modelu w VRAM, reszta na CPU", card, gpuShare.Value * 100),
        };
    }

    /// <summary>Linia pod odpowiedzią, np. „…: 73% modelu w VRAM · 113 tok. · 22,6 tok/s · pierwsze słowo po 1,2 s · razem 6,8 s”.</summary>
    public string Describe()
    {
        var pl = Formats.Pl;
        var parts = new List<string> { DescribePlacement(GpuShare, Device) };
        if (OutputTokens > 0)
        {
            parts.Add(string.Format(pl, "{0} tok.", OutputTokens));
        }

        if (TokensPerSecond is double speed)
        {
            parts.Add(string.Format(pl, "{0:0.0} tok/s", speed));
        }

        if (FirstToken is TimeSpan first)
        {
            parts.Add(string.Format(pl, "pierwsze słowo po {0:0.0} s", first.TotalSeconds));
        }

        parts.Add(string.Format(pl, "razem {0:0.0} s", Total.TotalSeconds));
        if (Load.TotalSeconds >= 0.5)
        {
            parts.Add(string.Format(pl, "w tym ładowanie modelu {0:0.0} s", Load.TotalSeconds));
        }

        return string.Join(" · ", parts);
    }
}
