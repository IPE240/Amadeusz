using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LokalneAI.Harness.Llm
{
    /// <summary>Model dostępny u dostawcy w chmurze.</summary>
    public sealed class CloudModelInfo
    {
        /// <summary>Tworzy opis modelu.</summary>
        public CloudModelInfo(string provider, string id, string displayName)
        {
            Provider = provider;
            Id = id;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? id : displayName;
        }

        /// <summary>Id dostawcy (<see cref="CloudProvider.Id"/>).</summary>
        public string Provider { get; }

        /// <summary>Id modelu do zapytań, np. <c>claude-sonnet-5-5</c>.</summary>
        public string Id { get; }

        /// <summary>Nazwa do okna (Anthropic podaje ładną nazwę; inni — id).</summary>
        public string DisplayName { get; }

        /// <inheritdoc/>
        public override string ToString() => DisplayName;
    }

    /// <summary>Lista modeli dostawcy (<c>GET /v1/models</c>) — sprawdza też klucz API. Nic nie kosztuje.</summary>
    public static class CloudModels
    {
        /// <summary>
        /// Pobiera modele, którymi da się rozmawiać (bez modeli do obrazów, mowy i wektorów). Zły klucz albo brak sieci:
        /// <see cref="LlmException"/> z opisem po polsku.
        /// </summary>
        public static async Task<IReadOnlyList<CloudModelInfo>> ListAsync(
            CloudProvider provider, string apiKey, HttpMessageHandler handler = null, CancellationToken cancellationToken = default)
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new LlmException(provider.Name + ": brak klucza API.");
            }

            using (var http = handler == null ? new HttpClient() : new HttpClient(handler, false))
            {
                http.Timeout = TimeSpan.FromSeconds(30);
                var url = provider == CloudProvider.Anthropic ? provider.ApiUrl + "/v1/models?limit=1000"
                    : provider == CloudProvider.Google ? provider.ApiUrl + "/models"
                    : provider.ApiUrl + "/v1/models";
                using (var message = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    if (provider == CloudProvider.Anthropic)
                    {
                        message.Headers.Add("x-api-key", apiKey.Trim());
                        message.Headers.Add("anthropic-version", AnthropicLlmClient.ApiVersion);
                    }
                    else
                    {
                        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
                    }

                    HttpResponseMessage response;
                    try
                    {
                        response = await http.SendAsync(message, cancellationToken).ConfigureAwait(false);
                    }
                    catch (HttpRequestException e)
                    {
                        throw new LlmUnavailableException(provider.ApiUrl, e);
                    }
                    catch (OperationCanceledException e) when (!cancellationToken.IsCancellationRequested)
                    {
                        throw new LlmException(provider.Name + " nie odpowiedział w 30 s.", e);
                    }

                    using (response)
                    {
                        var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (!response.IsSuccessStatusCode)
                        {
                            throw new LlmException(CloudHttp.DescribeError(provider.Name, (int)response.StatusCode, text));
                        }

                        return Parse(provider, text);
                    }
                }
            }
        }

        /// <summary>Lista z odpowiedzi <c>{"data":[{"id":…}]}</c>; bez modeli, którymi się nie rozmawia.</summary>
        internal static IReadOnlyList<CloudModelInfo> Parse(CloudProvider provider, string json)
        {
            var models = new List<KeyValuePair<CloudModelInfo, long>>();
            try
            {
                using (var document = JsonDocument.Parse(json))
                {
                    if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                    {
                        return new CloudModelInfo[0];
                    }

                    foreach (var item in data.EnumerateArray())
                    {
                        var id = item.TryGetProperty("id", out var i) && i.ValueKind == JsonValueKind.String ? i.GetString() : null;
                        if (string.IsNullOrWhiteSpace(id))
                        {
                            continue;
                        }

                        if (id.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
                        {
                            id = id.Substring("models/".Length);
                        }

                        if (!IsChatModel(provider, id))
                        {
                            continue;
                        }

                        var name = item.TryGetProperty("display_name", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
                        var created = item.TryGetProperty("created", out var c) && c.TryGetInt64(out var seconds) ? seconds
                            : item.TryGetProperty("created_at", out var ca) && ca.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(ca.GetString(), out var at) ? at.ToUnixTimeSeconds()
                            : 0;
                        models.Add(new KeyValuePair<CloudModelInfo, long>(new CloudModelInfo(provider.Id, id, name), created));
                    }
                }
            }
            catch (JsonException e)
            {
                throw new LlmException(provider.Name + ": nieczytelna lista modeli.", e);
            }

            // Najnowsze na górze; bez daty (Google) — alfabetycznie od końca, więc wyższe wersje wyżej.
            return models
                .GroupBy(m => m.Key.Id, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
                .OrderByDescending(m => m.Value).ThenByDescending(m => m.Key.Id, StringComparer.OrdinalIgnoreCase)
                .Select(m => m.Key).ToList();
        }

        /// <summary>Czy to model do rozmowy (a nie do obrazów, mowy, wektorów albo tylko przez inne API).</summary>
        internal static bool IsChatModel(CloudProvider provider, string id)
        {
            var name = id.ToLowerInvariant();
            if (provider == CloudProvider.Anthropic)
            {
                return name.StartsWith("claude", StringComparison.Ordinal);
            }

            string[] skip =
            {
                "embedding", "embed", "tts", "audio", "realtime", "transcribe", "whisper", "dall-e", "image", "imagen", "veo",
                "moderation", "search", "computer-use", "deep-research", "codex", "aqa", "learnlm", "live", "native-audio",
            };
            if (skip.Any(s => name.Contains(s)))
            {
                return false;
            }

            if (provider == CloudProvider.Google)
            {
                return name.StartsWith("gemini", StringComparison.Ordinal) || name.StartsWith("gemma", StringComparison.Ordinal);
            }

            // OpenAI: modele GPT i rozumujące „o…”; wersje „-pro” działają tylko przez inne API (Responses).
            var family = name.StartsWith("gpt-", StringComparison.Ordinal) || name.StartsWith("chatgpt-", StringComparison.Ordinal)
                || (name.Length > 1 && name[0] == 'o' && char.IsDigit(name[1]));
            return family && !name.EndsWith("-pro", StringComparison.Ordinal) && !name.Contains("-pro-") && !name.Contains("instruct");
        }
    }

    /// <summary>Tworzy klienta modelu w chmurze z ustawieniami dobranymi do dostawcy.</summary>
    public static class CloudLlm
    {
        /// <summary>
        /// Klient dla dostawcy: Claude → <see cref="AnthropicLlmClient"/>; GPT i Gemini → <see cref="OpenAiCompatibleLlmClient"/>
        /// ze schematem kroku w poleceniu. <paramref name="contextLength"/> 0 = domyślny dla dostawcy.
        /// </summary>
        public static ILlmClient Create(
            CloudProvider provider, string apiKey, string model, double temperature = 0.1, int contextLength = 0, HttpMessageHandler handler = null)
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new LlmException(provider.Name + ": brak klucza API. Wpisz go w Ustawieniach → Modele w chmurze.");
            }

            if (provider == CloudProvider.Anthropic)
            {
                return new AnthropicLlmClient(new AnthropicOptions
                {
                    ApiKey = apiKey.Trim(),
                    Model = model,
                    Temperature = temperature,
                    ContextLength = contextLength > 0 ? contextLength : 200000,
                }, handler);
            }

            return new OpenAiCompatibleLlmClient(new OpenAiCompatibleOptions
            {
                Url = provider.ApiUrl,
                ChatPath = provider == CloudProvider.Google ? "/chat/completions" : null,
                ApiKey = apiKey.Trim(),
                Model = model,
                Temperature = temperature,
                ContextLength = contextLength > 0 ? contextLength : 128000,
                SchemaInPrompt = true,
                JsonObjectMode = provider == CloudProvider.OpenAI,
                CloudProvider = provider.Id,
                ServiceName = provider.Name,
            }, handler);
        }
    }
}
