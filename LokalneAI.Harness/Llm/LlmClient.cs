using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LokalneAI.Harness.Llm
{
    /// <summary>Jedna wiadomość rozmowy z modelem: <c>system</c>, <c>user</c> albo <c>assistant</c>.</summary>
    public sealed class LlmMessage
    {
        /// <summary>Tworzy wiadomość o podanej roli i treści.</summary>
        public LlmMessage(string role, string content)
        {
            Role = role;
            Content = content;
        }

        /// <summary>Rola: <c>system</c>, <c>user</c> albo <c>assistant</c>.</summary>
        public string Role { get; }

        /// <summary>Treść wiadomości.</summary>
        public string Content { get; }
    }

    /// <summary>Zapytanie do modelu: wiadomości i schemat JSON, którego odpowiedź musi się trzymać.</summary>
    public sealed class LlmRequest
    {
        /// <summary>Cała rozmowa od najstarszej wiadomości: polecenie systemowe i historia.</summary>
        public IReadOnlyList<LlmMessage> Messages { get; set; }

        /// <summary>JSON Schema odpowiedzi (tekst); null = zwykły tekst.</summary>
        public string JsonSchema { get; set; }

        /// <summary>Temperatura; null = ustawienie klienta.</summary>
        public double? Temperature { get; set; }

        /// <summary>
        /// Najwięcej tokenów odpowiedzi dla tego jednego zapytania; null = ustawienie klienta. <see cref="OllamaLlmClient"/>
        /// wysyła to jako <c>num_predict</c> (np. 1 przy liczeniu tokenów samego polecenia systemowego).
        /// </summary>
        public int? MaxTokens { get; set; }
    }

    /// <summary>Odpowiedź modelu z liczbą tokenów (do licznika kontekstu).</summary>
    public sealed class LlmResponse
    {
        /// <summary>Tekst odpowiedzi; przy <see cref="LlmRequest.JsonSchema"/> to JSON zgodny ze schematem.</summary>
        public string Content { get; set; }

        /// <summary>Tokeny wejścia — cały kontekst wysłany do modelu (Ollama liczy tu cały prompt, także z pamięci podręcznej).</summary>
        public int PromptTokens { get; set; }

        /// <summary>Tokeny wygenerowane przez model.</summary>
        public int OutputTokens { get; set; }

        /// <summary>Czas całego zapytania.</summary>
        public TimeSpan Duration { get; set; }

        /// <summary>Czas ładowania modelu do pamięci (Ollama); zero, gdy model był już załadowany albo serwer tego nie podaje.</summary>
        public TimeSpan LoadDuration { get; set; }

        /// <summary>
        /// Czas czytania promptu przez model (Ollama: <c>prompt_eval_duration</c>) — zanim napisze pierwszy token. Część promptu
        /// z pamięci podręcznej (ten sam początek co w poprzednim zapytaniu) czyta się prawie od razu. Zero, gdy serwer nie podaje.
        /// </summary>
        public TimeSpan PromptDuration { get; set; }

        /// <summary>Czas pisania odpowiedzi (Ollama: <c>eval_duration</c>); zero, gdy serwer nie podaje.</summary>
        public TimeSpan OutputDuration { get; set; }

        /// <summary>Nazwa modelu, który odpowiedział.</summary>
        public string Model { get; set; }
    }

    /// <summary>Dostęp do modelu. Gotowe: <see cref="OllamaLlmClient"/> i <see cref="OpenAiCompatibleLlmClient"/>.</summary>
    public interface ILlmClient
    {
        /// <summary>Nazwa modelu (do opisu i dziennika).</summary>
        string ModelName { get; }

        /// <summary>Długość kontekstu w tokenach (do licznika kontekstu).</summary>
        int ContextLength { get; }

        /// <summary>
        /// Wysyła rozmowę i czeka na całą odpowiedź (bez strumienia). Gdy serwer nie odpowiada: <see cref="LlmUnavailableException"/>;
        /// inny błąd serwera albo przekroczony czas: <see cref="LlmException"/>; przerwanie: <see cref="OperationCanceledException"/>.
        /// </summary>
        Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken);
    }

    /// <summary>Błąd rozmowy z modelem, z komunikatem dla człowieka.</summary>
    public class LlmException : Exception
    {
        /// <summary>Tworzy błąd z komunikatem dla człowieka (po polsku).</summary>
        public LlmException(string message, Exception inner = null)
            : base(message, inner)
        {
        }
    }

    /// <summary>Serwer modelu nie odpowiada (wyłączony, zły adres, zapora).</summary>
    public sealed class LlmUnavailableException : LlmException
    {
        /// <summary>Tworzy błąd z adresem serwera, który nie odpowiedział.</summary>
        public LlmUnavailableException(string url, Exception inner = null)
            : base("Serwer modelu nie odpowiada pod adresem " + url + ". Sprawdź, czy działa i czy adres jest dobry.", inner)
        {
            Url = url;
        }

        /// <summary>Adres serwera, który nie odpowiedział.</summary>
        public string Url { get; }
    }
}
