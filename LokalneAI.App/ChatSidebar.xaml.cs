using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LokalneAI.Core.Storage;

namespace LokalneAI.App;

/// <summary>
/// Panel rozmów zakładki Czat (jak w Code): grupy i rozmowy z <c>Dokumenty\Amadeusz\Czat\</c>. Rozmowa zapisuje się po każdej
/// odpowiedzi; kliknięcie wczytuje ją do okna (z „Pamiętaj rozmowę” model dostaje też jej wcześniejsze pytania i odpowiedzi).
/// × i „Usuń” przenoszą do <c>Czat\kosz\</c>, skąd program kasuje rozmowy po <see cref="ConversationTrash.KeepDays"/> dniach.
/// </summary>
public partial class ChatSidebar : UserControl
{
    private readonly ObservableCollection<ConversationGroupNode> _groups = [];
    private MainViewModel? _viewModel;
    private string _root = ChatConversationStore.DefaultRoot;
    private CodeConversation _current = NewConversation();
    private ConversationNode? _currentNode;
    private ConversationGroupNode? _currentGroup;

    public ChatSidebar()
    {
        InitializeComponent();
        ConversationTree.ItemsSource = _groups;
    }

    /// <summary>Zapisywać rozmowy (kontrola bez klikania — tylko z <c>--rozmowy</c>).</summary>
    public bool Persist { get; set; } = true;

    /// <summary>Wiąże panel z oknem: zapis po każdej odpowiedzi, „Wyczyść rozmowę” = nowa rozmowa.</summary>
    public void Attach(MainViewModel viewModel, string? root = null)
    {
        _viewModel = viewModel;
        _root = root ?? ChatConversationStore.DefaultRoot;
        viewModel.ChatTurnEnded += (_, _) => SaveCurrent();
        viewModel.ChatCleared += (_, _) => StartNew(_currentGroup ?? _groups.FirstOrDefault());
        LoadGroups();
    }

    /// <summary>Kontrola bez klikania: rozmowy tylko w podanym folderze (z <c>--rozmowy</c>); bez niego — nic się nie zapisuje.</summary>
    internal void ConfigureForTest(string? folder)
    {
        Persist = folder is not null;
        if (folder is not null)
        {
            _root = Path.GetFullPath(folder);
            LoadGroups();
        }
    }

    private static CodeConversation NewConversation() => new() { Created = DateTime.Now, Updated = DateTime.Now };

    private void LoadGroups()
    {
        ChatConversationStore.PurgeTrash(_root);
        _groups.Clear();
        _groups.Add(new ConversationGroupNode("Niezgrupowane", _root, true, ConversationFolders.Chat));
        foreach (var (name, folder) in ChatConversationStore.Groups(_root))
        {
            _groups.Add(new ConversationGroupNode(name, folder, false, ConversationFolders.Chat));
        }

        foreach (var group in _groups)
        {
            group.Reload();
        }

        _currentGroup = _groups[0];
    }

    /// <summary>Zapisuje bieżącą rozmowę (po każdej odpowiedzi). Pusta rozmowa się nie zapisuje.</summary>
    public void SaveCurrent()
    {
        if (!Persist || _viewModel is null || _viewModel.Exchanges.Count == 0)
        {
            return;
        }

        var group = _currentGroup ?? _groups[0];
        var exchanges = _viewModel.Exchanges.ToList();
        if (string.IsNullOrWhiteSpace(_current.Title))
        {
            var first = exchanges[0].Question.ReplaceLineEndings(" ").Trim();
            _current.Title = first.Length <= 60 ? first : first[..60].TrimEnd() + "…";
        }

        _current.Updated = DateTime.Now;
        _current.Model = exchanges[^1].Model;
        _current.Entries = exchanges.SelectMany(e => new[]
        {
            new CodeConversationEntry { Kind = "User", Text = e.Question, Detail = e.Model },
            new CodeConversationEntry { Kind = e.IsError ? "Error" : "Answer", Text = e.Answer, Detail = e.Stats, Success = e.Completed && !e.IsError },
        }).ToList();
        _current.History = exchanges.Where(e => e.Completed && !e.IsError).SelectMany(e => new[]
        {
            new CodeConversationMessage { Role = "user", Content = e.Question },
            new CodeConversationMessage { Role = "assistant", Content = e.Answer },
        }).ToList();
        try
        {
            ChatConversationStore.Save(group.Folder, _current);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            SidebarHint.Text = "Nie zapisałem rozmowy: " + e.Message;
            return;
        }

        if (_currentNode is null || !ReferenceEquals(_currentNode.Group, group))
        {
            _currentNode = new ConversationNode(group, _current.Id, _current.Title, _current.Updated);
            group.Conversations.Insert(0, _currentNode);
            group.IsExpanded = true;
        }
        else
        {
            _currentNode.Title = _current.Title;
            _currentNode.Updated = _current.Updated;
            var index = group.Conversations.IndexOf(_currentNode);
            if (index > 0)
            {
                group.Conversations.Move(index, 0);
            }
        }

        MarkCurrent(_currentNode);
    }

    private void StartNew(ConversationGroupNode? group)
    {
        _current = NewConversation();
        _currentNode = null;
        _currentGroup = group ?? _groups.FirstOrDefault();
        MarkCurrent(null);
        SidebarHint.Text = "Nowa rozmowa w grupie „" + (_currentGroup?.Name ?? "Niezgrupowane") + "”.";
    }

    private void MarkCurrent(ConversationNode? node)
    {
        foreach (var conversation in _groups.SelectMany(g => g.Conversations))
        {
            conversation.IsCurrent = ReferenceEquals(conversation, node);
        }
    }

    private void Open(ConversationNode node)
    {
        if (_viewModel is null || ReferenceEquals(node, _currentNode))
        {
            return;
        }

        if (_viewModel.IsBusy)
        {
            SidebarHint.Text = "Poczekaj na koniec odpowiedzi (albo Stop) — potem otworzysz inną rozmowę.";
            return;
        }

        var conversation = CodeConversationStore.Load(ChatConversationStore.PathFor(node.Group.Folder, node.Id));
        if (conversation is null)
        {
            SidebarHint.Text = "Nie mogę odczytać rozmowy „" + node.DisplayTitle + "” (plik usunięty albo uszkodzony).";
            node.Group.Reload();
            return;
        }

        var exchanges = new List<ExchangeViewModel>();
        for (var i = 0; i < conversation.Entries.Count; i++)
        {
            var entry = conversation.Entries[i];
            if (entry.Kind != "User")
            {
                continue;
            }

            var answer = i + 1 < conversation.Entries.Count && conversation.Entries[i + 1].Kind != "User" ? conversation.Entries[i + 1] : null;
            exchanges.Add(new ExchangeViewModel(entry.Text, entry.Detail ?? conversation.Model ?? "")
            {
                Answer = answer?.Text ?? "",
                Stats = answer?.Detail ?? "",
                IsError = answer?.Kind == "Error",
                Completed = answer?.Success == true,
            });
        }

        _viewModel.LoadChat(exchanges, node.DisplayTitle);
        _current = conversation;
        _currentNode = node;
        _currentGroup = node.Group;
        MarkCurrent(node);
        SidebarHint.Text = "";
    }

    private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        switch (e.NewValue)
        {
            case ConversationNode node:
                Open(node);
                break;
            case ConversationGroupNode group when _viewModel?.Exchanges.Count == 0:
                _currentGroup = group; // pusta rozmowa: następna trafi do zaznaczonej grupy
                SidebarHint.Text = "Nowa rozmowa w grupie „" + group.Name + "”.";
                break;
        }
    }

    private void NewConversation_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null || _viewModel.IsBusy)
        {
            return;
        }

        var group = ConversationTree.SelectedItem switch
        {
            ConversationGroupNode g => g,
            ConversationNode n => n.Group,
            _ => _currentGroup,
        };
        _viewModel.NewChat(); // → ChatCleared → StartNew(bieżąca grupa)
        StartNew(group);
    }

    private void AddGroup_Click(object sender, RoutedEventArgs e)
    {
        var name = TextPrompt.Ask(Window.GetWindow(this), "Nowa grupa", "Nazwa grupy rozmów (podfolder w " + _root + "):", "");
        if (name is null)
        {
            return;
        }

        try
        {
            var folder = ChatConversationStore.CreateGroup(_root, name);
            var existing = _groups.FirstOrDefault(g => string.Equals(g.Folder, folder, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                existing = new ConversationGroupNode(Path.GetFileName(folder), folder, false, ConversationFolders.Chat);
                _groups.Add(existing);
            }

            existing.IsSelected = true;
            if (_viewModel?.Exchanges.Count == 0)
            {
                _currentGroup = existing;
            }

            SidebarHint.Text = "Grupa „" + existing.Name + "”: " + folder;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            SidebarHint.Text = "Nie utworzyłem grupy: " + ex.Message;
        }
    }

    private void TrashButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement { DataContext: ConversationNode node })
        {
            Trash(node);
        }
    }

    private void Trash(ConversationNode node)
    {
        if (ReferenceEquals(node, _currentNode) && _viewModel?.IsBusy == true)
        {
            SidebarHint.Text = "Ta rozmowa jeszcze trwa — Stop albo poczekaj na odpowiedź.";
            return;
        }

        try
        {
            ChatConversationStore.MoveToTrash(_root, node.Group.Folder, node.Id);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            SidebarHint.Text = "Nie udało się usunąć: " + e.Message;
            return;
        }

        node.Group.Conversations.Remove(node);
        if (ReferenceEquals(node, _currentNode))
        {
            _currentNode = null;
            _viewModel?.NewChat();
        }

        SidebarHint.Text = "Rozmowa „" + node.DisplayTitle + "” jest w koszu (" + Path.Combine(_root, ChatConversationStore.TrashName)
            + ") — można ją stamtąd przywrócić przez " + ConversationTrash.KeepDays + " dni.";
    }

    private void TreeItem_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TreeViewItem item)
        {
            return;
        }

        // Zdarzenie „Preview” przychodzi najpierw do rodzica (grupy) — menu dla elementu najgłębiej pod kursorem.
        if (e.OriginalSource is DependencyObject source && FindItem(source) is { } deepest && !ReferenceEquals(deepest, item))
        {
            return;
        }

        e.Handled = true;
        item.IsSelected = item.DataContext is ConversationGroupNode || item.IsSelected;
        var menu = item.DataContext switch
        {
            ConversationNode node => ConversationMenu(node),
            ConversationGroupNode group => GroupMenu(group),
            _ => null,
        };
        if (menu is not null)
        {
            menu.PlacementTarget = item;
            menu.IsOpen = true;
        }
    }

    private static TreeViewItem? FindItem(DependencyObject source)
    {
        while (source is not null and not TreeViewItem)
        {
            source = VisualTreeHelper.GetParent(source);
        }

        return source as TreeViewItem;
    }

    private ContextMenu ConversationMenu(ConversationNode node)
    {
        var menu = new ContextMenu();
        menu.Items.Add(Item("Zmień nazwę…", () => Rename(node)));
        var move = new MenuItem { Header = "Przenieś do grupy" };
        foreach (var group in _groups.Where(g => !ReferenceEquals(g, node.Group)))
        {
            var target = group;
            move.Items.Add(Item(group.Name, () => Move(node, target)));
        }

        move.IsEnabled = move.Items.Count > 0;
        menu.Items.Add(move);
        menu.Items.Add(Item("Pokaż plik w Eksploratorze", () => Explorer("/select,\"" + ChatConversationStore.PathFor(node.Group.Folder, node.Id) + "\"")));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Usuń (do kosza na " + ConversationTrash.KeepDays + " dni)", () => Trash(node)));
        return menu;
    }

    private ContextMenu GroupMenu(ConversationGroupNode group)
    {
        var menu = new ContextMenu();
        menu.Items.Add(Item("Nowa rozmowa w tej grupie", () =>
        {
            if (_viewModel is { IsBusy: false })
            {
                _viewModel.NewChat();
                StartNew(group);
            }
        }));
        menu.Items.Add(Item("Otwórz folder", () => Explorer("\"" + group.Folder + "\"")));
        menu.Items.Add(Item("Otwórz kosz", () => Explorer("\"" + Path.Combine(_root, ChatConversationStore.TrashName) + "\"")));
        if (!group.IsUngrouped)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("Zmień nazwę…", () => RenameGroup(group)));
            menu.Items.Add(Item("Usuń grupę (rozmowy do kosza)", () => DeleteGroup(group)));
        }

        return menu;
    }

    private static MenuItem Item(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private void Explorer(string arguments)
    {
        try
        {
            Directory.CreateDirectory(Path.Combine(_root, ChatConversationStore.TrashName));
            Process.Start(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = true });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            SidebarHint.Text = e.Message;
        }
    }

    private void Rename(ConversationNode node)
    {
        var name = TextPrompt.Ask(Window.GetWindow(this), "Nazwa rozmowy", "Nowa nazwa rozmowy:", node.Title);
        if (name is null)
        {
            return;
        }

        if (ReferenceEquals(node, _currentNode))
        {
            _current.Title = name;
            node.Title = name;
            SaveCurrent();
            return;
        }

        if (CodeConversationStore.Load(ChatConversationStore.PathFor(node.Group.Folder, node.Id)) is { } conversation)
        {
            conversation.Title = name;
            ChatConversationStore.Save(node.Group.Folder, conversation);
            node.Title = name;
        }
    }

    private void Move(ConversationNode node, ConversationGroupNode target)
    {
        try
        {
            ChatConversationStore.Move(node.Group.Folder, target.Folder, node.Id);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            SidebarHint.Text = "Nie udało się przenieść: " + e.Message;
            return;
        }

        node.Group.Conversations.Remove(node);
        node.Group = target;
        target.Conversations.Insert(0, node);
        target.IsExpanded = true;
        if (ReferenceEquals(node, _currentNode))
        {
            _currentGroup = target;
        }

        SidebarHint.Text = "Przeniesiono do grupy „" + target.Name + "”.";
    }

    private void RenameGroup(ConversationGroupNode group)
    {
        var name = TextPrompt.Ask(Window.GetWindow(this), "Nazwa grupy", "Nowa nazwa grupy (zmieni się też nazwa folderu):", group.Name);
        if (name is null)
        {
            return;
        }

        try
        {
            group.Folder = ChatConversationStore.RenameGroup(group.Folder, name);
            group.Name = Path.GetFileName(group.Folder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            SidebarHint.Text = "Nie zmieniłem nazwy: " + e.Message;
        }
    }

    private void DeleteGroup(ConversationGroupNode group)
    {
        if (_viewModel?.IsBusy == true && ReferenceEquals(_currentGroup, group))
        {
            return;
        }

        var answer = MessageBox.Show(Window.GetWindow(this),
            "Usunąć grupę „" + group.Name + "”?\n\nJej rozmowy (" + group.Conversations.Count + ") trafią do kosza i będzie można je przywrócić przez "
            + ConversationTrash.KeepDays + " dni.", "Usuń grupę", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            ChatConversationStore.DeleteGroup(_root, group.Folder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            SidebarHint.Text = "Nie usunąłem grupy: " + e.Message;
            return;
        }

        if (ReferenceEquals(_currentGroup, group))
        {
            _viewModel?.NewChat();
            StartNew(_groups[0]);
        }

        _groups.Remove(group);
        SidebarHint.Text = "Grupa usunięta, rozmowy są w koszu.";
    }
}
