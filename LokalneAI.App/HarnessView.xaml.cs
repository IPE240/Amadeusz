using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
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
    private OllamaLlmClient? _llm;
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
        ConnectionBox.Text = settings.CdeConnectionFile ?? "";
        PersonBox.Text = settings.CdePersonId ?? "";
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

    /// <summary>Odświeża opis modelu i komputera, który liczy (po zmianie w nagłówku albo w Ustawieniach).</summary>
    public void RefreshModelText()
    {
        _context = _contextProvider?.Invoke() ?? _context;
        ModelText.Text = "Model: " + ModelItem.ShortName(_context.Model) + " · kontekst " + _context.ContextLength + " · " + _context.ComputeWhere;
    }

    /// <summary>Kontrola bez klikania: profil i ustawienia wykonawcy bez zapisu w ustawieniach użytkownika.</summary>
    internal void ConfigureForTest(string profile, string roots, string? connection, string? person)
    {
        RootsBox.Text = roots;
        ConnectionBox.Text = connection ?? "";
        PersonBox.Text = person ?? "";
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
        settings.CdeConnectionFile = ConnectionBox.Text.Trim();
        settings.CdePersonId = PersonBox.Text.Trim();
        settings.FileRoots = RootsBox.Text.Trim();
        settings.CodePermission = _permission == "czyta" ? "czyta" : "pytaj";
        _context.Save?.Invoke();
    }

    /// <summary>Kończy sesję (kasuje historię) i zamyka połączenie z modelem.</summary>
    public void Shutdown()
    {
        Panel.NewSession();
        _llm?.Dispose();
        _llm = null;
    }

    private HarnessSession CreateSession()
    {
        _context = _contextProvider?.Invoke() ?? _context;
        var folder = ProfileFolderPath;
        var profile = HarnessProfile.Load(folder);
        _llm?.Dispose();
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

        var options = new SessionOptions { ConfirmMutation = ConfirmAsync };
        IToolExecutor executor;
        if (profile.Settings.SqlTools.Count > 0)
        {
            if (string.IsNullOrWhiteSpace(ConnectionBox.Text) || !File.Exists(ConnectionBox.Text))
            {
                throw new InvalidOperationException("Wskaż plik połączenia z bazą (JSON: host, port, dbname, user, password).");
            }

            if (string.IsNullOrWhiteSpace(PersonBox.Text))
            {
                throw new InvalidOperationException("Podaj swój person_id (identyfikator osoby w CDE) — model pyta o „moje” godziny.");
            }

            executor = new PostgresReadOnlyExecutor(new PostgresReadOnlyOptions { ConnectionString = ConnectionFromJson(ConnectionBox.Text) });
            options.Variables["ja"] = PersonBox.Text.Trim();
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
        Panel.ResultPreviewLength = isComputer ? 3000 : 600;
        TeklaOptions.Visibility = isCde || isFiles ? Visibility.Collapsed : Visibility.Visible;
        CdeOptions.Visibility = isCde ? Visibility.Visible : Visibility.Collapsed;
        FilesOptions.Visibility = isFiles ? Visibility.Visible : Visibility.Collapsed;
        Panel.Title = ProfileName == null ? "Asystent" : "Asystent: " + ProfileName;
        HintText.Text = isCde
            ? "SQL od modelu wykonuje się w transakcji tylko do odczytu; dostęp ustala rola w pliku połączenia. Przykład: „Ile przepracowałem godzin na projekcie WSP?”."
            : isComputer
                ? "Przykład: „cd C:\\ODS\\Projekty i utwórz folder Raporty”, „ile jest PDF-ów z podfolderami?”, „spakuj R02 do zipa”, „otwórz Notatnik”. "
                  + "Polecenia, które tylko czytają, idą od razu; zmiany zależą od uprawnień."
            : isFiles
                ? "Przykład: „cd C:\\ODS\\Projekty, a potem utwórz folder Raporty”, „co jest w tym folderze?”, „otwórz go”. Kasowania nie ma."
                : "Przykład: „wstaw słup HEB300 w punkcie 6000, 0 od zera do 3,53”, potem „zmień go na HEB320”, „dodaj mu podstawę z 4 kotwami M24”.";
        Panel.NewSession();
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

    private void PickConnection_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Plik połączenia (*.json)|*.json", Title = "Plik połączenia z bazą CDE" };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            ConnectionBox.Text = dialog.FileName;
            Panel.NewSession();
        }
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
