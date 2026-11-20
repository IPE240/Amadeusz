using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LokalneAI.Harness.Tools
{
    /// <summary>Decyzja strażnika bezpieczeństwa o wywołaniu narzędzia.</summary>
    public enum SafetyVerdictKind
    {
        /// <summary>Strażnik nie ma zastrzeżeń — dalej decydują zwykłe zasady (wykonawca, uprawnienia, zgoda).</summary>
        Allow,

        /// <summary>Zawsze pytać użytkownika, także w trybie „bez pytania”; okno pytania domyślnie odmawia.</summary>
        AlwaysAsk,

        /// <summary>Odrzucić zawsze, nawet gdy użytkownik by się zgodził: tego model nie robi sam.</summary>
        Block,
    }

    /// <summary>Decyzja strażnika: rodzaj, kategoria zagrożenia i powód po polsku.</summary>
    public sealed class SafetyVerdict
    {
        private SafetyVerdict(SafetyVerdictKind kind, string category, string reason)
        {
            Kind = kind;
            Category = category;
            Reason = reason;
        }

        /// <summary>Co zrobić z wywołaniem.</summary>
        public SafetyVerdictKind Kind { get; }

        /// <summary>
        /// Kategoria: <c>samo_amadeusz</c>, <c>siec_dostep</c>, <c>sekrety</c>, <c>autostart</c>, <c>zabezpieczenia</c>, <c>uprawnienia</c>,
        /// <c>wyciek</c>, <c>zdalne_wykonanie</c>, <c>siec_biura</c>, <c>niszczenie</c>, <c>ryzykowne</c>; null przy <see cref="SafetyVerdictKind.Allow"/>.
        /// </summary>
        public string Category { get; }

        /// <summary>Powód dla użytkownika i modelu; null przy <see cref="SafetyVerdictKind.Allow"/>.</summary>
        public string Reason { get; }

        /// <summary>Bez zastrzeżeń.</summary>
        public static SafetyVerdict Allowed { get; } = new SafetyVerdict(SafetyVerdictKind.Allow, null, null);

        /// <summary>Odrzucić zawsze.</summary>
        public static SafetyVerdict Block(string category, string reason) => new SafetyVerdict(SafetyVerdictKind.Block, category, reason);

        /// <summary>Zawsze pytać.</summary>
        public static SafetyVerdict Ask(string category, string reason) => new SafetyVerdict(SafetyVerdictKind.AlwaysAsk, category, reason);

        /// <summary>Ostrzejsza z dwóch decyzji (blokada &gt; pytanie &gt; brak zastrzeżeń; przy równych — pierwsza).</summary>
        public static SafetyVerdict Stricter(SafetyVerdict a, SafetyVerdict b)
            => b == null ? a : a == null ? b : b.Kind > a.Kind ? b : a;

        /// <summary>Rodzaj, kategoria i powód.</summary>
        public override string ToString() => Kind + (Category == null ? "" : " [" + Category + "] " + Reason);
    }

    /// <summary>
    /// Ustawienia strażnika bezpieczeństwa (<see cref="Session.SessionOptions.Safety"/>). Strażnik działa zawsze: domyślnie chroni foldery
    /// Amadeusza (<c>%APPDATA%\LokalneAI</c>, <c>%LOCALAPPDATA%\LokalneAI</c>, <c>%LOCALAPPDATA%\Programs\LokalneAI</c>), folder programu-gospodarza,
    /// foldery umiejętności sesji, autostart, profil PowerShella i foldery systemowe przed zmianą, a pliki z hasłami i kluczami — przed
    /// odczytem. Gospodarz dopisuje swoje foldery (np. repozytorium, folder wymiany mostu Tekli).
    /// </summary>
    public sealed class SafetyOptions
    {
        /// <summary>Dodatkowe foldery „samego programu”: czytać wolno, zmieniać, przenosić i kasować — nie.</summary>
        public IList<string> ProtectedFolders { get; set; } = new List<string>();

        /// <summary>Dodatkowe pliki i foldery z hasłami albo kluczami: nie wolno ich nawet czytać.</summary>
        public IList<string> SecretPaths { get; set; } = new List<string>();

        /// <summary>Dodatkowe nazwy programów gospodarza (bez rozszerzenia), których model nie może uruchomić.</summary>
        public IList<string> SelfPrograms { get; set; } = new List<string>();

        /// <summary>
        /// Dodatkowe foldery z prywatnymi danymi Amadeusza (np. rozmowy Czatu): chronione jak <see cref="ProtectedFolders"/>, a gdy sesję
        /// liczy model w chmurze, któremu tych danych nie dajemy (Gemini, GPT), nie wolno ich też czytać — treść poszłaby przez API do
        /// dostawcy. Zawsze prywatne: ustawienia i dane Amadeusza (<c>%APPDATA%\LokalneAI</c>, <c>%LOCALAPPDATA%\LokalneAI</c>) i każdy
        /// folder rozmów <c>.amadeusz</c>.
        /// </summary>
        public IList<string> PrivateFolders { get; set; } = new List<string>();

        /// <summary>Folder bieżący dla ścieżek względnych, gdy wykonawca go nie podaje (<see cref="IWorkingFolderProvider"/>).</summary>
        public string WorkingFolder { get; set; }

        /// <summary>
        /// Wołane po każdej blokadzie (do dziennika gospodarza): decyzja, nazwa narzędzia i argumenty w JSON. Błąd w tej funkcji jest
        /// pomijany.
        /// </summary>
        public Action<SafetyVerdict, string, string> Blocked { get; set; }
    }

    /// <summary>
    /// Wykonawca, który zna swój folder bieżący — strażnik bezpieczeństwa liczy z niego ścieżki względne (np. <c>.amadeusz\rozmowa.json</c>).
    /// </summary>
    public interface IWorkingFolderProvider
    {
        /// <summary>Folder, od którego liczą się ścieżki względne w tym wywołaniu; null = nie wiadomo.</summary>
        string WorkingFolderFor(ToolCall call);
    }

    /// <summary>
    /// Strażnik bezpieczeństwa sesji: ocenia każde wywołanie narzędzia, zanim zapyta o zgodę i zanim wykonawca je wykona. Pilnuje, żeby
    /// model nie użył Amadeusza przeciw komputerowi, biuru, sieci i danym: nie zmieniał samego Amadeusza, nie otwierał dostępu z zewnątrz,
    /// nie czytał sekretów, nie dodawał autostartu, nie wyłączał zabezpieczeń, nie podnosił uprawnień, nie wynosił danych, nie uruchamiał
    /// kodu w ukryciu i nie sięgał do innych komputerów. Zasady: <c>docs\bezpieczenstwo.md</c>. To nie jest piaskownica — chroni przed
    /// oszukanym modelem (wstrzyknięcie w dokumencie, na stronie, w wiedzy; model liczony na cudzym komputerze); granicą pozostają
    /// uprawnienia konta Windows.
    /// </summary>
    public sealed class SafetyGuard
    {
        /// <summary>Parametry narzędzi, które są ścieżkami (wartość tekst albo lista tekstów).</summary>
        internal static readonly HashSet<string> PathParameters = new HashSet<string>(StringComparer.Ordinal)
        {
            "sciezka", "nowa_sciezka", "zrodlo", "cel", "wynik", "pliki", "zalaczniki", "folder", "plik", "katalog",
        };

        private static readonly HashSet<string> ReadOnlyCopySource = new HashSet<string>(StringComparer.Ordinal) { "kopiuj" };

        private static readonly Regex UrlInText = new Regex(@"https?://[^\s<>""'`\)\]\}]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex BareDomain = new Regex(@"(?<![@\w.-])((?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+(?:[a-z]{2,24}))(?![\w-])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private readonly SafetyOptions _options;
        private readonly SafetyScope _scope;
        private readonly HashSet<string> _knownUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _userDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _modelUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Znane adresy w kolejności poznania (do podpowiedzi po odmowie: „te możesz otworzyć”).</summary>
        private readonly List<string> _knownOrder = new List<string>();
        private readonly object _gate = new object();

        /// <summary>Tworzy strażnika z ustawieniami gospodarza i folderami umiejętności sesji (chronionymi przed zmianą).</summary>
        public SafetyGuard(SafetyOptions options = null, IEnumerable<string> skillFolders = null)
        {
            _options = options ?? new SafetyOptions();
            _scope = SafetyScope.Create(_options, skillFolders, IsKnownUrl);
        }

        /// <summary>
        /// Dostawca w chmurze, którego model liczy sesję (<c>openai</c>, <c>google</c>, <c>anthropic</c>); null = model lokalny. Ustawia
        /// <see cref="Session.HarnessSession"/>. Przy dostawcy spoza <see cref="SafetyRules.TrustedCloudsForPrivateData"/> danych samego
        /// Amadeusza (<see cref="SafetyOptions.PrivateFolders"/>) nie wolno czytać.
        /// </summary>
        internal string ModelCloud { get; set; }

        /// <summary>
        /// Zapamiętuje adresy stron z polecenia użytkownika: wolno je czytać, a w ich domenach — strony bez parametrów. Użytkownik może
        /// podać każdy adres, także taki, który wcześniej napisał model.
        /// </summary>
        public void RememberUserText(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            lock (_gate)
            {
                foreach (var url in UrlsIn(text).Select(SafetyRules.NormalizeUrl).Where(u => u != null))
                {
                    if (_knownUrls.Add(url))
                    {
                        _knownOrder.Add(url);
                    }

                    if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                    {
                        _userDomains.Add(SafetyRules.RegistrableDomain(uri.Host));
                    }
                }

                foreach (Match match in BareDomain.Matches(text))
                {
                    var domain = SafetyRules.RegistrableDomain(match.Groups[1].Value);
                    if (domain != null && domain.IndexOf('.') > 0 && !SafetyRules.LooksLikeFileName(match.Groups[1].Value))
                    {
                        _userDomains.Add(domain);
                    }
                }
            }
        }

        /// <summary>Adresy http(s) w tekście (bez kropki i przecinka na końcu zdania).</summary>
        internal static IEnumerable<string> UrlsIn(string text)
            => string.IsNullOrEmpty(text)
                ? Enumerable.Empty<string>()
                : UrlInText.Matches(text).Cast<Match>().Select(m => m.Value.TrimEnd('.', ',', ';', ':', '!', '?', '»', '”', '„'));

        /// <summary>Domeny (bez http) w tekście, bez nazw plików wyglądających jak domeny (rysunek.pdf).</summary>
        internal static IEnumerable<string> DomainsIn(string text)
            => string.IsNullOrEmpty(text)
                ? Enumerable.Empty<string>()
                : BareDomain.Matches(text).Cast<Match>().Select(m => m.Groups[1].Value).Where(d => !SafetyRules.LooksLikeFileName(d));

        /// <summary>
        /// Zapamiętuje adresy z wyników wyszukiwania, przeczytanych stron i wiedzy: wolno je czytać dokładnie w tej postaci. Adresy, które
        /// wcześniej napisał sam model (<see cref="NoteModelText"/>), nie stają się przez to znane — inaczej model „wyprałby” adres
        /// z danymi przez wynik narzędzia, który go powtarza.
        /// </summary>
        public void RememberUrls(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            lock (_gate)
            {
                foreach (var url in UrlsIn(text).Select(SafetyRules.NormalizeUrl).Where(u => u != null))
                {
                    if (!_modelUrls.Contains(url) && _knownUrls.Add(url))
                    {
                        _knownOrder.Add(url);
                    }
                }
            }
        }

        /// <summary>
        /// Ostatnio poznane adresy, które wolno otworzyć (od użytkownika, z wyników wyszukiwania, stron i wiedzy), najnowsze najpierw — do
        /// podpowiedzi dla modelu, gdy próbował otworzyć adres zgadnięty albo zaślepkę.
        /// </summary>
        internal IReadOnlyList<string> RecentKnownUrls(int max)
        {
            lock (_gate)
            {
                return Enumerable.Reverse(_knownOrder).Take(Math.Max(0, max)).ToList();
            }
        }

        /// <summary>Tekst napisany przez model (krok, argumenty wywołań): jego adresy nigdy nie stają się „znane” z wyników narzędzi.</summary>
        public void NoteModelText(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            // Adresy w JSON-ie kroku mają ukośniki zabezpieczone (https:\/\/…) — zdejmujemy je przed szukaniem.
            var plain = text.Replace("\\/", "/");
            lock (_gate)
            {
                foreach (var url in UrlsIn(plain).Select(SafetyRules.NormalizeUrl).Where(u => u != null))
                {
                    if (!_knownUrls.Contains(url))
                    {
                        _modelUrls.Add(url);
                    }
                }
            }
        }

        /// <summary>Czy domenę podał użytkownik (w poleceniu albo w adresie).</summary>
        public bool IsUserDomain(string domain)
        {
            lock (_gate)
            {
                return _userDomains.Contains(SafetyRules.RegistrableDomain(domain));
            }
        }

        /// <summary>
        /// Czy model może sięgnąć pod ten adres: dokładnie ten adres był w rozmowie (polecenie, wyniki wyszukiwania, wiedza, pliki)
        /// albo strona bez parametrów w domenie, którą podał użytkownik. Inaczej model mógłby wynieść dane w adresie.
        /// </summary>
        public bool IsKnownUrl(string url)
        {
            var normalized = SafetyRules.NormalizeUrl(url);
            if (normalized == null || !Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
            {
                return false;
            }

            lock (_gate)
            {
                if (_knownUrls.Contains(normalized))
                {
                    return true;
                }

                return uri.Query.Length <= 1 && uri.AbsolutePath.Length <= 120 && !SafetyRules.HasDataLikeSegment(uri.AbsolutePath)
                       && _userDomains.Contains(SafetyRules.RegistrableDomain(uri.Host));
            }
        }

        /// <summary>
        /// Ocenia wywołanie narzędzia gospodarza. <paramref name="workingFolder"/> — folder ścieżek względnych (null = z ustawień);
        /// <paramref name="internet"/> — czy sesja ma dostęp do internetu (przełącznik w oknie i zgoda umiejętności).
        /// </summary>
        public SafetyVerdict Check(ToolCall call, string workingFolder, bool internet)
        {
            if (call == null)
            {
                return SafetyVerdict.Allowed;
            }

            var scope = _scope.With(string.IsNullOrWhiteSpace(workingFolder) ? _options.WorkingFolder : workingFolder, internet,
                SafetyRules.IsUntrustedCloud(ModelCloud));
            SafetyVerdict verdict = SafetyVerdict.Allowed;
            switch (call.Name)
            {
                case "powershell":
                    verdict = SafetyRules.CheckScript(call.GetString("polecenie"), scope, ScriptLanguage.PowerShell);
                    break;
                case "uruchom":
                    verdict = SafetyRules.CheckProgram(call.GetString("program"), call.GetString("argumenty"), scope);
                    break;
                case "zapisz_plik":
                    verdict = SafetyRules.CheckWrittenFile(call.GetString("sciezka"), call.GetString("tresc"), scope);
                    break;
            }

            if (verdict.Kind == SafetyVerdictKind.Block)
            {
                return verdict;
            }

            verdict = SafetyVerdict.Stricter(verdict, CheckPathArguments(call, scope));
            if (verdict.Kind == SafetyVerdictKind.Block)
            {
                return verdict;
            }

            return SafetyVerdict.Stricter(verdict, CheckValues(call.Arguments));
        }

        /// <summary>Ocenia narzędzie internetowe (<c>szukaj_w_internecie</c>, <c>czytaj_strone</c>): długość frazy, pochodzenie adresu.</summary>
        public SafetyVerdict CheckWeb(ToolCall call)
        {
            if (call == null)
            {
                return SafetyVerdict.Allowed;
            }

            if (call.Name == BuiltInTools.WebSearchName)
            {
                var phrase = call.GetString("fraza") ?? "";
                return phrase.Length > SafetyRules.MaxSearchPhrase
                    ? SafetyVerdict.Block(SafetyRules.Leak, "fraza wyszukiwania ma " + phrase.Length + " znaków (najwyżej " + SafetyRules.MaxSearchPhrase
                        + ") — wyszukiwarka nie jest miejscem na treść plików ani danych.")
                    : SafetyVerdict.Allowed;
            }

            if (call.Name == BuiltInTools.WebFetchName)
            {
                var address = (call.GetString("adres") ?? "").Trim();
                if (address.Length > 0 && address.IndexOf("://", StringComparison.Ordinal) < 0)
                {
                    address = "https://" + address;
                }

                return SafetyRules.CheckFetchUrl(address, IsKnownUrl);
            }

            return SafetyVerdict.Allowed;
        }

        /// <summary>Zgłasza blokadę gospodarzowi (dziennik); błąd gospodarza jest pomijany.</summary>
        internal void ReportBlocked(SafetyVerdict verdict, ToolCall call)
        {
            try
            {
                _options.Blocked?.Invoke(verdict, call?.Name, call?.ArgumentsJson);
            }
            catch (Exception)
            {
                // Dziennik gospodarza nie może przerwać tury.
            }
        }

        /// <summary>Ścieżki w argumentach: sekrety nie do czytania, foldery Amadeusza i systemu nie do zmiany.</summary>
        private static SafetyVerdict CheckPathArguments(ToolCall call, SafetyScope scope)
        {
            if (call.Arguments.ValueKind != JsonValueKind.Object || call.Name == "uruchom")
            {
                return SafetyVerdict.Allowed;
            }

            var verdict = SafetyVerdict.Allowed;
            // OCR z zapisz_txt zapisuje plik .txt obok źródła, choć samo narzędzie „czyta”.
            var writes = call.Definition.Mutates || call.GetBool("zapisz_txt") == true;
            foreach (var property in call.Arguments.EnumerateObject())
            {
                if (!PathParameters.Contains(property.Name))
                {
                    continue;
                }

                foreach (var value in Strings(property.Value))
                {
                    if (string.IsNullOrWhiteSpace(value) || value.IndexOf("://", StringComparison.Ordinal) >= 0)
                    {
                        continue;
                    }

                    var full = scope.Resolve(value);
                    if (full == null)
                    {
                        // Ścieżki, której strażnik nie umie sprawdzić, nie przepuszcza (wykonawca mógłby ją zrozumieć inaczej).
                        return SafetyVerdict.Block(SafetyRules.Self, "ścieżki „" + Text.Shorten(value, 80) + "” nie da się sprawdzić — podaj zwykłą ścieżkę.");
                    }

                    if (scope.UntrustedCloud && SafetyRules.IsPrivate(full, scope))
                    {
                        return SafetyRules.PrivateDataBlock(full);
                    }

                    var isSource = property.Name == "zrodlo";
                    var write = writes && !(isSource && ReadOnlyCopySource.Contains(call.Name));
                    var attachment = property.Name == "zalaczniki";
                    if (isSource && (call.Name == "kopiuj" || call.Name == "przenies"))
                    {
                        // Kopia albo przeniesienie pliku .lnk, .vbs… tworzy taki plik w miejscu docelowym.
                        var extension = Path.GetExtension(full);
                        if (SafetyRules.IsBlockedFileExtension(extension))
                        {
                            return SafetyVerdict.Block(SafetyRules.RemoteCode, "kopiowanie albo przenoszenie pliku " + extension + " (Windows go wykonuje) — tego asystent nie robi.");
                        }
                    }

                    var found = SafetyRules.CheckPath(full, write || attachment, scope, isSource || attachment);
                    verdict = SafetyVerdict.Stricter(verdict, found);
                    if (verdict.Kind == SafetyVerdictKind.Block)
                    {
                        return verdict;
                    }
                }
            }

            return verdict;
        }

        /// <summary>Wartości w każdym argumencie: formuły Excela sięgające sieci albo programów (WEBSERVICE, DDE).</summary>
        private static SafetyVerdict CheckValues(JsonElement arguments)
        {
            foreach (var text in Strings(arguments))
            {
                var verdict = SafetyRules.CheckFormula(text);
                if (verdict.Kind != SafetyVerdictKind.Allow)
                {
                    return verdict;
                }
            }

            return SafetyVerdict.Allowed;
        }

        private static IEnumerable<string> Strings(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                    yield return element.GetString();
                    break;
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                    {
                        foreach (var text in Strings(item))
                        {
                            yield return text;
                        }
                    }

                    break;
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        foreach (var text in Strings(property.Value))
                        {
                            yield return text;
                        }
                    }

                    break;
            }
        }
    }

    /// <summary>Język skryptu do oceny treści.</summary>
    internal enum ScriptLanguage
    {
        PowerShell,
        Batch,
        Python,
        Other,
    }

    /// <summary>Foldery chronione, sekrety, nazwy programu i folder bieżący jednego sprawdzenia.</summary>
    internal sealed class SafetyScope
    {
        private SafetyScope()
        {
        }

        /// <summary>Foldery samego programu (z ukośnikiem na końcu): w skryptach nawet wzmianka przy zmianie to blokada.</summary>
        public List<string> Protected { get; private set; } = new List<string>();

        /// <summary>Foldery systemu i autostartu: zapis narzędziem (zapisz_plik, kopiuj, zapis dokumentu) — blokada.</summary>
        public List<string> SystemProtected { get; private set; } = new List<string>();

        /// <summary>Foldery i pliki z sekretami (pełne ścieżki).</summary>
        public List<string> Secrets { get; private set; } = new List<string>();

        /// <summary>Znaczniki folderów chronionych w tekście skryptu (małe litery): pełne ścieżki i ich końcówki.</summary>
        public List<string> Markers { get; private set; } = new List<string>();

        /// <summary>Nazwy programów „samego siebie” (małe litery, bez rozszerzenia).</summary>
        public HashSet<string> SelfPrograms { get; private set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Foldery zainstalowanych programów (uruchamianie stąd to zwykła praca).</summary>
        public List<string> ProgramFolders { get; private set; } = new List<string>();

        /// <summary>Foldery z prywatnymi danymi Amadeusza (ustawienia, dzienniki, rozmowy) — przy <see cref="UntrustedCloud"/> nie do czytania.</summary>
        public List<string> Private { get; private set; } = new List<string>();

        public string WorkingFolder { get; private set; }

        public bool Internet { get; private set; }

        /// <summary>Sesję liczy model w chmurze, któremu nie dajemy danych samego Amadeusza (Gemini, GPT).</summary>
        public bool UntrustedCloud { get; private set; }

        public Func<string, bool> IsKnownUrl { get; private set; }

        public SafetyScope With(string workingFolder, bool internet, bool untrustedCloud = false) => new SafetyScope
        {
            Protected = Protected,
            SystemProtected = SystemProtected,
            Secrets = Secrets,
            Markers = Markers,
            SelfPrograms = SelfPrograms,
            ProgramFolders = ProgramFolders,
            Private = Private,
            IsKnownUrl = IsKnownUrl,
            WorkingFolder = workingFolder,
            Internet = internet,
            UntrustedCloud = untrustedCloud,
        };

        /// <summary>Do testów i oceny bez sesji: domyślne foldery plus dodatkowe.</summary>
        internal static SafetyScope ForTest(IEnumerable<string> protectedFolders, string workingFolder, bool internet, Func<string, bool> isKnownUrl = null)
            => Create(new SafetyOptions { ProtectedFolders = (protectedFolders ?? Enumerable.Empty<string>()).ToList() }, null, isKnownUrl ?? (_ => false))
                .With(workingFolder, internet);

        public static SafetyScope Create(SafetyOptions options, IEnumerable<string> skillFolders, Func<string, bool> isKnownUrl)
        {
            var scope = new SafetyScope { IsKnownUrl = isKnownUrl ?? (_ => false), WorkingFolder = options.WorkingFolder };
            string Folder(Environment.SpecialFolder folder) => Environment.GetFolderPath(folder);
            var appData = Folder(Environment.SpecialFolder.ApplicationData);
            var localAppData = Folder(Environment.SpecialFolder.LocalApplicationData);
            var profile = Folder(Environment.SpecialFolder.UserProfile);
            var documents = Folder(Environment.SpecialFolder.MyDocuments);
            var protectedFolders = new List<string>
            {
                Combine(appData, "LokalneAI"),
                Combine(localAppData, "LokalneAI"),
                Combine(localAppData, "Programs", "LokalneAI"),
                AppDomain.CurrentDomain.BaseDirectory,
            };
            var systemFolders = new List<string>
            {
                // autostart i miejsca, z których Windows albo programy same uruchamiają kod
                Folder(Environment.SpecialFolder.Startup),
                Folder(Environment.SpecialFolder.CommonStartup),
                Folder(Environment.SpecialFolder.StartMenu),
                Folder(Environment.SpecialFolder.CommonStartMenu),
                Combine(documents, "WindowsPowerShell"),
                Combine(documents, "PowerShell"),
                Combine(appData, "Microsoft", "Word", "STARTUP"),
                Combine(appData, "Microsoft", "Excel", "XLSTART"),
                Combine(appData, "Microsoft", "Templates"),
                // system
                Folder(Environment.SpecialFolder.Windows),
                Folder(Environment.SpecialFolder.ProgramFiles),
                Folder(Environment.SpecialFolder.ProgramFilesX86),
                Combine(Environment.GetEnvironmentVariable("ProgramData"), "Microsoft"),
            };
            protectedFolders.AddRange(options.ProtectedFolders ?? Enumerable.Empty<string>());
            protectedFolders.AddRange(options.PrivateFolders ?? Enumerable.Empty<string>());
            scope.Private = Normalize(new[] { Combine(appData, "LokalneAI"), Combine(localAppData, "LokalneAI") }
                .Concat(options.PrivateFolders ?? Enumerable.Empty<string>()));
            foreach (var folder in skillFolders ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(folder))
                {
                    continue;
                }

                protectedFolders.Add(folder);
                try
                {
                    // Cały folder umiejętności (profile\), ale nie dowolny folder nadrzędny: umiejętność z C:\ODS\moja nie zamyka całego C:\ODS.
                    var parent = Path.GetDirectoryName(Path.GetFullPath(folder).TrimEnd('\\', '/'));
                    if (parent != null && string.Equals(Path.GetFileName(parent), "profile", StringComparison.OrdinalIgnoreCase))
                    {
                        protectedFolders.Add(parent);
                    }
                }
                catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException || e is System.Security.SecurityException)
                {
                    // Zła ścieżka umiejętności — chroniony zostaje sam folder z listy.
                }
            }

            scope.Protected = Normalize(protectedFolders);
            scope.SystemProtected = Normalize(systemFolders);
            var secrets = new List<string>
            {
                Combine(appData, "LokalneAI", "klucze.json"),
                Combine(profile, ".ssh"),
                Combine(profile, ".aws"),
                Combine(profile, ".azure"),
                Combine(profile, ".kube"),
                Combine(profile, ".docker"),
                Combine(profile, ".gnupg"),
                Combine(appData, "Microsoft", "Credentials"),
                Combine(localAppData, "Microsoft", "Credentials"),
                Combine(appData, "Microsoft", "Protect"),
                Combine(appData, "Microsoft", "Vault"),
                Combine(localAppData, "Microsoft", "Vault"),
                Combine(appData, "Microsoft", "Crypto"),
                Combine(localAppData, "Google", "Chrome", "User Data"),
                Combine(localAppData, "Microsoft", "Edge", "User Data"),
                Combine(localAppData, "BraveSoftware"),
                Combine(appData, "Mozilla", "Firefox", "Profiles"),
                Combine(appData, "Opera Software"),
                Combine(Folder(Environment.SpecialFolder.Windows), "System32", "config"),
            };
            secrets.AddRange(options.SecretPaths ?? Enumerable.Empty<string>());
            scope.Secrets = Normalize(secrets, trailingSlash: false);
            scope.SelfPrograms = new HashSet<string>(
                new[] { "lokalneai", "lokalneai.app", "lokalneai.eval", "amadeusz", "amadeusz.stary" }
                    .Concat((options.SelfPrograms ?? Enumerable.Empty<string>()).Select(p => Path.GetFileNameWithoutExtension(p ?? "").ToLowerInvariant()))
                    .Where(p => p.Length > 0),
                StringComparer.OrdinalIgnoreCase);
            scope.ProgramFolders = Normalize(new[]
            {
                Folder(Environment.SpecialFolder.ProgramFiles),
                Folder(Environment.SpecialFolder.ProgramFilesX86),
                Folder(Environment.SpecialFolder.Windows),
                Combine(localAppData, "Programs"),
            });
            var markers = new List<string>();
            foreach (var folder in scope.Protected.Where(p => p.IndexOf("lokalneai", StringComparison.OrdinalIgnoreCase) < 0 && !IsUnder(p, systemFolders)))
            {
                var trimmed = folder.TrimEnd('\\').ToLowerInvariant();
                markers.Add(trimmed);
                var parts = trimmed.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3)
                {
                    var tail = parts[parts.Length - 2] + "\\" + parts[parts.Length - 1];
                    if (tail.Length >= 10)
                    {
                        markers.Add(tail);
                    }
                }
            }

            scope.Markers = markers.Distinct().ToList();
            return scope;
        }

        /// <summary>
        /// Czy tekst skryptu (małe litery) wskazuje folder chroniony: znacznik z ukośnikiem albo końcem ścieżki po nim, żeby
        /// <c>c:\ods\amadeusz</c> nie pasowało do <c>c:\ods\amadeusz2</c>.
        /// </summary>
        public bool MentionsProtectedFolder(string text)
        {
            foreach (var marker in Markers.SelectMany(m => new[] { m, m.Replace('\\', '/') }))
            {
                for (var at = text.IndexOf(marker, StringComparison.Ordinal); at >= 0; at = text.IndexOf(marker, at + 1, StringComparison.Ordinal))
                {
                    var end = at + marker.Length;
                    if (end >= text.Length || "\\/'\"` ;|),\n".IndexOf(text[end]) >= 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>Pełna ścieżka (zmienne środowiskowe, ~, ścieżka względna do folderu bieżącego); null, gdy to nie ścieżka.</summary>
        public string Resolve(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || path.IndexOf("://", StringComparison.Ordinal) >= 0 || path.IndexOf('\n') >= 0 || path.Length > 2000)
            {
                return null;
            }

            try
            {
                var raw = path.Trim();
                var parameter = Regex.Match(raw, @"^-[A-Za-z]+:(.+)$");
                if (parameter.Success)
                {
                    raw = parameter.Groups[1].Value; // -Path:C:\… — wartość parametru sklejona z nazwą
                }

                var text = PowerShellPolicy.ExpandEnvironment(raw.Trim('"', '\'')) ?? raw.Trim('"', '\'');
                if (text == "~" || text.StartsWith("~\\", StringComparison.Ordinal) || text.StartsWith("~/", StringComparison.Ordinal))
                {
                    text = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + text.Substring(1);
                }

                if (text.StartsWith("\\\\?\\UNC\\", StringComparison.OrdinalIgnoreCase))
                {
                    text = "\\\\" + text.Substring(8); // \\?\UNC\serwer\udział → \\serwer\udział
                }
                else if (text.StartsWith("\\\\?\\", StringComparison.Ordinal) || text.StartsWith("\\\\.\\", StringComparison.Ordinal))
                {
                    text = text.Substring(4);
                }

                var wildcard = text.IndexOfAny(new[] { '*', '?' });
                if (wildcard >= 0)
                {
                    var cut = text.LastIndexOfAny(new[] { '\\', '/' }, wildcard);
                    text = cut >= 0 ? text.Substring(0, cut + 1) : ".";
                }

                var basePath = string.IsNullOrWhiteSpace(WorkingFolder) ? Environment.CurrentDirectory : WorkingFolder;
                var full = Path.GetFullPath(Path.IsPathRooted(text) ? text : Path.Combine(basePath, text));
                return LongName(full);
            }
            catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException || e is IOException
                                      || e is System.Security.SecurityException)
            {
                return null;
            }
        }

        /// <summary>Krótka nazwa 8.3 (C:\ODS\AMADEU~1) → pełna, gdy plik albo folder istnieje.</summary>
        private static string LongName(string path)
        {
            if (path.IndexOf('~') < 0)
            {
                return path;
            }

            try
            {
                if (Directory.Exists(path))
                {
                    return new DirectoryInfo(path).FullName;
                }

                var parent = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                {
                    var name = Path.GetFileName(path);
                    var match = new DirectoryInfo(parent).EnumerateFileSystemInfos(name).FirstOrDefault();
                    if (match != null)
                    {
                        return Path.Combine(LongName(parent), match.Name);
                    }
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is System.Security.SecurityException)
            {
                // Bez rozwinięcia — zostaje ścieżka jak była.
            }

            return path;
        }

        private static bool IsUnder(string folder, IEnumerable<string> roots)
            => roots.Where(r => !string.IsNullOrWhiteSpace(r)).Any(r => folder.StartsWith(r.TrimEnd('\\', '/') + "\\", StringComparison.OrdinalIgnoreCase));

        private static string Combine(params string[] parts)
            => parts.Any(string.IsNullOrWhiteSpace) ? null : Path.Combine(parts);

        private static List<string> Normalize(IEnumerable<string> paths, bool trailingSlash = true)
            => paths.Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p =>
                {
                    try
                    {
                        var full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(p.Trim())).TrimEnd('\\', '/');
                        return trailingSlash || Directory.Exists(full) ? full + "\\" : full;
                    }
                    catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException)
                    {
                        return null;
                    }
                })
                .Where(p => p != null && p.Length > 3) // nigdy cały dysk
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
    }
}
