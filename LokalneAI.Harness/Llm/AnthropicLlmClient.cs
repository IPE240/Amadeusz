using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LokalneAI.Harness.Llm
{
    /// <summary>Ustawienia klienta Claude (API Anthropic). Klucz API zakłada się na <c>console.anthropic.com</c>.</summary>
    public sealed class AnthropicOptions
    {
        /// <summary>Adres API.</summary>
        public string Url { get; set; } = "https://api.anthropic.com";

        /// <summary>Klucz API (nagłówek <c>x-api-key</c>). Nie zapisuj go w repozytorium ani w plikach programu.</summary>
        public string ApiKey { get; set; }

        /// <summary>Id modelu, np. <c>claude-sonnet-5-5</c> (lista: <see cref="CloudModels.ListAsync"/>).</summary>
        public string Model { get; set; } = "";

        /// <summary>Długość kontekstu do licznika kontekstu.</summary>
        public int ContextLength { get; set; } = 200000;

        /// <summary>Losowość odpowiedzi. Do poleceń i SQL 0,1.</summary>
        public double Temperature { get; set; } = 0.1;

        /// <summary>Wysyłać temperaturę; po odrzuceniu przez model klient sam ponawia bez niej.</summary>
        public bool SendTemperature { get; set; } = true;

        /// <summary>Limit tokenów jednej odpowiedzi (pole <c>max_tokens</c>, wymagane przez API).</summary>
        public int MaxOutputTokens { get; set; } = 8192;

        /// <summary>Limit na jedną odpowiedź modelu.</summary>
        public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromMinutes(5);
    }

    /// <summary>
    /// Klient Claude: <c>POST /v1/messages</c>. Krok w JSON-ie wymusza narzędziem <c>krok</c> (<c>tool_choice</c>),
    /// polecenie systemowe i rozmowa idą z pamięcią podręczną promptu (taniej w kolejnych krokach tej samej sesji).
    /// </summary>
    public sealed class AnthropicLlmClient : ILlmClient, ICloudAwareLlmClient, IDisposable
    {
        /// <summary>Wersja API w nagłówku <c>anthropic-version</c>.</summary>
        public const string ApiVersion = "2023-06-01";

        internal const string StepTool = "krok";
        internal const string StepField = "dane";

        private readonly HttpClient _http;
        private readonly AnthropicOptions _options;
        private readonly string _baseUrl;
        private volatile bool _noTemperature;

        /// <summary>Tworzy klienta. Wystarczy jeden na sesję. <c>handler</c> podmienia HTTP w testach; null = zwykłe połączenie.</summary>
        public AnthropicLlmClient(AnthropicOptions options, HttpMessageHandler handler = null)
        {
            _options = options ?? new AnthropicOptions();
            _baseUrl = (_options.Url ?? "").Trim().TrimEnd('/');
            if (_baseUrl.Length == 0)
            {
                _baseUrl = "https://api.anthropic.com";
            }

            if (_baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
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
        public string CloudProviderId => CloudProvider.Anthropic.Id;

        /// <inheritdoc/>
        public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    return await SendAsync(request, cancellationToken).ConfigureAwait(false);
                }
                catch (TemperatureRejectedException) when (attempt == 0)
                {
                    _noTemperature = true;
                }
                catch (OverloadedException) when (attempt == 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
                }
                catch (OverloadedException e)
                {
                    throw new LlmException(e.Message);
                }
                catch (TemperatureRejectedException)
                {
                    throw new LlmException("Anthropic: model odrzucił temperaturę także po ponowieniu.");
                }
            }
        }

        /// <summary>Zamyka połączenie HTTP.</summary>
        public void Dispose() => _http.Dispose();

        private async Task<LlmResponse> SendAsync(LlmRequest request, CancellationToken cancellationToken)
        {
            var sentTemperature = !_noTemperature;
            using (var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            using (var message = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/v1/messages"))
            {
                limit.CancelAfter(_options.RequestTimeout);
                message.Content = new StringContent(BuildBody(request), Encoding.UTF8, "application/json");
                message.Headers.Add("x-api-key", _options.ApiKey ?? "");
                message.Headers.Add("anthropic-version", ApiVersion);

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
                    throw new LlmException("Claude nie odpowiedział w wyznaczonym czasie.", e);
                }

                using (response)
                {
                    var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var status = (int)response.StatusCode;
                    if (!response.IsSuccessStatusCode)
                    {
                        if (sentTemperature && CloudHttp.RejectsTemperature(status, text))
                        {
                            throw new TemperatureRejectedException();
                        }

                        var description = CloudHttp.DescribeError("Anthropic", status, text);
                        if (status == 529 || status == 503)
                        {
                            throw new OverloadedException(description);
                        }

                        throw new LlmException(description);
                    }

                    return Parse(text, clock.Elapsed, !string.IsNullOrEmpty(request.JsonSchema));
                }
            }
        }

        /// <summary>Treść zapytania: <c>system</c> osobno, role user/assistant na przemian, narzędzie <c>krok</c> przy schemacie.</summary>
        internal string BuildBody(LlmRequest request)
        {
            var system = new StringBuilder();
            var turns = new List<KeyValuePair<string, StringBuilder>>();
            foreach (var m in request.Messages ?? new LlmMessage[0])
            {
                var content = m.Content ?? "";
                if (m.Role == "system" && turns.Count == 0)
                {
                    if (system.Length > 0)
                    {
                        system.Append("\n\n");
                    }

                    system.Append(content);
                    continue;
                }

                // Późniejsze wiadomości systemowe i wyniki narzędzi („tool”) idą jako wiadomość użytkownika.
                var role = m.Role == "assistant" ? "assistant" : "user";
                if (m.Role == "system")
                {
                    content = "[Uwaga programu] " + content;
                }

                if (turns.Count > 0 && turns[turns.Count - 1].Key == role)
                {
                    turns[turns.Count - 1].Value.Append("\n\n").Append(content);
                }
                else
                {
                    turns.Add(new KeyValuePair<string, StringBuilder>(role, new StringBuilder(content)));
                }
            }

            if (turns.Count == 0 || turns[0].Key != "user")
            {
                turns.Insert(0, new KeyValuePair<string, StringBuilder>("user", new StringBuilder("(początek rozmowy)")));
            }

            if (turns[turns.Count - 1].Key != "user")
            {
                turns.Add(new KeyValuePair<string, StringBuilder>("user", new StringBuilder("Kontynuuj.")));
            }

            using (var stream = new MemoryStream())
            {
                using (var writer = new Utf8JsonWriter(stream, Text.CompactWriter))
                {
                    writer.WriteStartObject();
                    writer.WriteString("model", _options.Model);
                    writer.WriteNumber("max_tokens", _options.MaxOutputTokens > 0 ? _options.MaxOutputTokens : 8192);
                    if (!_noTemperature)
                    {
                        writer.WriteNumber("temperature", request.Temperature ?? _options.Temperature);
                    }

                    if (system.Length > 0)
                    {
                        writer.WriteStartArray("system");
                        WriteCachedText(writer, system.ToString());
                        writer.WriteEndArray();
                    }

                    writer.WriteStartArray("messages");
                    for (var i = 0; i < turns.Count; i++)
                    {
                        writer.WriteStartObject();
                        writer.WriteString("role", turns[i].Key);
                        if (i == turns.Count - 1)
                        {
                            // Punkt pamięci podręcznej na końcu rozmowy: następny krok tej sesji czyta wszystko wcześniejsze taniej.
                            writer.WriteStartArray("content");
                            WriteCachedText(writer, turns[i].Value.ToString());
                            writer.WriteEndArray();
                        }
                        else
                        {
                            writer.WriteString("content", turns[i].Value.ToString());
                        }

                        writer.WriteEndObject();
                    }

                    writer.WriteEndArray();
                    if (!string.IsNullOrEmpty(request.JsonSchema))
                    {
                        // API nie przyjmuje anyOf na samej górze input_schema — schemat kroku idzie jako pole „dane”.
                        writer.WriteStartArray("tools");
                        writer.WriteStartObject();
                        writer.WriteString("name", StepTool);
                        writer.WriteString("description", "Jedyny sposób odpowiedzi: następny krok asystenta (wywołania poleceń, odpowiedź albo pytanie) w polu „dane”.");
                        writer.WriteStartObject("input_schema");
                        writer.WriteString("type", "object");
                        writer.WriteStartObject("properties");
                        writer.WritePropertyName(StepField);
                        using (var schema = JsonDocument.Parse(request.JsonSchema))
                        {
                            schema.RootElement.WriteTo(writer);
                        }

                        writer.WriteEndObject();
                        writer.WriteStartArray("required");
                        writer.WriteStringValue(StepField);
                        writer.WriteEndArray();
                        writer.WriteEndObject();
                        writer.WriteEndObject();
                        writer.WriteEndArray();
                        writer.WriteStartObject("tool_choice");
                        writer.WriteString("type", "tool");
                        writer.WriteString("name", StepTool);
                        writer.WriteEndObject();
                    }

                    writer.WriteEndObject();
                }

                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        internal static LlmResponse Parse(string json, TimeSpan elapsed, bool expectStep)
        {
            try
            {
                using (var document = JsonDocument.Parse(json))
                {
                    var root = document.RootElement;
                    var text = new StringBuilder();
                    string step = null;
                    if (root.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var block in content.EnumerateArray())
                        {
                            var type = block.TryGetProperty("type", out var t) ? t.GetString() : null;
                            if (type == "text" && block.TryGetProperty("text", out var piece))
                            {
                                text.Append(piece.GetString());
                            }
                            else if (type == "tool_use" && block.TryGetProperty("input", out var input) && step == null)
                            {
                                step = UnwrapStep(input);
                            }
                        }
                    }

                    var stop = root.TryGetProperty("stop_reason", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
                    if (expectStep && step == null && stop == "max_tokens")
                    {
                        throw new LlmException("Claude: odpowiedź przekroczyła limit tokenów, zanim skończył krok.");
                    }

                    var usage = root.TryGetProperty("usage", out var u) && u.ValueKind == JsonValueKind.Object ? u : default;
                    return new LlmResponse
                    {
                        Content = step ?? (expectStep ? CloudHttp.ExtractJson(text.ToString()) : text.ToString()),
                        PromptTokens = Int(usage, "input_tokens") + Int(usage, "cache_creation_input_tokens") + Int(usage, "cache_read_input_tokens"),
                        OutputTokens = Int(usage, "output_tokens"),
                        Duration = elapsed,
                        Model = root.TryGetProperty("model", out var model) ? model.GetString() : null,
                    };
                }
            }
            catch (JsonException e)
            {
                throw new LlmException("Nieczytelna odpowiedź Anthropic: " + Text.Shorten(json, 200), e);
            }
        }

        /// <summary>Krok z narzędzia: pole „dane” (obiekt albo tekst z JSON-em); bez pola — całe wejście narzędzia.</summary>
        private static string UnwrapStep(JsonElement input)
        {
            if (input.ValueKind == JsonValueKind.Object && input.TryGetProperty(StepField, out var data))
            {
                return data.ValueKind == JsonValueKind.String ? CloudHttp.ExtractJson(data.GetString()) : data.GetRawText();
            }

            return input.GetRawText();
        }

        private static int Int(JsonElement usage, string name)
            => usage.ValueKind == JsonValueKind.Object && usage.TryGetProperty(name, out var v) && v.TryGetInt32(out var i) ? i : 0;

        private static void WriteCachedText(Utf8JsonWriter writer, string text)
        {
            writer.WriteStartObject();
            writer.WriteString("type", "text");
            writer.WriteString("text", text);
            writer.WriteStartObject("cache_control");
            writer.WriteString("type", "ephemeral");
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        private sealed class OverloadedException : Exception
        {
            public OverloadedException(string message)
                : base(message)
            {
            }
        }
    }
}
