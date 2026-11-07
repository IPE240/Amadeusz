using System.Net;

namespace LokalneAI.Core.Ollama;

/// <summary>Błąd rozmowy z Ollamą, z komunikatem dla człowieka po polsku.</summary>
public class OllamaException : Exception
{
    public OllamaException(string message, HttpStatusCode? statusCode = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode? StatusCode { get; }
}

/// <summary>Ollama nie odpowiada: nie jest uruchomiona, adres jest zły albo blokuje zapora.</summary>
public sealed class OllamaUnavailableException(string url, Exception? inner = null)
    : OllamaException($"Ollama nie odpowiada pod adresem {url}. Sprawdź, czy jest uruchomiona.", null, inner)
{
    public string Url { get; } = url;
}

/// <summary>Ollama działa, ale nie ma żądanego modelu.</summary>
public sealed class ModelNotFoundException(string model, string? detail = null)
    : OllamaException(
        $"W Ollamie nie ma modelu „{model}”. Pobierz go przyciskiem „Pobierz model” albo poleceniem: ollama pull {model}",
        HttpStatusCode.NotFound)
{
    public string Model { get; } = model;

    /// <summary>Oryginalny komunikat Ollamy.</summary>
    public string? Detail { get; } = detail;
}
