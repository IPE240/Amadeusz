using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LokalneAI.Harness.Llm
{
    /// <summary>Ustawienia połączenia z Ollamą (lokalną albo na serwerze w biurze).</summary>
    public sealed class OllamaOptions
    {
        /// <summary>Np. <c>http://127.0.0.1:11434</c> albo <c>http://serwer-ai:11434</c>.</summary>
        public string Url { get; set; } = "http://127.0.0.1:11434";

        /// <summary>Tag modelu w Ollamie, np. <c>SpeakLeash/bielik-11b-v3.0-instruct:Q4_K_M</c>. Model musi być pobrany na serwerze.</summary>
        public string Model { get; set; } = "SpeakLeash/bielik-11b-v3.0-instruct:Q4_K_M";

        /// <summary>Długość kontekstu. Ollama po cichu obcina dłuższy prompt, więc podajemy ją zawsze.</summary>
        public int ContextLength { get; set; } = 8192;

        /// <summary>Losowość odpowiedzi. Do poleceń i SQL 0,1: odpowiedzi prawie powtarzalne.</summary>
        public double Temperature { get; set; } = 0.1;

        /// <summary>Jak długo trzymać model w pamięci po odpowiedzi, np. <c>10m</c>; null = domyślnie serwera.</summary>
        public string KeepAlive { get; set; }

        /// <summary>Limit na jedną odpowiedź modelu (ładowanie modelu potrafi trwać kilkanaście sekund, a w kolejce bramy można czekać).</summary>
        public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Nazwa programu widoczna w kolejce okna LokalneAI, np. „Tekla”, „CDE”. Idzie w nagłówku <c>X-LokalneAI-Program</c>;
        /// Ollama go pomija. Null = bez nazwy.
        /// </summary>
        public string ClientName { get; set; }

        /// <summary>
        /// Dodatkowe nagłówki HTTP każdego zapytania, np. <c>X-LokalneAI-Przekazane: 1</c> — brama okna liczy wtedy na tym
        /// komputerze, choć „Gdzie liczyć” wskazuje inny (podagent na własnej karcie). Null = bez dodatkowych nagłówków.
        /// </summary>
        public IDictionary<string, string> Headers { get; set; }

        /// <summary>
        /// Pozwól modelowi „myśleć” przed odpowiedzią (pole <c>think</c> w <c>/api/chat</c>). Domyślnie false: zapytanie niesie
        /// <c>"think": false</c>, bo modele z trybem myślenia (qwen3.5) potrafiły myśleć kilka minut przed krokiem w JSON,
        /// a Ollama przyjmuje to pole także dla modeli bez myślenia (Bielik, Gemma — sprawdzone 2026-11-17). True = pole nie jest wysyłane.
        /// </summary>
        public bool AllowThinking { get; set; }

        /// <summary>
        /// Najwięcej tokenów jednej odpowiedzi (<c>num_predict</c>); 0 = bez limitu. Domyślnie 4096: krok w JSON to zwykle kilkaset
        /// tokenów, a model, który wpadnie w pętlę, nie blokuje Ollamy (liczy jedno zapytanie naraz) na długie minuty.
        /// </summary>
        public int MaxTokens { get; set; } = 4096;
    }

    /// <summary>Klient Ollamy: <c>POST /api/chat</c> bez strumienia, z <c>format</c> = JSON Schema.</summary>
    public sealed class OllamaLlmClient : ILlmClient, ICloudAwareLlmClient, IDisposable
    {
        private readonly HttpClient _http;
        private readonly OllamaOptions _options;
        private readonly string _baseUrl;

        /// <summary>Tworzy klienta. Wystarczy jeden na cały program, dla wszystkich sesji. <c>handler</c> podmienia HTTP w testach; null = zwykłe połączenie.</summary>
        public OllamaLlmClient(OllamaOptions options, HttpMessageHandler handler = null)
        {
            _options = options ?? new OllamaOptions();
            _baseUrl = (_options.Url ?? "").Trim().TrimEnd('/');
            if (_baseUrl.Length == 0)
            {
                _baseUrl = "http://127.0.0.1:11434";
            }

            if (_baseUrl.IndexOf("://", StringComparison.Ordinal) < 0)
            {
                _baseUrl = "http://" + _baseUrl;
            }

            _http = handler == null ? new HttpClient() : new HttpClient(handler, false);
            _http.Timeout = Timeout.InfiniteTimeSpan;
        }

        /// <inheritdoc/>
        public string ModelName => _options.Model;

        /// <inheritdoc/>
        public int ContextLength => _options.ContextLength;

        /// <summary>
        /// null = liczy sieć biura; <see cref="CloudProvider.OllamaCloudId"/> dla modeli „-cloud” (liczy ollama.com);
        /// <see cref="CloudProvider.OutsideListId"/>, gdy adres Ollamy jest w internecie.
        /// </summary>
        public string CloudProviderId => CloudProvider.IsOllamaCloudModel(_options.Model) ? CloudProvider.OllamaCloudId
            : CloudProvider.ClassifyUrl(_baseUrl) == null ? null : CloudProvider.OutsideListId;

        /// <inheritdoc/>
        public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var body = BuildBody(request);
            using (var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            using (var message = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/api/chat"))
            {
                limit.CancelAfter(_options.RequestTimeout);
                message.Content = new StringContent(body, Encoding.UTF8, "application/json");
                if (!string.IsNullOrWhiteSpace(_options.ClientName))
                {
                    message.Headers.TryAddWithoutValidation(LocalGateway.ProgramHeader, Uri.EscapeDataString(_options.ClientName.Trim()));
                }

                if (_options.Headers != null)
                {
                    foreach (var header in _options.Headers)
                    {
                        if (!string.IsNullOrWhiteSpace(header.Key))
                        {
                            message.Headers.TryAddWithoutValidation(header.Key, header.Value ?? "");
                        }
                    }
                }

                var clock = Stopwatch.StartNew();
                HttpResponseMessage response;
                try
                {
                    response = await _http.SendAsync(message, limit.Token).ConfigureAwait(false);
                }
                catch (HttpRequestException e)
                {
                    throw new LlmUnavailableException(_baseUrl, e);
                }
                catch (OperationCanceledException e) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new LlmException("Model nie odpowiedział w ciągu " + _options.RequestTimeout.TotalSeconds.ToString("0", Text.Pl) + " s.", e);
                }

                using (response)
                {
                    var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new LlmException(DescribeError(response.StatusCode, text));
                    }

                    return Parse(text, clock.Elapsed);
                }
            }
        }

        /// <summary>Zamyka połączenie HTTP. Wywołuj przy zamykaniu programu, nie po każdej sesji.</summary>
        public void Dispose() => _http.Dispose();

        internal string BuildBody(LlmRequest request)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new Utf8JsonWriter(stream, Text.CompactWriter))
                {
                    writer.WriteStartObject();
                    writer.WriteString("model", _options.Model);
                    writer.WriteStartArray("messages");
                    foreach (var m in request.Messages)
                    {
                        writer.WriteStartObject();
                        writer.WriteString("role", m.Role);
                        writer.WriteString("content", m.Content);
                        if (m.Images != null)
                        {
                            // Ollama: obrazy w base64 przy wiadomości; model bez obsługi obrazów (Bielik) ich nie dostaje — gospodarz pilnuje.
                            writer.WriteStartArray("images");
                            foreach (var image in m.Images)
                            {
                                writer.WriteStringValue(Convert.ToBase64String(image));
                            }

                            writer.WriteEndArray();
                        }

                        writer.WriteEndObject();
                    }

                    writer.WriteEndArray();
                    writer.WriteBoolean("stream", false);
                    if (!_options.AllowThinking)
                    {
                        writer.WriteBoolean("think", false);
                    }

                    if (!string.IsNullOrEmpty(request.JsonSchema))
                    {
                        writer.WritePropertyName("format");
                        using (var schema = JsonDocument.Parse(request.JsonSchema))
                        {
                            schema.RootElement.WriteTo(writer);
                        }
                    }

                    if (!string.IsNullOrEmpty(_options.KeepAlive))
                    {
                        writer.WriteString("keep_alive", _options.KeepAlive);
                    }

                    writer.WriteStartObject("options");
                    writer.WriteNumber("temperature", request.Temperature ?? _options.Temperature);
                    writer.WriteNumber("num_ctx", _options.ContextLength);
                    var maxTokens = request.MaxTokens ?? _options.MaxTokens;
                    if (maxTokens > 0)
                    {
                        writer.WriteNumber("num_predict", maxTokens);
                    }

                    writer.WriteEndObject();
                    writer.WriteEndObject();
                }

                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        private string DescribeError(HttpStatusCode status, string body)
        {
            var detail = body;
            try
            {
                using (var document = JsonDocument.Parse(body))
                {
                    if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("error", out var error))
                    {
                        detail = error.ValueKind == JsonValueKind.String ? error.GetString() : error.GetRawText();
                    }
                }
            }
            catch (JsonException)
            {
                // Treść błędu nie jest JSON-em — zostaje surowa.
            }

            if (status == HttpStatusCode.NotFound && detail != null && detail.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Na serwerze modelu nie ma modelu „" + _options.Model + "”. Pobierz go: ollama pull " + _options.Model;
            }

            return "Serwer modelu zgłosił błąd (" + (int)status + "): " + Text.Shorten(detail, 300);
        }

        private static LlmResponse Parse(string json, TimeSpan elapsed)
        {
            try
            {
                using (var document = JsonDocument.Parse(json))
                {
                    var root = document.RootElement;
                    var content = root.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var c)
                        ? c.GetString()
                        : "";
                    return new LlmResponse
                    {
                        Content = content ?? "",
                        PromptTokens = ReadInt(root, "prompt_eval_count"),
                        OutputTokens = ReadInt(root, "eval_count"),
                        Duration = root.TryGetProperty("total_duration", out var total) && total.TryGetInt64(out var ns)
                            ? TimeSpan.FromTicks(ns / 100)
                            : elapsed,
                        LoadDuration = ReadDuration(root, "load_duration"),
                        PromptDuration = ReadDuration(root, "prompt_eval_duration"),
                        OutputDuration = ReadDuration(root, "eval_duration"),
                        Model = root.TryGetProperty("model", out var model) ? model.GetString() : null,
                    };
                }
            }
            catch (JsonException e)
            {
                throw new LlmException("Nieczytelna odpowiedź serwera modelu: " + Text.Shorten(json, 200), e);
            }
        }

        private static TimeSpan ReadDuration(JsonElement root, string name)
            => root.TryGetProperty(name, out var value) && value.TryGetInt64(out var ns) ? TimeSpan.FromTicks(ns / 100) : TimeSpan.Zero;

        private static int ReadInt(JsonElement root, string name)
            => root.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : 0;
    }
}
