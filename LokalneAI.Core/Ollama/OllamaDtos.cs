namespace LokalneAI.Core.Ollama;

// Kształty żądań i odpowiedzi API Ollamy (pola snake_case mapuje Json.Api).
// Opis API: https://github.com/ollama/ollama/blob/main/docs/api.md

/// <summary>Jedna wiadomość rozmowy: rola <c>system</c>, <c>user</c> albo <c>assistant</c>.</summary>
public sealed class ChatMessage
{
    public ChatMessage()
    {
    }

    public ChatMessage(string role, string content)
    {
        Role = role;
        Content = content;
    }

    public string Role { get; set; } = "";

    public string Content { get; set; } = "";

    /// <summary>Tok rozumowania modeli z trybem „myślenia”; Bielik go nie zwraca.</summary>
    public string? Thinking { get; set; }
}

/// <summary>Parametry generowania przekazywane modelowi.</summary>
public sealed class ChatOptions
{
    /// <summary>0 = zawsze ta sama odpowiedź; im wyżej, tym swobodniej.</summary>
    public double? Temperature { get; set; }

    /// <summary>Długość kontekstu w tokenach — wpływa na pamięć karty graficznej.</summary>
    public int? NumCtx { get; set; }

    public int? Seed { get; set; }

    /// <summary>
    /// Najwięcej tokenów odpowiedzi (<c>num_predict</c>). Bez limitu model z trybem myślenia potrafił generować bez końca
    /// i blokować Ollamę (liczy jedno zapytanie naraz) — zmierzone 2026-11-18 na qwen3.5:9b.
    /// </summary>
    public int? NumPredict { get; set; }
}

/// <summary>Żądanie <c>POST /api/chat</c>.</summary>
public sealed class ChatRequest
{
    public string Model { get; set; } = "";

    public List<ChatMessage> Messages { get; set; } = [];

    public bool Stream { get; set; } = true;

    public ChatOptions? Options { get; set; }

    /// <summary>Jak długo trzymać model w pamięci po odpowiedzi, np. <c>5m</c>; null = domyślnie Ollamy.</summary>
    public string? KeepAlive { get; set; }

    /// <summary>„Myślenie” modelu (pole <c>think</c>); false = odpowiada od razu; null = pole nie jest wysyłane.</summary>
    public bool? Think { get; set; }
}

/// <summary>Fragment odpowiedzi strumieniowej czatu. Ostatni ma <see cref="Done"/> i statystyki.</summary>
public sealed class ChatChunk
{
    public string? Model { get; set; }

    public ChatMessage? Message { get; set; }

    public bool Done { get; set; }

    public string? DoneReason { get; set; }

    /// <summary>Czasy w nanosekundach (tak podaje je Ollama).</summary>
    public long? TotalDuration { get; set; }

    public long? LoadDuration { get; set; }

    public int? PromptEvalCount { get; set; }

    public long? PromptEvalDuration { get; set; }

    public int? EvalCount { get; set; }

    public long? EvalDuration { get; set; }

    public string? Error { get; set; }
}

public sealed class ModelDetails
{
    public string? Format { get; set; }

    public string? Family { get; set; }

    public string? ParameterSize { get; set; }

    public string? QuantizationLevel { get; set; }

    public int? ContextLength { get; set; }
}

/// <summary>Model pobrany na dysk (<c>GET /api/tags</c>).</summary>
public sealed class LocalModel
{
    public string Name { get; set; } = "";

    public long Size { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public ModelDetails? Details { get; set; }

    /// <summary>Np. <c>completion</c>, <c>tools</c>, <c>embedding</c>; starsze wersje Ollamy nie podają.</summary>
    public List<string>? Capabilities { get; set; }
}

/// <summary>Model załadowany do pamięci (<c>GET /api/ps</c>).</summary>
public sealed class RunningModel
{
    public string Name { get; set; } = "";

    /// <summary>Cały model w pamięci (bajty), łącznie z kontekstem.</summary>
    public long Size { get; set; }

    /// <summary>Część modelu w pamięci karty graficznej (bajty).</summary>
    public long SizeVram { get; set; }

    public int? ContextLength { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>Udział karty graficznej, 0–1. 1 = cały model w VRAM, 0 = liczy sam procesor.</summary>
    public double GpuShare => Size > 0 ? Math.Clamp((double)SizeVram / Size, 0, 1) : 0;
}

/// <summary>Linia postępu <c>POST /api/pull</c>.</summary>
public sealed class PullProgress
{
    public string? Status { get; set; }

    public string? Digest { get; set; }

    public long? Total { get; set; }

    public long? Completed { get; set; }

    public string? Error { get; set; }
}

internal sealed class TagsResponse
{
    public List<LocalModel> Models { get; set; } = [];
}

internal sealed class PsResponse
{
    public List<RunningModel> Models { get; set; } = [];
}

internal sealed class VersionResponse
{
    public string? Version { get; set; }
}

internal sealed class ErrorResponse
{
    public string? Error { get; set; }
}
