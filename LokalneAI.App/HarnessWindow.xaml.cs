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
/// Próba osadzenia pół-harnessu: to samo, co gospodarz (np. most do Tekli) zrobi w kodzie —
/// wybór profilu, wykonawcy i modelu, a potem gotowy <c>HarnessPanel</c>.
/// </summary>
public partial class HarnessWindow : Window
{
    private readonly HarnessContext _context;
    private OllamaLlmClient? _llm;

    public HarnessWindow(HarnessContext context)
    {
        InitializeComponent();
        _context = context;
        ExchangeBox.Text = context.Settings.TeklaExchangeFolder ?? "";
        ConnectionBox.Text = context.Settings.CdeConnectionFile ?? "";
        PersonBox.Text = context.Settings.CdePersonId ?? "";
        RootsBox.Text = string.IsNullOrWhiteSpace(context.Settings.FileRoots)
            ? "C:\\ODS;%USERPROFILE%\\Documents;%USERPROFILE%\\Desktop"
            : context.Settings.FileRoots;
        ModelText.Text = "Model: " + context.Model + " · kontekst " + context.ContextLength;
        foreach (var name in ProfilesFolder.List(context.ProfilesRoot))
        {
            ProfileBox.Items.Add(name);
        }

        Panel.SessionFactory = CreateSession;
        ProfileBox.SelectedItem = ProfileBox.Items.Contains(context.Settings.HarnessProfile) ? context.Settings.HarnessProfile : ProfileBox.Items.Cast<object>().FirstOrDefault();
        if (ProfileBox.Items.Count == 0)
        {
            HintText.Text = "Nie znalazłem folderu z profilami (profile\\<nazwa>\\profil.json) obok programu ani wyżej.";
        }

        Closed += (_, _) =>
        {
            Panel.NewSession();
            _llm?.Dispose();
            SaveChoices();
        };
    }

    internal string? ProfileName => ProfileBox.SelectedItem as string;

    /// <summary>Kontrola bez klikania: polecenia zmieniające są zatwierdzane bez okna pytania.</summary>
    internal bool AutoConfirm { get; set; }

    /// <summary>Do kontroli bez klikania: wysyła polecenia po kolei.</summary>
    internal async Task RunAsync(IEnumerable<string> commands)
    {
        foreach (var command in commands)
        {
            await Panel.SendAsync(command);
        }
    }

    internal void SavePng(string path) => Snapshot.Save(Root, path);

    private HarnessSession CreateSession()
    {
        var folder = System.IO.Path.Combine(_context.ProfilesRoot, ProfileName ?? "");
        var profile = HarnessProfile.Load(folder);
        _llm ??= new OllamaLlmClient(new OllamaOptions
        {
            Url = _context.OllamaUrl,
            Model = _context.Model,
            ContextLength = _context.ContextLength,
            Temperature = _context.Temperature,
            KeepAlive = "10m",
        });

        var options = new SessionOptions();
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
            options.ConfirmMutation = AutoConfirm ? (c, t) => Task.FromResult(true) : Panel.ConfirmMutationAsync;
            options.ContextNote = "Komputer z Windows, użytkownik " + Environment.UserName + ". Folder bieżący na start: " + executorFiles.WorkingFolder
                + ". Dozwolone foldery: " + string.Join("; ", roots.Select(Environment.ExpandEnvironmentVariables)) + ". Dziś jest " + DateTime.Today.ToString("yyyy-MM-dd") + ".";
        }
        else if (BridgeRadio.IsChecked == true)
        {
            executor = new TeklaBridgeExecutor(new TeklaBridgeOptions { ExchangeFolder = ExchangeBox.Text.Trim(), ProfileFolder = folder });
            options.ConfirmMutation = AutoConfirm ? (c, t) => Task.FromResult(true) : Panel.ConfirmMutationAsync;
        }
        else
        {
            executor = new SimulatedModelExecutor();
            options.ContextNote = "Model Tekli jest symulowany (próba bez Tekli).";
        }

        return new HarnessSession(_llm, profile, executor, options);
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
                  + "Polecenia, które tylko czytają, idą od razu; zmiany i uruchamianie programów zatwierdzasz w okienku."
            : isFiles
                ? "Przykład: „cd C:\\ODS\\Projekty, a potem utwórz folder Raporty”, „co jest w tym folderze?”, „otwórz go”. Kasowania nie ma; zmiany wymagają potwierdzenia."
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

    private string ProfileFolderPath => System.IO.Path.Combine(_context.ProfilesRoot, ProfileName ?? "");

    private static string RepoCache => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LokalneAI", "repo");

    /// <summary>Sama aktualizacja wiedzy, gdy profil ma źródła, a od ostatniej minęło ponad 6 godzin.</summary>
    private async Task AutoSyncAsync()
    {
        var folder = ProfileFolderPath;
        if (KnowledgeSync.ReadSources(folder).Count == 0)
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

        new SourcesWindow(ProfileName, ProfileFolderPath, RepoCache) { Owner = this }.ShowDialog();
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
        if (dialog.ShowDialog(this) == true)
        {
            ConnectionBox.Text = dialog.FileName;
            Panel.NewSession();
        }
    }

    private void SaveChoices()
    {
        _context.Settings.HarnessProfile = ProfileName;
        _context.Settings.TeklaExchangeFolder = ExchangeBox.Text.Trim();
        _context.Settings.CdeConnectionFile = ConnectionBox.Text.Trim();
        _context.Settings.CdePersonId = PersonBox.Text.Trim();
        _context.Settings.FileRoots = RootsBox.Text.Trim();
        _context.Save?.Invoke();
    }

    private static string ConnectionFromJson(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var json = document.RootElement;
        string? Get(string name) => json.TryGetProperty(name, out var v) ? (v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText()) : null;
        return $"Host={Get("host")};Port={Get("port") ?? "5432"};Database={Get("dbname")};Username={Get("user")};Password={Get("password")};Timeout=15;Command Timeout=30;Application Name=LokalneAI";
    }
}

/// <summary>Co okno pół-harnessu bierze z okna głównego.</summary>
public sealed class HarnessContext
{
    public string OllamaUrl { get; init; } = "";

    public string Model { get; init; } = "";

    public int ContextLength { get; init; }

    public double Temperature { get; init; }

    public string ProfilesRoot { get; init; } = "";

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
            var candidate = System.IO.Path.Combine(dir.FullName, "profile");
            if (Directory.Exists(candidate) && Directory.GetFiles(candidate, "profil.json", SearchOption.AllDirectories).Length > 0)
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return System.IO.Path.Combine(AppContext.BaseDirectory, "profile");
    }

    public static IEnumerable<string> List(string root)
        => Directory.Exists(root)
            ? Directory.GetDirectories(root).Where(d => File.Exists(System.IO.Path.Combine(d, "profil.json"))).Select(d => System.IO.Path.GetFileName(d)).OrderBy(n => n)
            : Enumerable.Empty<string>();
}
