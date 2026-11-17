using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LokalneAI.Harness.Llm;
using LokalneAI.Harness.Postgres;
using LokalneAI.Harness.Profiles;
using LokalneAI.Harness.Session;
using LokalneAI.Harness.Tools;
using Microsoft.Win32;

namespace LokalneAI.App;

/// <summary>
/// Zakładka Code: asystent, który wykonuje polecenia (Tekla, baza CDE, pliki, PowerShell) — to samo, co gospodarz
/// (np. most do Tekli) zrobi w kodzie: wybór profilu, wykonawcy, uprawnień i modelu, a potem gotowy <c>HarnessPanel</c>.
/// </summary>
public partial class HarnessView : UserControl
{
    private Func<HarnessContext>? _contextProvider;
    private HarnessContext _context = new();
    private ILlmClient? _llm;
    private volatile string _permission = "pytaj";

    public HarnessView()
    {
        InitializeComponent();
        Panel.SessionFactory = CreateSession;
    }

    internal string? ProfileName => ProfileBox.SelectedItem as string;

    /// <summary>Kontrola bez klikania: polecenia zmieniające są zatwierdzane bez okna pytania.</summary>
    internal bool AutoConfirm { get; set; }

    /// <summary>Wiąże widok z oknem: skąd brać model, adres bramy i ustawienia. Wołać raz, przed pokazaniem.</summary>
    public void Attach(Func<HarnessContext> contextProvider, bool allowSeparateWindow = true)
    {
        _contextProvider = contextProvider;
        _context = contextProvider();
        var settings = _context.Settings;
        ExchangeBox.Text = settings.TeklaExchangeFolder ?? "";
        RootsBox.Text = string.IsNullOrWhiteSpace(settings.FileRoots)
            ? "C:\\ODS;%USERPROFILE%\\Documents;%USERPROFILE%\\Desktop"
            : settings.FileRoots;
        PermissionBox.SelectedIndex = settings.CodePermission == "czyta" ? 0 : 1;
        WindowButton.Visibility = allowSeparateWindow ? Visibility.Visible : Visibility.Collapsed;
        ProfileBox.Items.Clear();
        foreach (var name in ProfilesFolder.List(_context.ProfilesRoot))
        {
            ProfileBox.Items.Add(name);
        }

        ProfileBox.SelectedItem = ProfileBox.Items.Contains(settings.HarnessProfile) ? settings.HarnessProfile : ProfileBox.Items.Cast<object>().FirstOrDefault();
        if (ProfileBox.Items.Count == 0)
        {
            HintText.Text = "Nie znalazłem folderu z profilami (profile\\<nazwa>\\profil.json) obok programu ani wyżej.";
        }

        RefreshModelText();
    }

    /// <summary>Odświeża opis modelu i komputera, który liczy (po zmianie w nagłówku albo w Ustawieniach), i ostrzeżenie o chmurze.</summary>
    /// <summary>Kontrola bez klikania: plik połączenia i person_id podane z wiersza poleceń (bez zapisu).</summary>
    private string? _testConnectionFile;

    private string? _testPerson;

    public void RefreshModelText()
    {
        _context = _contextProvider?.Invoke() ?? _context;
        if (_context.Cloud is not null)
        {
            ModelText.Text = "Model: " + _context.ModelLabel + " · " + _context.ComputeWhere;
            ModelText.Foreground = _context.ModelBrush ?? (Brush)FindResource("MutedBrush");
            ModelText.FontWeight = FontWeights.SemiBold;
        }
        else
        {
            ModelText.Text = "Model: " + _context.ModelLabel + " · kontekst " + _context.ContextLength + " · " + _context.ComputeWhere;
            ModelText.Foreground = (Brush)FindResource("MutedBrush");
            ModelText.FontWeight = FontWeights.Normal;
        }

        ModelText.Text += " · " + InternetText();
        RefreshCloudText();
    }

    /// <summary>„internet: tak” albo dlaczego nie (przełącznik w oknie, pole „internet” w profil.json).</summary>
    private string InternetText()
    {
        if (_context.Web is null)
        {
            return "internet: zablokowany";
        }

        var settings = ProfileName == null ? null : CloudViewModel.SafeSettings(Path.Combine(_context.ProfilesRoot, ProfileName));
        return settings?.Internet == true ? "internet: tak" : "internet: nie w tym profilu";
    }

    /// <summary>
    /// Pasek pod nagłówkiem: dokąd idą dane przy modelu w chmurze, a gdy profil na niego nie pozwala (pole „chmura” w profil.json) —
    /// ostrzeżenie. Sesja i tak odmówi (sprawdza to biblioteka), tu widać to przed wysłaniem.
    /// </summary>
    private void RefreshCloudText()
    {
        if (_context.Cloud is not { } cloud || ProfileName is null)
        {
            CloudBar.Visibility = Visibility.Collapsed;
            return;
        }

        var settings = CloudViewModel.SafeSettings(ProfileFolderPath) ?? new ProfileSettings();
        CloudBar.Visibility = Visibility.Visible;
        if (settings.AllowsCloud(cloud.Id))
        {
            CloudBar.BorderBrush = _context.ModelBrush ?? (Brush)FindResource("LineBrush");
            CloudText.Foreground = (Brush)FindResource("TextBrush");
            CloudText.Text = "Model w chmurze " + cloud.Name + ": polecenia, wyniki narzędzi i dokumentacja profilu " + ProfileName
                + " idą przez internet do " + cloud.Name + ". Uprawnienia działają tak samo jak przy modelu lokalnym.";
        }
        else
        {
            CloudBar.BorderBrush = (Brush)FindResource("BadBrush");
            CloudText.Foreground = (Brush)FindResource("BadBrush");
            CloudText.Text = "Profil " + ProfileName + ": " + CloudProvider.DescribeAllowed(settings.CloudProviders)
                + ". Model " + _context.ModelLabel + " (" + cloud.Name + ") tu nie zadziała — wybierz inny model na górze okna.";
        }
    }

    /// <summary>Kontrola bez klikania: profil i ustawienia wykonawcy bez zapisu w ustawieniach użytkownika.</summary>
    internal void ConfigureForTest(string profile, string roots, string? connection, string? person)
    {
        RootsBox.Text = roots;
        _testConnectionFile = connection;
        _testPerson = person;
        ProfileBox.SelectedItem = profile;
        Panel.NewSession();
    }

    /// <summary>Do kontroli bez klikania: wysyła polecenia po kolei.</summary>
    internal async Task RunAsync(IEnumerable<string> commands)
    {
        foreach (var command in commands)
        {
            await Panel.SendAsync(command);
        }
    }

    /// <summary>Zapisuje wybory (profil, foldery, uprawnienia) w ustawieniach programu.</summary>
    public void SaveChoices()
    {
        var settings = _context.Settings;
        settings.HarnessProfile = ProfileName;
        settings.TeklaExchangeFolder = ExchangeBox.Text.Trim();
        settings.FileRoots = RootsBox.Text.Trim();
        settings.CodePermission = _permission == "czyta" ? "czyta" : "pytaj";
        _context.Save?.Invoke();
    }

    /// <summary>Kończy sesję (kasuje historię) i zamyka połączenie z modelem.</summary>
    public void Shutdown()
    {
        Panel.NewSession();
        (_llm as IDisposable)?.Dispose();
        _llm = null;
    }

    private HarnessSession CreateSession()
    {
        _context = _contextProvider?.Invoke() ?? _context;
        var folder = ProfileFolderPath;
        var profile = HarnessProfile.Load(folder);
        (_llm as IDisposable)?.Dispose();
        _llm = null;
        if (_context.Cloud is { } cloud)
        {
            // Model w chmurze idzie prosto z tego komputera (bez bramy i kolejki). Czy profil na niego pozwala, sprawdza HarnessSession.
            var key = _context.GetApiKey?.Invoke(cloud);
            if (string.IsNullOrEmpty(key))
            {
                throw new InvalidOperationException("Brak klucza API " + cloud.Name + " — wpisz go w Ustawieniach → Modele w chmurze.");
            }

            _llm = CloudLlm.Create(cloud, key, _context.Model, _context.Temperature);
        }
        else
        {
            _llm = new OllamaLlmClient(new OllamaOptions
            {
                Url = _context.OllamaUrl,
                Model = _context.Model,
                ContextLength = _context.ContextLength,
                Temperature = _context.Temperature,
                KeepAlive = "10m",
                ClientName = "Code: " + ProfileName,
                // W kolejce bramy można poczekać, aż inny program albo komputer skończy.
                RequestTimeout = TimeSpan.FromMinutes(10),
            });
        }

        var options = new SessionOptions { ConfirmMutation = ConfirmAsync };
        if (_context.Web is { } web && profile.Settings.Internet)
        {
            // Internet odblokowany i profil na niego pozwala (cde — nie: dane z bazy nie mogą wyjść w zapytaniu do wyszukiwarki).
            profile = profile.WithInternet();
            options.Web = web;
        }
        IToolExecutor executor;
        if (profile.Settings.SqlTools.Count > 0)
        {
            // Kontrola bez klikania może podać plik połączenia; zwykle dane są zapamiętane w Ustawieniach → Baza CDE.
            var connection = !string.IsNullOrWhiteSpace(_testConnectionFile) ? ConnectionFromJson(_testConnectionFile) : _context.GetCdeConnection?.Invoke();
            if (connection is null)
            {
                throw new InvalidOperationException("Brak danych logowania do bazy CDE. Wpisz je raz w Ustawieniach → Baza CDE (albo wczytaj plik połączenia) — program je zapamięta.");
            }

            var person = !string.IsNullOrWhiteSpace(_testPerson) ? _testPerson : _context.Settings.CdePersonId;
            if (string.IsNullOrWhiteSpace(person))
            {
                throw new InvalidOperationException("Podaj swój person_id w Ustawieniach → Baza CDE — model pyta o „moje” godziny.");
            }

            executor = new PostgresReadOnlyExecutor(new PostgresReadOnlyOptions { ConnectionString = connection });
            options.Variables["ja"] = person.Trim();
            options.ContextNote = "Pytający ma w bazie identyfikator osoby {{ja}} — w SQL wpisuj dosłownie '{{ja}}', program podstawi wartość. Dziś jest "
                + DateTime.Today.ToString("yyyy-MM-dd") + ".";
        }
        else if (IsFilesProfile(ProfileName))
        {
            var roots = RootsBox.Text.Split(';').Select(r => r.Trim()).Where(r => r.Length > 0).ToList();
            var executorFiles = new FileSystemExecutor(new FileSystemOptions
            {
                AllowedRoots = roots,
                // Pierwszy istniejący (na innym PC może nie być C:\ODS).
                WorkingFolder = roots.Select(Environment.ExpandEnvironmentVariables).FirstOrDefault(Directory.Exists),
            });
            executor = executorFiles;
            options.ContextNote = "Komputer z Windows, użytkownik " + Environment.UserName + ". Folder bieżący na start: " + executorFiles.WorkingFolder
                + ". Dozwolone foldery: " + string.Join("; ", roots.Select(Environment.ExpandEnvironmentVariables)) + ". Dziś jest " + DateTime.Today.ToString("yyyy-MM-dd") + ".";
        }
        else if (profile.Tools.All(t => t.IsBuiltIn))
        {
            // Profil samej wiedzy (np. mkk): model szuka w dokumentacji i odpowiada; poleceń gospodarza nie ma, więc i wykonawcy nie trzeba.
            executor = new DelegateExecutor((call, _) => Task.FromResult(ToolResult.Error("Profil " + profile.Name + " nie ma polecenia " + call.Name + ".")));
            options.ContextNote = "Dziś jest " + DateTime.Today.ToString("yyyy-MM-dd") + ".";
        }
        else if (BridgeRadio.IsChecked == true)
        {
            executor = new TeklaBridgeExecutor(new TeklaBridgeOptions { ExchangeFolder = ExchangeBox.Text.Trim(), ProfileFolder = folder });
        }
        else
        {
            executor = new SimulatedModelExecutor();
            options.ContextNote = "Model Tekli jest symulowany (próba bez Tekli).";
        }

        return new HarnessSession(_llm, profile, executor, options);
    }

    /// <summary>Zgoda na polecenie zmieniające według wybranych uprawnień. Wołane z wątku w tle.</summary>
    private Task<bool> ConfirmAsync(ToolCall call, CancellationToken cancellationToken)
    {
        if (AutoConfirm)
        {
            return Task.FromResult(true);
        }

        return _permission switch
        {
            "czyta" => Task.FromResult(false),
            "bez" when !call.ConfirmationDangerous => Task.FromResult(true),
            _ => Panel.ConfirmMutationAsync(call, cancellationToken),
        };
    }

    private void Permission_Changed(object sender, SelectionChangedEventArgs e)
    {
        _permission = (PermissionBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "pytaj";
        if (_permission == "czyta")
        {
            HintText.Text = "Tylko czyta: polecenia, które coś zmieniają, będą odrzucane bez pytania.";
        }
        else if (_permission == "bez")
        {
            HintText.Text = "Bez pytania: zmiany wykonują się od razu (kasowanie nadal pyta). Po ponownym uruchomieniu programu wraca „Pyta o każdą zmianę”.";
        }
    }

    private void Profile_Changed(object sender, SelectionChangedEventArgs e)
    {
        var isCde = ProfileName == "cde";
        var isFiles = IsFilesProfile(ProfileName);
        var isComputer = ProfileName == "komputer";
        // Profil samej wiedzy (bez poleceń gospodarza, np. mkk): nie ma czego wykonywać, więc opcje Tekli nie mają sensu.
        var isKnowledge = !isCde && !isFiles && ProfileName != null && !HarnessProfile.HasHostTools(ProfileFolderPath);
        Panel.ResultPreviewLength = isComputer || isKnowledge ? 3000 : 600;
        TeklaOptions.Visibility = isCde || isFiles || isKnowledge ? Visibility.Collapsed : Visibility.Visible;
        CdeOptions.Visibility = isCde ? Visibility.Visible : Visibility.Collapsed;
        RefreshCdeText();
        FilesOptions.Visibility = isFiles ? Visibility.Visible : Visibility.Collapsed;
        Panel.Title = ProfileName == null ? "Asystent" : "Asystent: " + ProfileName;
        HintText.Text = isCde
            ? "SQL od modelu wykonuje się w transakcji tylko do odczytu. Przykłady: „Ile przepracowałem godzin na projekcie WSP?”, „W których dokumentach projektu EDO jest mowa o kotwach?”."
            : isComputer
                ? "Przykład: „cd C:\\ODS\\Projekty i utwórz folder Raporty”, „ile jest PDF-ów z podfolderami?”, „spakuj R02 do zipa”, „otwórz Notatnik”. "
                  + "Polecenia, które tylko czytają, idą od razu; zmiany zależą od uprawnień."
            : isFiles
                ? "Przykład: „cd C:\\ODS\\Projekty, a potem utwórz folder Raporty”, „co jest w tym folderze?”, „otwórz go”. Kasowania nie ma."
            : isKnowledge
                ? "Profil wiedzy: model szuka w dokumentacji (szukaj_wiedzy) i odpowiada, nie wykonuje poleceń. "
                  + (ProfileName == "mkk"
                      ? "Przykład: „jak w Hali dodać stężenia dachowe?”, „jaki znak ma siła osiowa w Robocie, a jaki w RFEM?”, „ile zapytań ma plan Free API RFEM 6?”."
                      : "Dokumentację pobiera przycisk „Aktualizuj wiedzę”.")
                : "Przykład: „wstaw słup HEB300 w punkcie 6000, 0 od zera do 3,53”, potem „zmień go na HEB320”, „dodaj mu podstawę z 4 kotwami M24”.";
        Panel.NewSession();
        RefreshCloudText();
        _ = AutoSyncAsync();
    }

    /// <summary>Profile obsługiwane przez wykonawcę plików i poleceń (folder, PowerShell, programy).</summary>
    private static bool IsFilesProfile(string? name) => name == "pliki" || name == "komputer";

    private void Executor_Changed(object sender, RoutedEventArgs e)
    {
        if (IsLoaded)
        {
            Panel.NewSession();
        }
    }

    private void Steps_Changed(object sender, RoutedEventArgs e) => Panel.ShowModelSteps = StepsBox.IsChecked == true;

    private void OpenWindow_Click(object sender, RoutedEventArgs e)
    {
        if (_contextProvider is null)
        {
            return;
        }

        SaveChoices();
        new HarnessWindow(_contextProvider) { Owner = Window.GetWindow(this) }.Show();
    }

    private string ProfileFolderPath => Path.Combine(_context.ProfilesRoot, ProfileName ?? "");

    private static string RepoCache => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LokalneAI", "repo");

    /// <summary>Sama aktualizacja wiedzy, gdy profil ma źródła, a od ostatniej minęło ponad 6 godzin.</summary>
    private async Task AutoSyncAsync()
    {
        var folder = ProfileFolderPath;
        if (ProfileName is null || KnowledgeSync.ReadSources(folder).Count == 0)
        {
            return;
        }

        var last = KnowledgeSync.LastSync(folder);
        if (last != null && DateTime.Now - last.Value < TimeSpan.FromHours(6))
        {
            return;
        }

        await SyncAsync();
    }

    private async void Sync_Click(object sender, RoutedEventArgs e) => await SyncAsync();

    private void Sources_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileName == null)
        {
            return;
        }

        new SourcesWindow(ProfileName, ProfileFolderPath, RepoCache) { Owner = Window.GetWindow(this) }.ShowDialog();
        Panel.NewSession(); // nowa sesja zbuduje indeks wiedzy z nowych plików
    }

    private async Task SyncAsync()
    {
        var folder = ProfileFolderPath;
        if (KnowledgeSync.ReadSources(folder).Count == 0)
        {
            HintText.Text = "Profil " + ProfileName + " nie ma źródeł dokumentacji. Kliknij „Źródła wiedzy…” i wklej adres repozytorium.";
            return;
        }

        SyncButton.IsEnabled = false;
        HintText.Text = "Aktualizuję wiedzę profilu " + ProfileName + "…";
        try
        {
            var result = await Task.Run(() => KnowledgeSync.SyncAsync(folder, RepoCache));
            HintText.Text = "Wiedza profilu " + ProfileName + ": " + result + ". Nowa sesja użyje nowych plików.";
        }
        catch (Exception ex)
        {
            HintText.Text = "Aktualizacja wiedzy nie powiodła się: " + ex.Message;
        }
        finally
        {
            SyncButton.IsEnabled = true;
        }
    }

    /// <summary>Przycisk „Dane logowania…” — okno główne otwiera Ustawienia → Baza CDE.</summary>
    public event EventHandler? CdeSettingsRequested;

    private void CdeSettings_Click(object sender, RoutedEventArgs e)
    {
        if (CdeSettingsRequested is null)
        {
            MessageBox.Show(Window.GetWindow(this), "Dane logowania do bazy CDE ustawisz w głównym oknie: Ustawienia → Baza CDE.", "Baza CDE");
            return;
        }

        CdeSettingsRequested(this, EventArgs.Empty);
    }

    /// <summary>Opis zapamiętanych danych logowania CDE (albo czerwona prośba o nie).</summary>
    public void RefreshCdeText()
    {
        _context = _contextProvider?.Invoke() ?? _context;
        var missing = _context.GetCdeConnection is null || string.IsNullOrEmpty(_context.CdeDescription) || _context.CdeDescription.StartsWith("brak", StringComparison.Ordinal);
        CdeText.Text = missing
            ? "Baza CDE: brak danych logowania — wpisz je raz w Ustawieniach, program je zapamięta."
            : "Baza CDE: " + _context.CdeDescription + " (tylko odczyt)";
        CdeText.Foreground = (Brush)FindResource(missing ? "BadBrush" : "MutedBrush");
    }

    private static string ConnectionFromJson(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var json = document.RootElement;
        string? Get(string name) => json.TryGetProperty(name, out var v) ? (v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText()) : null;
        return $"Host={Get("host")};Port={Get("port") ?? "5432"};Database={Get("dbname")};Username={Get("user")};Password={Get("password")};Timeout=15;Command Timeout=30;Application Name=LokalneAI";
    }
}

/// <summary>Co zakładka Code bierze z okna głównego.</summary>
public sealed class HarnessContext
{
    /// <summary>Adres bramy (kolejka, wybrany komputer) albo Ollamy, gdy brama nie działa.</summary>
    public string OllamaUrl { get; init; } = "";

    public string Model { get; init; } = "";

    /// <summary>Nazwa modelu do okna (dla chmury ładna nazwa, np. „Claude Sonnet 5.5”).</summary>
    public string ModelLabel { get; init; } = "";

    /// <summary>Dostawca w chmurze; null = model z Ollamy (ten komputer albo inny w sieci).</summary>
    public CloudProvider? Cloud { get; init; }

    /// <summary>Kolor dostawcy w chmurze; null = model lokalny.</summary>
    public Brush? ModelBrush { get; init; }

    /// <summary>Klucz API dostawcy (z programu albo zmiennej środowiskowej).</summary>
    public Func<CloudProvider, string?>? GetApiKey { get; init; }

    /// <summary>Połączenie z bazą CDE z Ustawień (hasło odszyfrowane dopiero tutaj); null = brak danych logowania.</summary>
    public Func<string?>? GetCdeConnection { get; init; }

    /// <summary>Opis danych logowania CDE do zakładki Code, np. „maciej@10.0.0.214:5441/cde”.</summary>
    public string CdeDescription { get; init; } = "";

    /// <summary>Dostęp do internetu (przełącznik w oknie); null = zablokowany.</summary>
    public WebAccess? Web { get; init; }

    public int ContextLength { get; init; }

    public double Temperature { get; init; }

    public string ProfilesRoot { get; init; } = "";

    /// <summary>Opis do okna, np. „liczy: ten komputer”.</summary>
    public string ComputeWhere { get; init; } = "";

    public LokalneAI.Core.Storage.AppSettings Settings { get; init; } = new();

    public Action? Save { get; init; }
}

/// <summary>Szuka folderu <c>profile</c> (z podfolderami zawierającymi <c>profil.json</c>) obok programu albo wyżej.</summary>
internal static class ProfilesFolder
{
    public static string Find()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "profile");
            if (Directory.Exists(candidate) && Directory.GetFiles(candidate, "profil.json", SearchOption.AllDirectories).Length > 0)
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return Path.Combine(AppContext.BaseDirectory, "profile");
    }

    public static IEnumerable<string> List(string root)
        => Directory.Exists(root)
            ? Directory.GetDirectories(root).Where(d => File.Exists(Path.Combine(d, "profil.json"))).Select(d => Path.GetFileName(d)).OrderBy(n => n)
            : Enumerable.Empty<string>();
}
