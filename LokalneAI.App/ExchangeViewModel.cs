using LokalneAI.Core;
using LokalneAI.Core.Ollama;

namespace LokalneAI.App;

/// <summary>Jedno pytanie z odpowiedzią w oknie rozmowy.</summary>
public sealed class ExchangeViewModel(string question, string model) : ObservableObject
{
    private string _answer = "";
    private string _stats = "Model liczy…";
    private bool _isError;

    public string Question { get; } = question;

    public string Model { get; } = model;

    /// <summary>Krótka nazwa modelu nad odpowiedzią, np. „bielik-11b-v3.0-instruct:Q4_K_M”.</summary>
    public string ModelLabel { get; } = ModelItem.ShortName(model);

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

/// <summary>Pozycja listy modeli.</summary>
public sealed class ModelItem(LocalModel model)
{
    public string Name { get; } = model.Name;

    public string Display { get; } = string.Format(
        System.Globalization.CultureInfo.GetCultureInfo("pl-PL"), "{0}   ({1:0.0} GB)", model.Name, model.Size / 1e9);

    public static string ShortName(string name)
    {
        var slash = name.LastIndexOf('/');
        return slash >= 0 ? name[(slash + 1)..] : name;
    }

    public override string ToString() => Display;
}
