using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace LokalneAI.Harness.Llm
{
    /// <summary>
    /// Dostawca modeli w chmurze dopuszczony w biurze: Anthropic (Claude), OpenAI (GPT), Google (Gemini).
    /// Lista jest zamknięta: innych chmur (np. DeepSeek, OpenRouter, chmura Ollamy) nie da się użyć,
    /// a <see cref="Session.HarnessSession"/> odmawia pracy z modelem spoza listy albo niedozwolonym w profilu.
    /// </summary>
    public sealed class CloudProvider
    {
        /// <summary>Id dla modeli w chmurze spoza listy (serwer w internecie, którego biuro nie dopuszcza).</summary>
        public const string OutsideListId = "spoza-listy";

        /// <summary>Id dla modeli „w chmurze” Ollamy (nazwa z <c>-cloud</c>): liczy serwer ollama.com, nie ten komputer.</summary>
        public const string OllamaCloudId = "ollama-cloud";

        /// <summary>Anthropic: modele Claude.</summary>
        public static readonly CloudProvider Anthropic = new CloudProvider(
            "anthropic", "Anthropic", "Claude", "https://api.anthropic.com", "#D97757", "ANTHROPIC_API_KEY",
            "https://console.anthropic.com/settings/keys", "api.anthropic.com");

        /// <summary>OpenAI: modele GPT.</summary>
        public static readonly CloudProvider OpenAI = new CloudProvider(
            "openai", "OpenAI", "GPT", "https://api.openai.com", "#0F9D96", "OPENAI_API_KEY",
            "https://platform.openai.com/api-keys", "api.openai.com");

        /// <summary>Google: modele Gemini (przez API zgodne z OpenAI).</summary>
        public static readonly CloudProvider Google = new CloudProvider(
            "google", "Google", "Gemini", "https://generativelanguage.googleapis.com/v1beta/openai", "#1A73E8", "GEMINI_API_KEY",
            "https://aistudio.google.com/apikey", "generativelanguage.googleapis.com");

        private readonly string _host;

        private CloudProvider(string id, string name, string family, string apiUrl, string color, string keyVariable, string keysPage, string host)
        {
            Id = id;
            Name = name;
            Family = family;
            ApiUrl = apiUrl;
            Color = color;
            KeyVariable = keyVariable;
            KeysPage = keysPage;
            _host = host;
        }

        /// <summary>Wszyscy dopuszczeni dostawcy.</summary>
        public static IReadOnlyList<CloudProvider> All { get; } = new[] { Anthropic, OpenAI, Google };

        /// <summary>Id do ustawień i <c>profil.json</c> (pole <c>chmura</c>): <c>anthropic</c>, <c>openai</c>, <c>google</c>.</summary>
        public string Id { get; }

        /// <summary>Nazwa firmy, np. „Anthropic”.</summary>
        public string Name { get; }

        /// <summary>Rodzina modeli, np. „Claude”.</summary>
        public string Family { get; }

        /// <summary>Adres API.</summary>
        public string ApiUrl { get; }

        /// <summary>Kolor na liście modeli (#RRGGBB): Claude pomarańczowy, GPT turkusowy, Gemini niebieski.</summary>
        public string Color { get; }

        /// <summary>Zmienna środowiskowa z kluczem API, gdy klucza nie wpisano w programie.</summary>
        public string KeyVariable { get; }

        /// <summary>Strona, na której zakłada się klucz API.</summary>
        public string KeysPage { get; }

        /// <summary>Dostawca o podanym id; null = nie ma go na liście (np. <c>deepseek</c>).</summary>
        public static CloudProvider Find(string id)
            => All.FirstOrDefault(p => string.Equals(p.Id, (id ?? "").Trim(), StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Kto naprawdę liczy pod tym adresem: null = sieć biura (ten komputer, inny PC, serwer w podsieci),
        /// id dostawcy z listy albo <see cref="OutsideListId"/> dla innego serwera w internecie.
        /// Sieć biura to: adres lokalny albo prywatny, nazwa komputera bez kropek, nazwa kończąca się na .local, .lan albo .internal.
        /// </summary>
        public static string ClassifyUrl(string url)
        {
            var text = (url ?? "").Trim();
            if (text.Length == 0)
            {
                return null;
            }

            if (text.IndexOf("://", StringComparison.Ordinal) < 0)
            {
                text = "http://" + text;
            }

            if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
            {
                return null;
            }

            var host = uri.IdnHost.TrimEnd('.').ToLowerInvariant();
            var known = All.FirstOrDefault(p => host == p._host);
            if (known != null)
            {
                return known.Id;
            }

            if (uri.IsLoopback || host.IndexOf('.') < 0 && uri.HostNameType == UriHostNameType.Dns)
            {
                return null;
            }

            if (IPAddress.TryParse(host.Trim('[', ']'), out var address))
            {
                return IsPrivate(address) ? null : OutsideListId;
            }

            foreach (var suffix in new[] { ".local", ".lan", ".internal", ".localdomain", ".home.arpa" })
            {
                if (host.EndsWith(suffix, StringComparison.Ordinal))
                {
                    return null;
                }
            }

            return OutsideListId;
        }

        /// <summary>Model Ollamy liczony na serwerach ollama.com (tag <c>cloud</c> albo kończący się na <c>-cloud</c>), np. <c>deepseek-v3.1:671b-cloud</c>.</summary>
        public static bool IsOllamaCloudModel(string model)
        {
            var name = (model ?? "").Trim().ToLowerInvariant();
            var colon = name.LastIndexOf(':');
            var tag = colon >= 0 ? name.Substring(colon + 1) : "";
            return tag == "cloud" || tag.EndsWith("-cloud", StringComparison.Ordinal) || name.EndsWith("-cloud", StringComparison.Ordinal);
        }

        /// <summary>Opis dozwolonych modeli dla profilu, np. „modele lokalne i Claude (Anthropic)”.</summary>
        public static string DescribeAllowed(IEnumerable<string> allowedIds)
        {
            var names = (allowedIds ?? Enumerable.Empty<string>()).Select(Find).Where(p => p != null).Distinct()
                .Select(p => p.Family + " (" + p.Name + ")").ToList();
            return names.Count == 0 ? "tylko modele lokalne" : "modele lokalne i " + string.Join(", ", names);
        }

        /// <inheritdoc/>
        public override string ToString() => Name;

        private static bool IsPrivate(IPAddress address)
        {
            if (address.IsIPv4MappedToIPv6)
            {
                address = address.MapToIPv4();
            }

            if (IPAddress.IsLoopback(address))
            {
                return true;
            }

            if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                var bytes6 = address.GetAddressBytes();
                // fe80::/10 (łącze lokalne) i fc00::/7 (adresy prywatne)
                return address.IsIPv6LinkLocal || (bytes6[0] & 0xFE) == 0xFC;
            }

            var b = address.GetAddressBytes();
            return b[0] == 10
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 169 && b[1] == 254);
        }
    }

    /// <summary>
    /// Klient modelu, który wie, kto naprawdę liczy. <see cref="Session.HarnessSession"/> sprawdza to z polem <c>chmura</c> profilu.
    /// Klient bez tego interfejsu traktowany jest jak model lokalny.
    /// </summary>
    public interface ICloudAwareLlmClient
    {
        /// <summary>null = sieć biura; id z <see cref="CloudProvider"/>, <see cref="CloudProvider.OutsideListId"/> albo <see cref="CloudProvider.OllamaCloudId"/>.</summary>
        string CloudProviderId { get; }
    }
}
