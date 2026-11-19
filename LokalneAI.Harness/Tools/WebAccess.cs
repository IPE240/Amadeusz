using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LokalneAI.Harness.Llm;

namespace LokalneAI.Harness.Tools
{
    /// <summary>Ustawienia dostępu do internetu (narzędzia <c>szukaj_w_internecie</c> i <c>czytaj_strone</c>).</summary>
    public sealed class WebAccessOptions
    {
        /// <summary>Wyszukiwarka: DuckDuckGo w wersji „lite” (bez klucza, bez kosztów). Fraza jest doklejana na końcu.</summary>
        public string SearchUrl { get; set; } = "https://lite.duckduckgo.com/lite/?q=";

        /// <summary>Ile wyników wyszukiwania trafia do modelu.</summary>
        public int MaxResults { get; set; } = 6;

        /// <summary>Ile znaków strony najwyżej trafia do modelu za jednym razem (dalej: <c>od_znaku</c>).</summary>
        public int MaxPageChars { get; set; } = 6000;

        /// <summary>Największa pobierana strona (bajty).</summary>
        public long MaxDownloadBytes { get; set; } = 3 * 1024 * 1024;

        /// <summary>Po tylu sekundach strona, która nie odpowiada, daje błąd.</summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(20);

        /// <summary>Jak program przedstawia się stronom.</summary>
        public string UserAgent { get; set; } = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) LokalneAI/0.3";

        /// <summary>Do testów: własny transport HTTP. Null = zwykłe połączenie, przekierowania sprawdzane ręcznie.</summary>
        public HttpMessageHandler Handler { get; set; }

        /// <summary>Do testów: zamiana nazwy serwera na adresy IP. Null = DNS.</summary>
        public Func<string, Task<IPAddress[]>> Resolve { get; set; }
    }

    /// <summary>
    /// Dostęp modelu do internetu: wyszukiwanie (DuckDuckGo) i czytanie stron jako tekstu. Adresy w sieci biura i na tym
    /// komputerze są zablokowane (także po przekierowaniu i gdy nazwa serwera wskazuje adres prywatny), żeby model nie sięgnął
    /// przez „internet” do bazy, dysku budowy ani bramy.
    /// </summary>
    public sealed class WebAccess : IDisposable
    {
        private const int MaxRedirects = 5;
        private readonly WebAccessOptions _options;
        private readonly HttpClient _http;

        /// <summary>Tworzy dostęp; bez ustawień — DuckDuckGo i domyślne limity.</summary>
        public WebAccess(WebAccessOptions options = null)
        {
            _options = options ?? new WebAccessOptions();
            var handler = _options.Handler ?? new HttpClientHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate };
            _http = new HttpClient(handler, _options.Handler == null) { Timeout = _options.Timeout };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(_options.UserAgent);
            _http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("pl,en;q=0.7");
        }

        /// <summary>Wyniki wyszukiwania jako tekst dla modelu: tytuł, adres, opis.</summary>
        public async Task<ToolResult> SearchAsync(string phrase, CancellationToken cancellationToken = default(CancellationToken))
        {
            phrase = (phrase ?? "").Trim();
            if (phrase.Length == 0)
            {
                return ToolResult.Error("BŁĄD: pusta fraza.");
            }

            var page = await GetAsync(_options.SearchUrl + Uri.EscapeDataString(phrase), cancellationToken).ConfigureAwait(false);
            if (page.Error != null)
            {
                return ToolResult.Error(page.Error);
            }

            var results = ParseDuckDuckGoLite(page.Body).Take(_options.MaxResults).ToList();
            if (results.Count == 0)
            {
                return ToolResult.Ok("Nic nie znaleziono dla: " + phrase + ". To jeszcze nie znaczy, że tego nie ma: szukaj dalej innymi słowami "
                    + "(synonimy, po angielsku, krótsza fraza, nazwa producenta, normy albo programu).");
            }

            var text = new StringBuilder("Wyniki dla „" + phrase + "”:");
            for (var i = 0; i < results.Count; i++)
            {
                text.Append("\n").Append(i + 1).Append(". ").Append(results[i].Title)
                    .Append("\n   ").Append(results[i].Url);
                if (results[i].Snippet.Length > 0)
                {
                    text.Append("\n   ").Append(results[i].Snippet);
                }
            }

            return ToolResult.Ok(text.Append("\nTreść strony: czytaj_strone z adresem.").ToString());
        }

        /// <summary>Strona jako tekst (bez skryptów i znaczników), od znaku <paramref name="from"/>.</summary>
        public async Task<ToolResult> FetchAsync(string address, int from = 0, CancellationToken cancellationToken = default(CancellationToken))
        {
            address = (address ?? "").Trim();
            if (address.Length > 0 && address.IndexOf("://", StringComparison.Ordinal) < 0)
            {
                address = "https://" + address;
            }

            var page = await GetAsync(address, cancellationToken).ConfigureAwait(false);
            if (page.Error != null)
            {
                return ToolResult.Error(page.Error);
            }

            var media = page.MediaType ?? "";
            string title = null;
            string text;
            if (media.Contains("html") || media.Length == 0 && page.Body.TrimStart().StartsWith("<", StringComparison.Ordinal))
            {
                text = HtmlToText(page.Body, out title);
            }
            else if (media.StartsWith("text/", StringComparison.Ordinal) || media.Contains("json") || media.Contains("xml"))
            {
                text = page.Body;
            }
            else
            {
                return ToolResult.Error("BŁĄD: " + page.Url + " to plik " + media + ", nie strona z tekstem — tego nie przeczytam.");
            }

            from = Math.Max(0, Math.Min(from, text.Length));
            var chunk = text.Substring(from, Math.Min(_options.MaxPageChars, text.Length - from));
            var header = new StringBuilder("Strona: ").Append(string.IsNullOrWhiteSpace(title) ? "(bez tytułu)" : title)
                .Append("\nAdres: ").Append(page.Url)
                .Append("\nZnaki ").Append(from).Append('–').Append(from + chunk.Length).Append(" z ").Append(text.Length).Append("\n\n");
            var more = from + chunk.Length < text.Length ? "\n\n… dalej: czytaj_strone z od_znaku=" + (from + chunk.Length) : "";
            return ToolResult.Ok(header + chunk + more);
        }

        /// <summary>Opis problemu z adresem albo null: tylko http(s) i tylko serwery w internecie (nie sieć biura, nie ten komputer).</summary>
        internal async Task<string> CheckAddressAsync(Uri uri)
        {
            if (uri == null || uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            {
                return "BŁĄD: czytam tylko adresy http:// i https://.";
            }

            if (CloudProvider.ClassifyUrl(uri.ToString()) == null)
            {
                return "BŁĄD: " + uri.Host + " to adres w sieci biura albo na tym komputerze — przez internet tam nie sięgam.";
            }

            IPAddress[] addresses;
            if (IPAddress.TryParse(uri.Host.Trim('[', ']'), out var literal))
            {
                addresses = new[] { literal };
            }
            else
            {
                try
                {
                    addresses = await (_options.Resolve ?? Dns.GetHostAddressesAsync)(uri.Host).ConfigureAwait(false);
                }
                catch (SocketException)
                {
                    return "BŁĄD: nie ma serwera " + uri.Host + ".";
                }
            }

            return addresses.Any(IsPrivate)
                ? "BŁĄD: " + uri.Host + " wskazuje adres w sieci lokalnej — zablokowane."
                : null;
        }

        /// <summary>Adres prywatny, lokalny albo specjalny (nie internet).</summary>
        internal static bool IsPrivate(IPAddress address)
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
                var b6 = address.GetAddressBytes();
                return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || (b6[0] & 0xFE) == 0xFC || address.Equals(IPAddress.IPv6None);
            }

            var b = address.GetAddressBytes();
            return b[0] == 10 || b[0] == 127 || b[0] == 0 || b[0] >= 224
                   || b[0] == 172 && b[1] >= 16 && b[1] <= 31
                   || b[0] == 192 && b[1] == 168
                   || b[0] == 169 && b[1] == 254
                   || b[0] == 100 && b[1] >= 64 && b[1] <= 127;
        }

        private async Task<Page> GetAsync(string address, CancellationToken cancellationToken)
        {
            if (!Uri.TryCreate(address, UriKind.Absolute, out var uri))
            {
                return Page.Fail("BŁĄD: to nie jest adres strony: " + address);
            }

            try
            {
                for (var hop = 0; hop <= MaxRedirects; hop++)
                {
                    var problem = await CheckAddressAsync(uri).ConfigureAwait(false);
                    if (problem != null)
                    {
                        return Page.Fail(problem);
                    }

                    using (var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
                    {
                        var status = (int)response.StatusCode;
                        if (status >= 300 && status < 400 && response.Headers.Location != null)
                        {
                            uri = response.Headers.Location.IsAbsoluteUri ? response.Headers.Location : new Uri(uri, response.Headers.Location);
                            continue;
                        }

                        if (!response.IsSuccessStatusCode)
                        {
                            return Page.Fail("BŁĄD: " + uri.Host + " odpowiedział " + status + " " + response.ReasonPhrase + ".");
                        }

                        if (response.Content.Headers.ContentLength > _options.MaxDownloadBytes)
                        {
                            return Page.Fail("BŁĄD: strona ma ponad " + _options.MaxDownloadBytes / (1024 * 1024) + " MB — za duża.");
                        }

                        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        return new Page { Url = uri.ToString(), Body = body, MediaType = response.Content.Headers.ContentType?.MediaType };
                    }
                }

                return Page.Fail("BŁĄD: za dużo przekierowań.");
            }
            catch (HttpRequestException e)
            {
                return Page.Fail("BŁĄD: nie połączyłem się z " + uri.Host + ": " + (e.InnerException?.Message ?? e.Message));
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return Page.Fail("BŁĄD: " + uri.Host + " nie odpowiedział w " + (int)_options.Timeout.TotalSeconds + " s.");
            }
        }

        /// <summary>Wyniki z DuckDuckGo „lite”: odnośniki <c>result-link</c> i opisy <c>result-snippet</c> po kolei.</summary>
        internal static IEnumerable<SearchResult> ParseDuckDuckGoLite(string html)
        {
            var links = Regex.Matches(html ?? "", "<a[^>]*href=\"([^\"]+)\"[^>]*class=['\"]result-link['\"][^>]*>(.*?)</a>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            var snippets = Regex.Matches(html ?? "", "<td[^>]*class=['\"]result-snippet['\"][^>]*>(.*?)</td>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            for (var i = 0; i < links.Count; i++)
            {
                var href = WebUtility.HtmlDecode(links[i].Groups[1].Value);
                var target = Regex.Match(href, "[?&]uddg=([^&]+)");
                var url = target.Success ? Uri.UnescapeDataString(target.Groups[1].Value) : href.StartsWith("//", StringComparison.Ordinal) ? "https:" + href : href;
                if (url.IndexOf("duckduckgo.com/y.js", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue; // reklama
                }

                yield return new SearchResult
                {
                    Title = Clean(links[i].Groups[2].Value),
                    Url = url,
                    Snippet = i < snippets.Count ? Clean(snippets[i].Groups[1].Value) : "",
                };
            }
        }

        /// <summary>HTML → zwykły tekst: bez skryptów, stylów i znaczników, akapity w osobnych liniach.</summary>
        internal static string HtmlToText(string html, out string title)
        {
            html = html ?? "";
            var t = Regex.Match(html, "<title[^>]*>(.*?)</title>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            title = t.Success ? Clean(t.Groups[1].Value) : null;
            var text = Regex.Replace(html, "<(script|style|noscript|svg|head|template)[^>]*>.*?</\\1>", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            text = Regex.Replace(text, "<!--.*?-->", " ", RegexOptions.Singleline);
            text = Regex.Replace(text, "<(br|/p|/div|/li|/h[1-6]|/tr|/section|/article|/header|/footer|/table)[^>]*>", "\n", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, "<li[^>]*>", "\n- ", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, "<(td|th)[^>]*>", " | ", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, "<[^>]+>", " ");
            text = WebUtility.HtmlDecode(text).Replace(' ', ' ');
            text = Regex.Replace(text, "[ \t\r\f\v]+", " ");
            text = Regex.Replace(text, " *\n *", "\n");
            text = Regex.Replace(text, "\n{3,}", "\n\n");
            return text.Trim();
        }

        private static string Clean(string html)
            => Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", "")), "\\s+", " ").Trim();

        /// <summary>Zamyka połączenia HTTP.</summary>
        public void Dispose() => _http.Dispose();

        internal sealed class SearchResult
        {
            public string Title { get; set; }

            public string Url { get; set; }

            public string Snippet { get; set; }
        }

        private sealed class Page
        {
            public string Url { get; set; }

            public string Body { get; set; }

            public string MediaType { get; set; }

            public string Error { get; set; }

            public static Page Fail(string error) => new Page { Error = error };
        }
    }
}
