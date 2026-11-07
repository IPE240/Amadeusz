using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LokalneAI.Harness.Llm
{
    /// <summary>Ustawienia serwera zgodnego z API OpenAI (llama.cpp <c>llama-server</c>, LM Studio, vLLM, Ollama <c>/v1</c>).</summary>
    public sealed class OpenAiCompatibleOptions
    {
        /// <summary>Adres bazowy bez <c>/v1</c>, np. <c>http://serwer-ai:8080</c>.</summary>
        public string Url { get; set; } = "http://127.0.0.1:8080";

        /// <summary>Nazwa modelu na serwerze (pole <c>model</c> zapytania).</summary>
        public string Model { get; set; } = "";

        /// <summary>Klucz API, gdy serwer stoi za proxy z uwierzytelnianiem; null = bez klucza.</summary>
        public string ApiKey { get; set; }

        /// <summary>Kontekst ustawiony na serwerze (tu tylko do licznika).</summary>
        public int ContextLength { get; set; } = 8192;

        /// <summary>Losowość odpowiedzi. Do poleceń i SQL 0,1.</summary>
        public double Temperature { get; set; } = 0.1;

        /// <summary>Limit na jedną odpowiedź modelu.</summary>
        public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromMinutes(5);
    }

    /// <summary>Klient <c>POST /v1/chat/completions</c> z <c>response_format</c> = JSON Schema.</summary>
    public sealed class OpenAiCompatibleLlmClient : ILlmClient, IDisposable
    {
        private readonly HttpClient _http;
        private readonly OpenAiCompatibleOptions _options;
        private readonly string _baseUrl;

        /// <summary>Tworzy klienta. Wystarczy jeden na cały program. <c>handler</c> podmienia HTTP w testach; null = zwykłe połączenie.</summary>
        public OpenAiCompatibleLlmClient(OpenAiCompatibleOptions options, HttpMessageHandler handler = null)
        {
            _options = options ?? new OpenAiCompatibleOptions();
            _baseUrl = (_options.Url ?? "").Trim().TrimEnd('/');
            if (_baseUrl.Length > 0 && _baseUrl.IndexOf("://", StringComparison.Ordinal) < 0)
            {
                _baseUrl = "http://" + _baseUrl;
            }

            if (_baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            {
                _baseUrl = _baseUrl.Substring(0, _baseUrl.Length - 3);
            }

            _http = handler == null ? new HttpClient() : new HttpClient(handler, false);
            _http.Timeout = Timeout.InfiniteTimeSpan;
        }

        /// <inheritdoc/>
        public string ModelName => _options.Model;

        /// <inheritdoc/>
        public int ContextLength => _options.ContextLength;

        /// <inheritdoc/>
        public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            using (var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            using (var message = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/v1/chat/completions"))
            {
                limit.CancelAfter(_options.RequestTimeout);
                message.Content = new StringContent(BuildBody(request), Encoding.UTF8, "application/json");
                if (!string.IsNullOrEmpty(_options.ApiKey))
                {
                    message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
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
                    throw new LlmException("Model nie odpowiedział w wyznaczonym czasie.", e);
                }

                using (response)
                {
                    var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new LlmException("Serwer modelu zgłosił błąd (" + (int)response.StatusCode + "): " + Text.Shorten(text, 300));
                    }

                    return Parse(text, clock.Elapsed);
                }
            }
        }

        /// <summary>Zamyka połączenie HTTP. Wywołuj przy zamykaniu programu.</summary>
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
                        writer.WriteEndObject();
                    }

                    writer.WriteEndArray();
                    writer.WriteNumber("temperature", request.Temperature ?? _options.Temperature);
                    if (!string.IsNullOrEmpty(request.JsonSchema))
                    {
                        writer.WriteStartObject("response_format");
                        writer.WriteString("type", "json_schema");
                        writer.WriteStartObject("json_schema");
                        writer.WriteString("name", "krok");
                        writer.WriteBoolean("strict", true);
                        writer.WritePropertyName("schema");
                        using (var schema = JsonDocument.Parse(request.JsonSchema))
                        {
                            schema.RootElement.WriteTo(writer);
                        }

                        writer.WriteEndObject();
                        writer.WriteEndObject();
                    }

                    writer.WriteEndObject();
                }

                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        private static LlmResponse Parse(string json, TimeSpan elapsed)
        {
            try
            {
                using (var document = JsonDocument.Parse(json))
                {
                    var root = document.RootElement;
                    var content = "";
                    if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0
                        && choices[0].TryGetProperty("message", out var message) && message.TryGetProperty("content", out var c))
                    {
                        content = c.GetString() ?? "";
                    }

                    var usage = root.TryGetProperty("usage", out var u) ? u : default;
                    return new LlmResponse
                    {
                        Content = content,
                        PromptTokens = usage.ValueKind == JsonValueKind.Object && usage.TryGetProperty("prompt_tokens", out var p) && p.TryGetInt32(out var pt) ? pt : 0,
                        OutputTokens = usage.ValueKind == JsonValueKind.Object && usage.TryGetProperty("completion_tokens", out var o) && o.TryGetInt32(out var ot) ? ot : 0,
                        Duration = elapsed,
                        Model = root.TryGetProperty("model", out var model) ? model.GetString() : null,
                    };
                }
            }
            catch (JsonException e)
            {
                throw new LlmException("Nieczytelna odpowiedź serwera modelu: " + Text.Shorten(json, 200), e);
            }
        }
    }
}
