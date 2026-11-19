using System.Windows.Media;
using LokalneAI.Core;
using LokalneAI.Core.Ollama;
using LokalneAI.Core.Storage;
using LokalneAI.Harness.Llm;

namespace LokalneAI.App;

/// <summary>Jedno pytanie z odpowiedzią w oknie rozmowy. <paramref name="cloud"/> = model w chmurze (etykieta w kolorze dostawcy).</summary>
public sealed class ExchangeViewModel(string question, string model, string? computer = null, ModelItem? cloud = null) : ObservableObject
{
    private string _answer = "";
    private string _stats = "Model liczy…";
    private bool _isError;

    public string Question { get; } = question;

    public string Model { get; } = model;

    /// <summary>Podpis pod pytaniem, np. „Obrazy dla modelu: 1”; null = pytanie bez obrazów.</summary>
    public string? ImagesNote { get; set; }


    /// <summary>Krótka nazwa modelu nad odpowiedzią, np. „bielik-11b-v3.0-instruct:Q4_K_M” albo „Claude Sonnet 5.5 · chmura Anthropic”.</summary>
    public string ModelLabel { get; } = cloud?.Cloud is { } provider
        ? cloud.Display + " · chmura " + provider.Name
        : ModelItem.ShortName(model) + (computer is null ? "" : " · liczy " + computer);

    /// <summary>Kolor etykiety: dostawcy w chmurze albo szary dla modeli lokalnych.</summary>
    public Brush? LabelBrush { get; } = cloud?.NameBrush;

    public string Answer
    {
        get => _answer;
        set => SetProperty(ref _answer, value);
    }

    public string Stats
    {
        get => _stats;
        set => SetProperty(ref _stats, value);
    }

    public bool IsError
    {
        get => _isError;
        set => SetProperty(ref _isError, value);
    }

    /// <summary>Odpowiedź skończona bez błędu i bez przerwania — tylko takie wracają do modelu w trybie „Pamiętaj rozmowę”.</summary>
    public bool Completed { get; set; }

    public ChatStats? StatsData { get; set; }
}

/// <summary>
/// Pozycja listy modeli: model z Ollamy (ten komputer albo inny w sieci) albo model w chmurze dodany w Ustawieniach.
/// Modele w chmurze mają kolor dostawcy: Claude pomarańczowy, GPT turkusowy, Gemini niebieski.
/// </summary>
public sealed class ModelItem : ObservableObject
{
    private static readonly System.Globalization.CultureInfo Pl = System.Globalization.CultureInfo.GetCultureInfo("pl-PL");
    private bool _isFavorite;

    public ModelItem(LocalModel model)
    {
        Name = model.Name;
        Display = model.Name;
        Detail = string.Format(Pl, "{0:0.0} GB", model.Size / 1e9);
        SeesImages = model.Capabilities?.Contains("vision") == true;
    }

    public ModelItem(CloudProvider provider, string id, string? name)
    {
        Cloud = provider;
        Name = id;
        Display = string.IsNullOrWhiteSpace(name) ? id : name;
        Detail = "chmura " + provider.Name;
        NameBrush = BrushFor(provider);
        SeesImages = true; // Claude, GPT i Gemini czytają obrazy
    }

    /// <summary>Nazwa modelu do zapytań (Ollama) albo id u dostawcy.</summary>
    public string Name { get; }

    /// <summary>Dostawca w chmurze; null = model z Ollamy.</summary>
    public CloudProvider? Cloud { get; }

    public string? Provider => Cloud?.Id;

    /// <summary>
    /// Czy model widzi obrazy: Ollama zgłasza <c>vision</c> w <c>/api/tags</c> (gemma4, qwen3.5, ministral — tak; Bielik — nie);
    /// modele w chmurze — tak. Wklejony obrazek i obraz tablicy idą do modelu tylko wtedy; inaczej sam tekst z OCR.
    /// </summary>
    public bool SeesImages { get; }

    public bool IsCloud => Cloud is not null;

    public string Display { get; }

    /// <summary>Szary dopisek po prawej: rozmiar albo „chmura Anthropic”.</summary>
    public string Detail { get; }

    /// <summary>Kolor nazwy; null = zwykły kolor tekstu (modele lokalne).</summary>
    public Brush? NameBrush { get; }

    public string Key => AppSettings.ModelKey(Provider, Name);

    /// <summary>Gwiazdka na liście (ustawia okno przy składaniu listy).</summary>
    public System.Windows.Input.ICommand? FavoriteCommand { get; set; }

    public bool IsFavorite
    {
        get => _isFavorite;
        set => SetProperty(ref _isFavorite, value);
    }

    /// <summary>Krótki opis do zakładki Code i paska stanu.</summary>
    public string Label => IsCloud ? Display : ShortName(Name);

    public static string ShortName(string name)
    {
        var slash = name.LastIndexOf('/');
        return slash >= 0 ? name[(slash + 1)..] : name;
    }

    public static Brush BrushFor(CloudProvider provider)
    {
        var brush = (Brush)new BrushConverter().ConvertFromString(provider.Color)!;
        brush.Freeze();
        return brush;
    }

    public override string ToString() => Display;
}
