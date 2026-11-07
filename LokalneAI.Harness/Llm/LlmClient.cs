using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LokalneAI.Harness.Llm
{
    /// <summary>Jedna wiadomość rozmowy z modelem: <c>system</c>, <c>user</c> albo <c>assistant</c>.</summary>
    public sealed class LlmMessage
    {
        public LlmMessage(string role, string content)
        {
            Role = role;
            Content = content;
        }

        public string Role { get; }

        public string Content { get; }
    }

    /// <summary>Zapytanie do modelu: wiadomości i schemat JSON, którego odpowiedź musi się trzymać.</summary>
    public sealed class LlmRequest
    {
        public IReadOnlyList<LlmMessage> Messages { get; set; }

        /// <summary>JSON Schema odpowiedzi (tekst); null = zwykły tekst.</summary>
        public string JsonSchema { get; set; }

        /// <summary>Temperatura; null = ustawienie klienta.</summary>
        public double? Temperature { get; set; }
    }

    /// <summary>Odpowiedź modelu z liczbą tokenów (do licznika kontekstu).</summary>
    public sealed class LlmResponse
    {
        public string Content { get; set; }

        /// <summary>Tokeny wejścia — cały kontekst wysłany do modelu (Ollama liczy tu cały prompt, także z pamięci podręcznej).</summary>
        public int PromptTokens { get; set; }

        public int OutputTokens { get; set; }

        public TimeSpan Duration { get; set; }

        public TimeSpan LoadDuration { get; set; }

        public string Model { get; set; }
    }

    /// <summary>Dostęp do modelu. Gotowe: <see cref="OllamaLlmClient"/> i <see cref="OpenAiCompatibleLlmClient"/>.</summary>
    public interface ILlmClient
    {
        /// <summary>Nazwa modelu (do opisu i dziennika).</summary>
        string ModelName { get; }

        /// <summary>Długość kontekstu w tokenach (do licznika kontekstu).</summary>
        int ContextLength { get; }

        Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken);
    }

    /// <summary>Błąd rozmowy z modelem, z komunikatem dla człowieka.</summary>
    public class LlmException : Exception
    {
        public LlmException(string message, Exception inner = null)
            : base(message, inner)
        {
        }
    }

    /// <summary>Serwer modelu nie odpowiada (wyłączony, zły adres, zapora).</summary>
    public sealed class LlmUnavailableException : LlmException
    {
        public LlmUnavailableException(string url, Exception inner = null)
            : base("Serwer modelu nie odpowiada pod adresem " + url + ". Sprawdź, czy działa i czy adres jest dobry.", inner)
        {
            Url = url;
        }

        public string Url { get; }
    }
}
