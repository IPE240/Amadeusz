using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace LokalneAI.Harness.Llm
{
    /// <summary>
    /// Brama okna LokalneAI na tym komputerze (<c>http://127.0.0.1:11435</c>). Gdy okno jest uruchomione, zapytania
    /// wszystkich programów przechodzą przez jedną kolejkę i liczą tam, gdzie użytkownik wybrał w oknie
    /// („Ustawienia → Gdzie liczyć”: ten komputer albo mocniejszy komputer w biurze). Brama mówi API Ollamy,
    /// więc <see cref="OllamaLlmClient"/> działa z nią bez zmian.
    /// </summary>
    /// <example>
    /// <code>
    /// var llm = new OllamaLlmClient(new OllamaOptions
    /// {
    ///     Url = LocalGateway.ChooseUrl(),   // brama, gdy okno LokalneAI działa; inaczej Ollama na tym PC
    ///     ClientName = "Tekla",             // nazwa w kolejce okna
    /// });
    /// </code>
    /// </example>
    public static class LocalGateway
    {
        /// <summary>Adres bramy na tym komputerze.</summary>
        public const string DefaultUrl = "http://127.0.0.1:11435";

        /// <summary>Adres Ollamy na tym komputerze (gdy okno LokalneAI nie działa).</summary>
        public const string OllamaUrl = "http://127.0.0.1:11434";

        /// <summary>Nagłówek z nazwą programu (<see cref="OllamaOptions.ClientName"/>).</summary>
        public const string ProgramHeader = "X-LokalneAI-Program";

        /// <summary>Czy brama okna LokalneAI odpowiada (<c>GET /lokalneai/info</c>). Nie rzuca wyjątków.</summary>
        public static async Task<bool> IsRunningAsync(int timeoutMs = 500, string url = DefaultUrl)
        {
            try
            {
                using (var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(Math.Max(50, timeoutMs)) })
                using (var response = await http.GetAsync(url.TrimEnd('/') + "/lokalneai/info").ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        return false;
                    }

                    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    return body.IndexOf("\"LokalneAI\"", StringComparison.Ordinal) >= 0;
                }
            }
            catch (Exception e) when (e is HttpRequestException || e is OperationCanceledException || e is InvalidOperationException)
            {
                return false;
            }
        }

        /// <summary>Adres do <see cref="OllamaOptions.Url"/>: brama, gdy działa, inaczej <paramref name="fallbackUrl"/>.</summary>
        public static async Task<string> ChooseUrlAsync(string fallbackUrl = OllamaUrl, int timeoutMs = 500)
            => await IsRunningAsync(timeoutMs).ConfigureAwait(false) ? DefaultUrl : fallbackUrl;

        /// <summary>
        /// To samo co <see cref="ChooseUrlAsync"/>, synchronicznie — do konstruktorów i starszego kodu (.NET Framework 4.8).
        /// Blokuje najwyżej <paramref name="timeoutMs"/> ms. Wybór obowiązuje do końca życia klienta: gdy okno LokalneAI
        /// uruchomi się później, program dalej liczy bezpośrednio w Ollamie.
        /// </summary>
        public static string ChooseUrl(string fallbackUrl = OllamaUrl, int timeoutMs = 500)
            => Task.Run(() => ChooseUrlAsync(fallbackUrl, timeoutMs)).GetAwaiter().GetResult();
    }
}
