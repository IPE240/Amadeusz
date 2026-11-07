using System.Collections.ObjectModel;
using System.Windows;
using LokalneAI.Harness.Tools;

namespace LokalneAI.App;

/// <summary>
/// Repozytoria i adresy, z których profil pobiera dokumentację (<c>zrodla.json</c>), bez ręcznej edycji JSON-a.
/// „Zapisz i pobierz” od razu ściąga pliki do <c>wiedza\zrodla\</c>; potem aktualizują się same co 6 godzin.
/// </summary>
public partial class SourcesWindow : Window
{
    private readonly string _profileFolder;
    private readonly string _cacheFolder;
    private readonly ObservableCollection<SourceRow> _rows = new();
    private readonly List<KnowledgeSource> _original;

    public SourcesWindow(string profileName, string profileFolder, string cacheFolder)
    {
        InitializeComponent();
        _profileFolder = profileFolder;
        _cacheFolder = cacheFolder;
        Title = "Źródła wiedzy — profil " + profileName;
        IntroText.Text = "Wklej adres repozytorium (ten sam co do „git clone”, np. https://github.com/Firma/Repo.git) i wzorce plików "
            + "z dokumentacją: README.md; docs/**/*.md (** = z podfolderami; puste pole = wszystkie pliki .md). Gałąź może zostać pusta. Pliki trafią do " + System.IO.Path.Combine(profileFolder, "wiedza", "zrodla")
            + ", a asystent przeszuka je, gdy czegoś nie wie. Repozytoria prywatne działają, jeśli git na tym komputerze ma do nich dostęp. "
            + "Pojedynczy plik z internetu: wklej jego adres i zostaw pole Pliki puste.";
        _original = KnowledgeSync.ReadSources(profileFolder).ToList();
        foreach (var source in _original)
        {
            _rows.Add(new SourceRow
            {
                Adres = source.Repo ?? source.Url ?? "",
                Galaz = source.Branch ?? "",
                Pliki = string.Join("; ", source.Files),
                Original = source,
            });
        }

        if (_rows.Count == 0)
        {
            _rows.Add(new SourceRow());
        }

        Grid.ItemsSource = _rows;
    }

    private void Add_Click(object sender, RoutedEventArgs e) => _rows.Add(new SourceRow());

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (Grid.SelectedItem is SourceRow row)
        {
            _rows.Remove(row);
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        Grid.CommitEdit();
        Grid.CommitEdit();
        var sources = _rows.Where(r => !string.IsNullOrWhiteSpace(r.Adres)).Select(ToSource).ToList();
        KnowledgeSync.WriteSources(_profileFolder, sources);
        if (sources.Count == 0)
        {
            StatusText.Text = "Zapisano: profil nie ma źródeł.";
            return;
        }

        SaveButton.IsEnabled = false;
        StatusText.Text = "Pobieram dokumentację…";
        try
        {
            var result = await Task.Run(() => KnowledgeSync.SyncAsync(_profileFolder, _cacheFolder));
            StatusText.Text = "Gotowe: " + result + ". Nowa sesja asystenta użyje nowych plików.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Zapisano, ale pobieranie nie powiodło się: " + ex.Message;
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }

    /// <summary>Wiersz z okna → źródło. Adres z wzorcami plików albo bez rozszerzenia pliku to repozytorium git; reszta to pojedynczy plik.</summary>
    internal static KnowledgeSource ToSource(SourceRow row)
    {
        var address = row.Adres.Trim();
        // Link do pliku na stronie GitHuba (…/blob/gałąź/plik) → surowy plik.
        var blob = System.Text.RegularExpressions.Regex.Match(address, @"^https://github\.com/([^/]+)/([^/]+)/blob/(.+)$");
        if (blob.Success)
        {
            address = "https://raw.githubusercontent.com/" + blob.Groups[1].Value + "/" + blob.Groups[2].Value + "/" + blob.Groups[3].Value;
        }

        var files = (row.Pliki ?? "").Split(new[] { ';', ',', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(f => f.Trim()).Where(f => f.Length > 0).ToList();
        var lastSegment = address.TrimEnd('/').Split('/').Last();
        var looksLikeFile = !address.EndsWith(".git", StringComparison.OrdinalIgnoreCase)
            && (address.Contains("raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase) || System.IO.Path.HasExtension(lastSegment))
            && files.Count == 0;
        var source = new KnowledgeSource
        {
            Type = looksLikeFile ? "url" : "git",
            Repo = looksLikeFile ? null : address,
            Url = looksLikeFile ? address : null,
            Branch = string.IsNullOrWhiteSpace(row.Galaz) ? null : row.Galaz.Trim(),
            TokenEnv = row.Original?.TokenEnv,
            Target = row.Original?.Target,
        };
        foreach (var file in files)
        {
            source.Files.Add(file);
        }

        return source;
    }
}

/// <summary>Wiersz tabeli źródeł.</summary>
public sealed class SourceRow
{
    public string Adres { get; set; } = "";

    public string Galaz { get; set; } = "";

    public string Pliki { get; set; } = "";

    /// <summary>Źródło z pliku (zachowuje pola, których okno nie pokazuje: token_env, cel).</summary>
    internal KnowledgeSource? Original { get; set; }
}
