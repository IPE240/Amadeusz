using System.Collections.ObjectModel;
using System.Windows;

namespace LokalneAI.App;

/// <summary>Model do wyboru w oknie preferowanych modeli: klucz (<see cref="Core.Storage.AppSettings.ModelKey"/>) i opis.</summary>
public sealed record ModelChoice(string Key, string Display)
{
    /// <summary>Krótka nazwa z klucza: bez dostawcy i bez przedrostka repozytorium (SpeakLeash/…).</summary>
    public static string ShortKey(string key)
    {
        var name = key.Contains('|') ? key[(key.IndexOf('|') + 1)..] : key;
        return ModelItem.ShortName(name);
    }

    public override string ToString() => Display;
}

/// <summary>
/// Preferowane modele profilu w kolejności od najlepszego (↑/↓). Lista modeli okna podpowiada, ale można wpisać dowolną
/// nazwę — model z innego komputera (np. większego), którego tu nie ma.
/// </summary>
public partial class PreferredModelsWindow : Window
{
    private readonly ObservableCollection<ModelChoice> _chosen = new();
    private readonly IReadOnlyList<ModelChoice> _available;

    public PreferredModelsWindow(string profileName, IEnumerable<string> keys, IReadOnlyList<ModelChoice> available)
    {
        InitializeComponent();
        Title = "Preferowane modele — profil " + profileName;
        _available = available;
        foreach (var key in keys)
        {
            _chosen.Add(Describe(key));
        }

        ChosenList.ItemsSource = _chosen;
        ChoiceBox.ItemsSource = available;
    }

    /// <summary>Wybrane klucze w kolejności (po OK).</summary>
    public List<string> Keys => _chosen.Select(c => c.Key).ToList();

    private ModelChoice Describe(string key)
        => _available.FirstOrDefault(a => string.Equals(a.Key, key, StringComparison.OrdinalIgnoreCase))
           ?? new ModelChoice(key, ModelChoice.ShortKey(key) + "  (nie ma na liście tego komputera)");

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var choice = ChoiceBox.SelectedItem as ModelChoice;
        var typed = ChoiceBox.Text?.Trim() ?? "";
        if (choice is null || choice.Display != typed)
        {
            choice = typed.Length == 0 ? null
                : _available.FirstOrDefault(a => string.Equals(a.Display, typed, StringComparison.OrdinalIgnoreCase)
                                                 || string.Equals(a.Key, typed, StringComparison.OrdinalIgnoreCase))
                  ?? Describe(typed);
        }

        if (choice is null || _chosen.Any(c => string.Equals(c.Key, choice.Key, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        _chosen.Add(choice);
        ChosenList.SelectedItem = choice;
        ChoiceBox.Text = "";
    }

    private void Up_Click(object sender, RoutedEventArgs e) => Move(-1);

    private void Down_Click(object sender, RoutedEventArgs e) => Move(1);

    private void Move(int delta)
    {
        var index = ChosenList.SelectedIndex;
        var target = index + delta;
        if (index < 0 || target < 0 || target >= _chosen.Count)
        {
            return;
        }

        _chosen.Move(index, target);
        ChosenList.SelectedIndex = target;
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (ChosenList.SelectedItem is ModelChoice choice)
        {
            _chosen.Remove(choice);
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
