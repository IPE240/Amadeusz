using System.Collections.ObjectModel;
using LokalneAI.Core.Storage;

namespace LokalneAI.App;

/// <summary>Gdzie leżą rozmowy grupy: zakładka Code — <c>&lt;folder&gt;\.amadeusz\</c>, Czat — sam folder grupy.</summary>
public sealed class ConversationFolders
{
    /// <summary>Code: <see cref="CodeConversationStore"/>.</summary>
    public static ConversationFolders Code { get; } = new(CodeConversationStore.List, CodeConversationStore.PathFor, CodeConversationStore.StoreFolder);

    /// <summary>Czat: <see cref="ChatConversationStore"/> (pliki wprost w folderze grupy).</summary>
    public static ConversationFolders Chat { get; } = new(ChatConversationStore.List, ChatConversationStore.PathFor, folder => folder);

    private ConversationFolders(Func<string, IReadOnlyList<CodeConversationInfo>> list, Func<string, string, string> pathFor, Func<string, string> storeFolder)
    {
        List = list;
        PathFor = pathFor;
        StoreFolder = storeFolder;
    }

    public Func<string, IReadOnlyList<CodeConversationInfo>> List { get; }

    public Func<string, string, string> PathFor { get; }

    public Func<string, string> StoreFolder { get; }
}

/// <summary>Grupa w panelu rozmów: w Code = folder roboczy (rozmowy w jego <c>.amadeusz\</c>), w Czacie = podfolder <c>Czat\</c>.</summary>
public sealed class ConversationGroupNode : ObservableObject
{
    private string _name;
    private string _folder;
    private bool _isExpanded = true;
    private bool _isSelected;

    public ConversationGroupNode(string name, string folder, bool isUngrouped, ConversationFolders? store = null)
    {
        _name = name;
        _folder = folder;
        IsUngrouped = isUngrouped;
        Store = store ?? ConversationFolders.Code;
        Conversations.CollectionChanged += (_, _) => OnPropertyChanged(nameof(CountText));
    }

    /// <summary>Magazyn rozmów grupy (Code albo Czat).</summary>
    public ConversationFolders Store { get; }

    /// <summary>Wywoływane po zwinięciu albo rozwinięciu (zapis w ustawieniach).</summary>
    public event EventHandler? ExpandedChanged;

    /// <summary>„Niezgrupowane” — zawsze pierwsza, nie da się jej usunąć z listy.</summary>
    public bool IsUngrouped { get; }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    /// <summary>Folder grupy (pełna ścieżka). Rozmowy: <c>Folder\.amadeusz\</c>.</summary>
    public string Folder
    {
        get => _folder;
        set
        {
            if (SetProperty(ref _folder, value))
            {
                OnPropertyChanged(nameof(Tooltip));
            }
        }
    }

    public string Tooltip => Folder + "\nRozmowy: " + Store.StoreFolder(Folder);

    public string CountText => Conversations.Count == 0 ? "" : Conversations.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public ObservableCollection<ConversationNode> Conversations { get; } = [];

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value))
            {
                ExpandedChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    /// <summary>Wczytuje listę rozmów z <c>.amadeusz\</c> (najnowsze pierwsze).</summary>
    public void Reload()
    {
        Conversations.Clear();
        foreach (var info in Store.List(Folder))
        {
            Conversations.Add(new ConversationNode(this, info.Id, info.Title, info.Updated));
        }
    }
}

/// <summary>Rozmowa w panelu rozmów.</summary>
public sealed class ConversationNode : ObservableObject
{
    private string _title;
    private DateTime _updated;
    private bool _isSelected;
    private bool _isCurrent;
    private bool _isExpanded;

    public ConversationNode(ConversationGroupNode group, string id, string title, DateTime updated)
    {
        Group = group;
        Id = id;
        _title = title;
        _updated = updated;
    }

    public ConversationGroupNode Group { get; set; }

    public string Id { get; }

    public string Title
    {
        get => _title;
        set
        {
            if (SetProperty(ref _title, value))
            {
                OnPropertyChanged(nameof(DisplayTitle));
                OnPropertyChanged(nameof(Tooltip));
            }
        }
    }

    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? "(bez tytułu)" : Title;

    public DateTime Updated
    {
        get => _updated;
        set
        {
            if (SetProperty(ref _updated, value))
            {
                OnPropertyChanged(nameof(Tooltip));
            }
        }
    }

    public string Tooltip => DisplayTitle + "\n" + Updated.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture)
        + "\n" + Group.Store.PathFor(Group.Folder, Id);

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    /// <summary>Rozmowa otwarta w oknie (pogrubiona).</summary>
    public bool IsCurrent
    {
        get => _isCurrent;
        set => SetProperty(ref _isCurrent, value);
    }

    /// <summary>Nieużywane (rozmowa nie ma dzieci); jest, bo ten sam styl elementu drzewa obsługuje grupy.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }
}
