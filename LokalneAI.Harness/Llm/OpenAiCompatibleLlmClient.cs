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

        /// <summary>Ścieżka czatu doklejana do <see cref="Url"/>; null = <c>/v1/chat/completions</c>. Google: <c>/chat/completions</c>.</summary>
        public string ChatPath { get; set; }

        /// <summary>
        /// Schemat odpowiedzi w poleceniu systemowym zamiast <c>response_format</c> ze <c>strict</c>.
        /// Dla chmury: tryb ścisły OpenAI nie przyjmuje schematu kroku z <c>anyOf</c> na górze; poprawność sprawdza harness.
        /// </summary>
        public bool SchemaInPrompt { get; set; }

        /// <summary>Przy <see cref="SchemaInPrompt"/>: <c>response_format</c> = <c>json_object</c> (OpenAI gwarantuje wtedy poprawny JSON).</summary>
        public bool JsonObjectMode { get; set; }

        /// <summary>Dostawca w chmurze (<see cref="CloudProvider.Id"/>); null = rozpoznaj po adresie (<see cref="CloudProvider.ClassifyUrl"/>).</summary>
        public string CloudProvider { get; set; }

        /// <summary>Nazwa serwera w komunikatach błędów, np. „OpenAI”; null = „Serwer modelu”.</summary>
        public string ServiceName { get; set; }

        /// <summary>
        /// Wysyłać temperaturę. Część modeli w chmurze przyjmuje tylko domyślną — po takim błędzie klient sam ponawia zapytanie
        /// bez temperatury i dalej jej nie wysyła.
        /// </summary>
        public bool SendTemperature { get; set; } = true;
    }

    /// <summary>
    /// Klient <c>POST /v1/chat/completions</c> z <c>response_format</c> = JSON Schema: llama.cpp, LM Studio, vLLM,
    /// a w chmurze OpenAI (GPT) i Google (Gemini) — patrz <see cref="CloudLlm.Create"/>.
    /// </summary>
    public sealed class OpenAiCompatibleLlmClient : ILlmClient, ICloudAwareLlmClient, IDisposable
    {
        private readonly HttpClient _http;
        private readonly OpenAiCompatibleOptions _options;
        private readonly string _baseUrl;
        private volatile bool _noTemperature;

        /// <summary>Tworzy klienta. Wystarczy jeden na cały program. <c>handler</c> podmienia HTTP w testach; null = zwykłe połączenie.</summary>
        public OpenAiCompatibleLlmClient(OpenAiCompatibleOptions options, HttpMessageHandler handler = null)
        {
            _options = options ?? new OpenAiCompatibleOptions();
            _baseUrl = (_options.Url ?? "").Trim().TrimEnd('/');
            if (_baseUrl.Length > 0 && _baseUrl.IndexOf("://", StringComparison.Ordinal) < 0)
            {
                _baseUrl = "http://" + _baseUrl;
            }

            if (_options.ChatPath == null && _baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            {
                _baseUrl = _baseUrl.Substring(0, _baseUrl.Length - 3);
            }

            _noTemperature = !_options.SendTemperature;
            _http = handler == null ? new HttpClient() : new HttpClient(handler, false);
            _http.Timeout = Timeout.InfiniteTimeSpan;
        }

        /// <inheritdoc/>
        public string ModelName => _options.Model;

        /// <inheritdoc/>
        public int ContextLength => _options.ContextLength;

        /// <inheritdoc/>
        public string CloudProviderId => string.IsNullOrWhiteSpace(_options.CloudProvider) ? Llm.CloudProvider.ClassifyUrl(_baseUrl) : _options.CloudProvider.Trim().ToLowerInvariant();

        private string ChatUrl => _baseUrl + (string.IsNullOrWhiteSpace(_options.ChatPath) ? "/v1/chat/completions" : "/" + _options.ChatPath.Trim().TrimStart('/'));

        /// <inheritdoc/>
        public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            try
            {
                return await SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (TemperatureRejectedException)
            {
                _noTemperature = true;
                return await SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task<LlmResponse> SendAsync(LlmRequest request, CancellationToken cancellationToken)
        {
            var sentTemperature = !_noTemperature;
            using (var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            using (var message = new HttpRequestMessage(HttpMethod.Post, ChatUrl))
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
                        var status = (int)response.StatusCode;
                        if (sentTemperature && CloudHttp.RejectsTemperature(status, text))
                        {
                            throw new TemperatureRejectedException();
                        }

                        throw new LlmException(_options.ServiceName == null
                            ? "Serwer modelu zgłosił błąd (" + status + "): " + Text.Shorten(text, 300)
                            : CloudHttp.DescribeError(_options.ServiceName, status, text));
                    }

                    var parsed = Parse(text, clock.Elapsed);
                    if (_options.SchemaInPrompt && !string.IsNullOrEmpty(request.JsonSchema))
                    {
                        parsed.Content = CloudHttp.ExtractJson(parsed.Content);
                    }

                    return parsed;
                }
            }
        }

        /// <summary>Zamyka połączenie HTTP. Wywołuj przy zamykaniu programu.</summary>
        public void Dispose() => _http.Dispose();

        internal string BuildBody(LlmRequest request)
        {
            var schemaInPrompt = _options.SchemaInPrompt && !string.IsNullOrEmpty(request.JsonSchema);
            using (var stream = new MemoryStream())
            {
                using (var writer = new Utf8JsonWriter(stream, Text.CompactWriter))
                {
                    writer.WriteStartObject();
                    writer.WriteString("model", _options.Model);
                    writer.WriteStartArray("messages");
                    var instructionAdded = false;
                    foreach (var m in request.Messages)
                    {
                        var content = m.Content;
                        if (schemaInPrompt && !instructionAdded && m.Role == "system")
                        {
                            content += CloudHttp.SchemaInstruction(request.JsonSchema);
                            instructionAdded = true;
                        }

                        writer.WriteStartObject();
                        writer.WriteString("role", m.Role);
                        if (m.Images == null)
                        {
                            writer.WriteString("content", content);
                        }
                        else
                        {
                            // Obrazy jako części treści (image_url z adresem data:) — tak przyjmują je GPT i Gemini.
                            writer.WriteStartArray("content");
                            writer.WriteStartObject();
                            writer.WriteString("type", "text");
                            writer.WriteString("text", content);
                            writer.WriteEndObject();
                            foreach (var image in m.Images)
                            {
                                writer.WriteStartObject();
                                writer.WriteString("type", "image_url");
                                writer.WriteStartObject("image_url");
                                writer.WriteString("url", "data:" + LlmMessage.MediaType(image) + ";base64," + Convert.ToBase64String(image));
                                writer.WriteEndObject();
                                writer.WriteEndObject();
                            }

                            writer.WriteEndArray();
                        }

                        writer.WriteEndObject();
                    }

                    if (schemaInPrompt && !instructionAdded)
                    {
                        writer.WriteStartObject();
                        writer.WriteString("role", "system");
                        writer.WriteString("content", CloudHttp.SchemaInstruction(request.JsonSchema).Trim());
                        writer.WriteEndObject();
                    }

                    writer.WriteEndArray();
                    if (!_noTemperature)
                    {
                        writer.WriteNumber("temperature", request.Temperature ?? _options.Temperature);
                    }

                    if (schemaInPrompt)
                    {
                        if (_options.JsonObjectMode)
                        {
                            writer.WriteStartObject("response_format");
                            writer.WriteString("type", "json_object");
                            writer.WriteEndObject();
                        }
                    }
                    else if (!string.IsNullOrEmpty(request.JsonSchema))
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

    /// <summary>Serwer nie przyjął temperatury — klient ponawia zapytanie bez niej.</summary>
    internal sealed class TemperatureRejectedException : Exception
    {
    }
}
