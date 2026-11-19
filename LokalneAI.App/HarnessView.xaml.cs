using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LokalneAI.Core.Storage;
using LokalneAI.Harness.Llm;
using LokalneAI.Harness.Ocr;
using LokalneAI.Harness.Rfem;
using LokalneAI.Harness.Office;
using LokalneAI.Harness.Postgres;
using LokalneAI.Harness.Profiles;
using LokalneAI.Harness.Session;
using LokalneAI.Harness.Tools;
using LokalneAI.Harness.Wpf;
using Microsoft.Win32;

namespace LokalneAI.App;

/// <summary>
/// Zakładka Code: asystent, który wykonuje polecenia (Tekla, baza CDE, pliki, PowerShell) — to samo, co gospodarz
/// (np. most do Tekli) zrobi w kodzie: wybór profili, wykonawców, uprawnień i modelu, a potem gotowy <c>HarnessPanel</c>.
/// Z lewej panel rozmów: grupa = folder roboczy, a rozmowy grupy leżą wyłącznie w jej podfolderze <c>.amadeusz\</c>.
/// </summary>
public partial class HarnessView : UserControl
{
    private readonly List<ConversationGroupNode> _groups = [];
    private Func<HarnessContext>? _contextProvider;
    private HarnessContext _context = new();
    private ILlmClient? _llm;
    private volatile string _permission = "pytaj";

    /// <summary>Otwarta rozmowa (jeszcze bez pliku, dopóki nie padło pierwsze polecenie).</summary>
    private CodeConversation _current = NewConversation([]);

    private ConversationGroupNode? _currentGroup;
    private ConversationNode? _currentNode;
    private string _sessionModelKey = "";
    private bool _loading;

    /// <summary>Zapisywać rozmowy (kontrola bez klikania — tylko z <c>--rozmowy</c>).</summary>
    private bool _persist = true;

    /// <summary>Zapisywać wybory i grupy w ustawieniach programu (kontrola bez klikania — nie).</summary>
    private bool _saveSettings = true;

    /// <summary>Kontrola bez klikania: plik połączenia i person_id podane z wiersza poleceń (bez zapisu).</summary>
    private string? _testConnectionFile;

    private string? _testPerson;

    /// <summary>Profile (nazwane zestawy umiejętności) — lista z ustawień albo domyślna.</summary>
    private List<WorkProfileSetting> _workProfiles = [];

    /// <summary>Ostatnio wybrany profil — gdy kilka ma te same umiejętności, lista pokazuje ten.</summary>
    private string? _lastWorkProfile;

    public HarnessView()
    {
        InitializeComponent();
        Panel.SessionFactory = CreateSession;
        Panel.NewSessionText = "Nowa rozmowa";
        Panel.TurnEnded += (_, _) => SaveCurrent(newPrompt: true);
        // Rozmowa jest już zapisana po ostatnim poleceniu, a panel właśnie się wyczyścił — nie zapisujemy pustej.
        Panel.NewSessionClicked += (_, _) => StartNewConversation(_currentGroup, save: false);
        Panel.ImagePasted = OcrPastedImageAsync;
        ConversationTree.ItemsSource = _groups;
        // Lista umiejętności: kliknięcie w przycisk przy otwartej liście ma ją zamknąć, a nie otworzyć od nowa.
        SkillsPopup.Opened += (_, _) => SkillsButton.IsHitTestVisible = false;
        SkillsPopup.Closed += (_, _) => SkillsButton.IsHitTestVisible = true;
    }

    /// <summary>Zaznaczone profile, np. „tekla+cde” (do opisu i kontroli bez klikania).</summary>
    internal string? ProfileName => SelectedProfiles.Count == 0 ? null : string.Join("+", SelectedProfiles);

    /// <summary>Zaznaczone profile w kolejności listy.</summary>
    private List<string> SelectedProfiles => ProfilesPanel.Children.OfType<CheckBox>().Where(c => c.IsChecked == true).Select(c => (string)c.Tag).ToList();

    /// <summary>
    /// Profile, które naprawdę wejdą do sesji: „komputer” ma wszystkie polecenia „pliki”, a „pliki” w zasadach zakazują
    /// PowerShella — przy obu zostaje sam „komputer”.
    /// </summary>
    private List<string> EffectiveProfiles
    {
        get
        {
            var names = SelectedProfiles;
            if (names.Contains("komputer"))
            {
                names.Remove("pliki");
            }

            return names;
        }
    }

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
        Panel.ShowTokens = settings.ShowTokens;
        Panel.ShowModelSteps = settings.ShowSteps;

        var chosen = settings.HarnessProfiles ?? (settings.HarnessProfile is { } old ? [old] : []);
        _loading = true;
        StepsBox.IsChecked = settings.ShowSteps;
        ProfilesPanel.Children.Clear();
        foreach (var name in ProfilesFolder.List(_context.ProfilesRoot))
        {
            var box = new CheckBox
            {
                Content = name,
                Tag = name,
                Margin = new Thickness(0, 4, 0, 4),
                VerticalAlignment = VerticalAlignment.Center,
                IsChecked = chosen.Contains(name),
                ToolTip = CloudViewModel.SafeDescription(Path.Combine(_context.ProfilesRoot, name)),
            };
            box.Checked += Profile_Changed;
            box.Unchecked += Profile_Changed;
            ProfilesPanel.Children.Add(box);
        }

        _workProfiles = CopyWorkProfiles(settings.WorkProfiles);
        _loading = false;
        RefreshCdeText(); // profile z bazą bez poświadczenia są ukryte (odświeża też listę profili)
        _loading = true;
        if (SelectedProfiles.Count == 0 && ProfilesPanel.Children.OfType<CheckBox>().FirstOrDefault(c => c.Visibility == Visibility.Visible) is { } first)
        {
            first.IsChecked = true;
        }

        _loading = false;
        if (ProfilesPanel.Children.Count == 0)
        {
            HintText.Text = "Nie znalazłem folderu z umiejętnościami (profile\\<nazwa>\\profil.json) obok programu ani wyżej.";
        }

        LoadGroups();
        var last = _groups.FirstOrDefault(g => SameFolder(g.Folder, settings.CodeLastGroup)) ?? _groups[0];
        StartNewConversation(last, save: false);
        ApplyProfiles(syncKnowledge: true);
        RefreshModelText();
    }

    // ───────────────────────────── Rozmowy i grupy ─────────────────────────────

    private static CodeConversation NewConversation(List<string> profiles) => new()
    {
        Id = CodeConversationStore.NewId(),
        Created = DateTime.Now,
        Updated = DateTime.Now,
        Profiles = profiles,
    };

    private static bool SameFolder(string? a, string? b)
        => !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b)
            && string.Equals(Path.TrimEndingDirectorySeparator(a.Trim()), Path.TrimEndingDirectorySeparator(b.Trim()), StringComparison.OrdinalIgnoreCase);

    private static string FullFolder(string folder)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(CodeConversationStore.Expand(folder));
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return folder;
        }
    }

    /// <summary>Grupy z ustawień: „Niezgrupowane” (Dokumenty\Amadeusz albo wskazany folder) i foldery dodane przez użytkownika.</summary>
    private void LoadGroups()
    {
        var settings = _context.Settings;
        _groups.Clear();
        var ungrouped = new ConversationGroupNode("Niezgrupowane", FullFolder(settings.CodeUngroupedFolder ?? CodeConversationStore.DefaultUngroupedFolder), true)
        {
            IsExpanded = !settings.CodeUngroupedCollapsed,
        };
        _groups.Add(ungrouped);
        foreach (var group in settings.CodeGroups)
        {
            _groups.Add(new ConversationGroupNode(group.Name, FullFolder(group.Folder), false) { IsExpanded = !group.Collapsed });
        }

        foreach (var group in _groups)
        {
            ReloadGroup(group);
            group.ExpandedChanged += (_, _) => SaveGroups();
        }

        ConversationTree.Items.Refresh();
    }

    private void ReloadGroup(ConversationGroupNode group)
    {
        try
        {
            group.Reload();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            SidebarHint.Text = "Nie mogę odczytać rozmów grupy „" + group.Name + "”: " + e.Message;
        }

        if (_currentNode is not null && ReferenceEquals(_currentNode.Group, group))
        {
            _currentNode = group.Conversations.FirstOrDefault(c => c.Id == _current.Id);
            if (_currentNode is not null)
            {
                _currentNode.IsCurrent = true;
            }
        }
    }

    /// <summary>Zapisuje listę grup (same foldery i nazwy) w ustawieniach programu.</summary>
    private void SaveGroups()
    {
        if (!_saveSettings)
        {
            return;
        }

        var settings = _context.Settings;
        settings.CodeUngroupedCollapsed = !_groups[0].IsExpanded;
        settings.CodeGroups = _groups.Where(g => !g.IsUngrouped)
            .Select(g => new CodeGroupSetting { Name = g.Name, Folder = g.Folder, Collapsed = !g.IsExpanded })
            .ToList();
        _context.Save?.Invoke();
    }

    /// <summary>Nowa, pusta rozmowa w grupie (plik powstanie przy pierwszym poleceniu).</summary>
    private void StartNewConversation(ConversationGroupNode? group, bool save = true)
    {
        if (save)
        {
            SaveCurrent();
        }

        if (Panel.Entries.Count > 0)
        {
            Panel.NewSession();
        }
        else
        {
            Panel.ResetSession();
        }

        SetCurrentNode(null);
        ClearTreeSelection();
        _currentGroup = group ?? _groups.FirstOrDefault();
        _current = NewConversation(EffectiveProfiles);
        Panel.Title = "Nowa rozmowa";
        if (_currentGroup is not null && _saveSettings)
        {
            _context.Settings.CodeLastGroup = _currentGroup.IsUngrouped ? null : _currentGroup.Folder;
        }

        RefreshWorkFolderText();
        SidebarHint.Text = _currentGroup is null ? "" : "Nowa rozmowa w grupie „" + _currentGroup.Name + "”.";
    }

    private void SetCurrentNode(ConversationNode? node)
    {
        if (_currentNode is not null)
        {
            _currentNode.IsCurrent = false;
        }

        _currentNode = node;
        if (node is not null)
        {
            node.IsCurrent = true;
        }
    }

    /// <summary>Wpisy panelu do pliku rozmowy (bez „Model pracuje…”), z przebiegiem podagentów jako wpisami podrzędnymi.</summary>
    private static List<CodeConversationEntry> ToStored(IEnumerable<PanelEntry> entries)
        => entries
            .Where(e => !(e.Kind == PanelEntryKind.Info && (e.Text ?? "").StartsWith("Model pracuje", StringComparison.Ordinal)))
            .Select(e => new CodeConversationEntry
            {
                Kind = e.Kind.ToString(),
                Text = e.Text ?? "",
                Detail = e.Detail,
                Success = e.Success,
                Metrics = e.Metrics,
                Children = e.HasChildren ? ToStored(e.Children) : null,
            })
            .ToList();

    /// <summary>Wpis z pliku rozmowy do panelu (nieznany rodzaj z nowszej wersji = informacja), z wpisami podrzędnymi.</summary>
    private static PanelEntry FromStored(CodeConversationEntry e)
    {
        var entry = new PanelEntry(Enum.TryParse<PanelEntryKind>(e.Kind, out var kind) ? kind : PanelEntryKind.Info, e.Text, e.Detail)
        {
            Success = e.Success,
            Metrics = e.Metrics,
        };
        foreach (var child in e.Children ?? [])
        {
            entry.Children.Add(FromStored(child));
        }

        return entry;
    }

    /// <summary>Historia bieżącej sesji (to, co dostaje model) do pliku rozmowy — przed zamknięciem albo przebudową sesji.</summary>
    private void CaptureHistory()
    {
        if (Panel.Session is { } session)
        {
            _current.History = session.History
                .Where(m => m.Role is "user" or "assistant")
                .Select(m => new CodeConversationMessage { Role = m.Role, Content = m.Content })
                .ToList();
        }
    }

    /// <summary>
    /// Zapisuje otwartą rozmowę w <c>&lt;folder grupy&gt;\.amadeusz\</c>. Pusta rozmowa nie powstaje. Na górę listy idzie tylko
    /// po nowym poleceniu (<paramref name="newPrompt"/>); zapis przy przejściu do innej rozmowy, zmianie nazwy albo przeniesieniu
    /// nie zmienia kolejności (lista sortuje się po <see cref="CodeConversation.LastPrompt"/>).
    /// </summary>
    private void SaveCurrent(bool newPrompt = false)
    {
        if (!_persist || _currentGroup is null)
        {
            return;
        }

        CaptureHistory();
        var entries = ToStored(Panel.Entries);
        if (entries.Count == 0)
        {
            return; // pusta rozmowa nie powstaje, a zapisanej nie nadpisujemy pustą
        }

        _current.Entries = entries;
        _current.Profiles = EffectiveProfiles;
        _current.Model = _context.ModelLabel;
        if (newPrompt)
        {
            _current.LastPrompt = DateTime.Now;
        }
        else if (_current.LastPrompt is null && _current.Updated != default)
        {
            _current.LastPrompt = _current.Updated; // plik sprzed tej zmiany: zostaje na swoim miejscu
        }

        _current.Updated = DateTime.Now;
        if (string.IsNullOrWhiteSpace(_current.Title))
        {
            var first = entries.FirstOrDefault(e => e.Kind == nameof(PanelEntryKind.User))?.Text ?? "";
            first = string.Join(" ", first.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            _current.Title = first.Length > 60 ? first[..60].TrimEnd() + "…" : first;
        }

        try
        {
            CodeConversationStore.Save(_currentGroup.Folder, _current);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            SidebarHint.Text = "Nie udało się zapisać rozmowy w " + CodeConversationStore.StoreFolder(_currentGroup.Folder) + ": " + e.Message;
            return;
        }

        if (_currentNode is null || !ReferenceEquals(_currentNode.Group, _currentGroup))
        {
            var node = new ConversationNode(_currentGroup, _current.Id, _current.Title, _current.Activity);
            _currentGroup.InsertSorted(node);
            SetCurrentNode(node);
        }
        else
        {
            _currentNode.Title = _current.Title;
            _currentNode.Updated = _current.Activity;
            var index = _currentGroup.Conversations.IndexOf(_currentNode);
            if (newPrompt && index > 0)
            {
                _currentGroup.Conversations.Move(index, 0);
            }
        }

        Panel.Title = _current.Title;
    }

    /// <summary>Otwiera zapisaną rozmowę: jej wpisy, profile i historię dla modelu (sesja powstanie przy następnym poleceniu).</summary>
    private void OpenConversation(ConversationNode node)
    {
        if (_currentNode == node)
        {
            return;
        }

        if (Panel.IsBusy)
        {
            SidebarHint.Text = "Asystent jeszcze pracuje — poczekaj albo kliknij Stop, potem otwórz inną rozmowę.";
            return;
        }

        SaveCurrent();
        var conversation = CodeConversationStore.Load(CodeConversationStore.PathFor(node.Group.Folder, node.Id));
        if (conversation is null)
        {
            SidebarHint.Text = "Nie mogę odczytać rozmowy „" + node.DisplayTitle + "” (plik usunięty albo uszkodzony).";
            ReloadGroup(node.Group);
            return;
        }

        _current = conversation;
        _currentGroup = node.Group;
        SetCurrentNode(node);
        if (conversation.Profiles.Count > 0)
        {
            _loading = true;
            foreach (var box in ProfilesPanel.Children.OfType<CheckBox>())
            {
                box.IsChecked = conversation.Profiles.Contains((string)box.Tag);
            }

            _loading = false;
        }

        ApplyProfiles(syncKnowledge: true);
        Panel.LoadEntries(conversation.Entries.Select(FromStored));
        Panel.Title = node.DisplayTitle;
        RefreshWorkFolderText();
        SidebarHint.Text = "";
        RefreshModelText();
    }

    private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_loading)
        {
            return;
        }

        if (e.NewValue is ConversationNode node)
        {
            OpenConversation(node);
        }
    }

    private void NewConversation_Click(object sender, RoutedEventArgs e)
    {
        var group = ConversationTree.SelectedItem switch
        {
            ConversationGroupNode g => g,
            ConversationNode c => c.Group,
            _ => _currentGroup,
        };
        StartNewConversation(group);
        ClearTreeSelection();
        Panel.FocusInput();
    }

    private void ClearTreeSelection()
    {
        _loading = true;
        foreach (var group in _groups)
        {
            group.IsSelected = false;
            foreach (var conversation in group.Conversations)
            {
                conversation.IsSelected = false;
            }
        }

        _loading = false;
    }

    private void AddGroup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Folder roboczy nowej grupy (rozmowy zapiszą się w jego podfolderze .amadeusz)" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        var folder = FullFolder(dialog.FolderName);
        var existing = _groups.FirstOrDefault(g => SameFolder(g.Folder, folder));
        if (existing is null)
        {
            existing = new ConversationGroupNode(Path.GetFileName(folder) is { Length: > 0 } name ? name : folder, folder, false);
            existing.ExpandedChanged += (_, _) => SaveGroups();
            _groups.Add(existing);
            ReloadGroup(existing);
            ConversationTree.Items.Refresh();
            SaveGroups();
        }

        StartNewConversation(existing);
        ClearTreeSelection();
        _loading = true;
        existing.IsSelected = true;
        _loading = false;
        SidebarHint.Text = "Grupa „" + existing.Name + "”: asystent pracuje w " + folder + ", rozmowy zapisuje w " + CodeConversationStore.StoreFolder(folder) + ".";
    }

    private void TreeItem_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TreeViewItem item || item.DataContext is null)
        {
            return;
        }

        // Zdarzenie przechodzi przez wszystkie poziomy drzewa — menu tylko dla elementu pod kursorem.
        if (e.OriginalSource is DependencyObject source && FindItem(source) != item)
        {
            return;
        }

        e.Handled = true;
        var menu = item.DataContext switch
        {
            ConversationGroupNode group => GroupMenu(group),
            ConversationNode conversation => ConversationMenu(conversation),
            _ => null,
        };
        if (menu is not null)
        {
            menu.PlacementTarget = item;
            menu.IsOpen = true;
        }
    }

    private static TreeViewItem? FindItem(DependencyObject? element)
    {
        while (element is not null and not TreeViewItem)
        {
            element = element is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
        }

        return element as TreeViewItem;
    }

    private static MenuItem Item(string header, Action action, bool enabled = true)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled };
        item.Click += (_, _) => action();
        return item;
    }

    private ContextMenu GroupMenu(ConversationGroupNode group)
    {
        var menu = new ContextMenu();
        menu.Items.Add(Item("Nowa rozmowa w tej grupie", () =>
        {
            StartNewConversation(group);
            ClearTreeSelection();
            Panel.FocusInput();
        }));
        menu.Items.Add(Item("Otwórz folder w Eksploratorze", () => OpenInExplorer(group.Folder)));
        menu.Items.Add(new Separator());
        if (group.IsUngrouped)
        {
            menu.Items.Add(Item("Zmień folder rozmów niezgrupowanych…", () => ChangeUngroupedFolder(group)));
        }
        else
        {
            menu.Items.Add(Item("Zmień nazwę…", () =>
            {
                var name = TextPrompt.Ask(Window.GetWindow(this), "Nazwa grupy", "Nazwa grupy w panelu (folder zostaje ten sam: " + group.Folder + "):", group.Name);
                if (name is not null)
                {
                    group.Name = name;
                    SaveGroups();
                }
            }));
            menu.Items.Add(Item("Usuń z listy (pliki zostają w folderze)", () => RemoveGroup(group)));
        }

        return menu;
    }

    private ContextMenu ConversationMenu(ConversationNode conversation)
    {
        var menu = new ContextMenu();
        menu.Items.Add(Item("Zmień nazwę…", () => RenameConversation(conversation)));
        var move = new MenuItem { Header = "Przenieś do grupy" };
        foreach (var group in _groups.Where(g => !ReferenceEquals(g, conversation.Group)))
        {
            var target = group;
            move.Items.Add(Item(group.Name, () => MoveConversation(conversation, target), !Panel.IsBusy));
        }

        move.IsEnabled = move.Items.Count > 0;
        menu.Items.Add(move);
        menu.Items.Add(Item("Pokaż plik w Eksploratorze", () => OpenInExplorer(CodeConversationStore.PathFor(conversation.Group.Folder, conversation.Id), select: true)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Usuń (do kosza na " + ConversationTrash.KeepDays + " dni)", () => TrashConversation(conversation), !(Panel.IsBusy && conversation == _currentNode)));
        return menu;
    }

    private static void OpenInExplorer(string path, bool select = false)
    {
        try
        {
            if (!select)
            {
                Directory.CreateDirectory(path);
            }

            Process.Start(new ProcessStartInfo("explorer.exe", select ? "/select,\"" + path + "\"" : "\"" + path + "\"") { UseShellExecute = true });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(e.Message, "Eksplorator");
        }
    }

    private void ChangeUngroupedFolder(ConversationGroupNode group)
    {
        var dialog = new OpenFolderDialog { Title = "Folder rozmów niezgrupowanych (rozmowy zapiszą się w jego podfolderze .amadeusz)", InitialDirectory = Directory.Exists(group.Folder) ? group.Folder : "" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        SaveCurrent();
        var old = group.Folder;
        group.Folder = FullFolder(dialog.FolderName);
        _context.Settings.CodeUngroupedFolder = group.Folder;
        _context.Save?.Invoke();
        ReloadGroup(group);
        if (ReferenceEquals(_currentGroup, group))
        {
            StartNewConversation(group, save: false);
        }

        SidebarHint.Text = "Rozmowy niezgrupowane: " + CodeConversationStore.StoreFolder(group.Folder)
            + ". Wcześniejsze zostały w " + CodeConversationStore.StoreFolder(old) + ".";
    }

    private void RemoveGroup(ConversationGroupNode group)
    {
        var answer = MessageBox.Show(Window.GetWindow(this),
            "Usunąć grupę „" + group.Name + "” z panelu?\n\nNic nie zostanie skasowane: rozmowy zostają w\n" + CodeConversationStore.StoreFolder(group.Folder)
            + "\ni wrócą, gdy znów dodasz ten folder jako grupę.", "Usuń grupę z listy", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        if (ReferenceEquals(_currentGroup, group))
        {
            StartNewConversation(_groups[0]);
        }

        _groups.Remove(group);
        ConversationTree.Items.Refresh();
        SaveGroups();
    }

    private void RenameConversation(ConversationNode node)
    {
        var name = TextPrompt.Ask(Window.GetWindow(this), "Nazwa rozmowy", "Nowa nazwa rozmowy:", node.Title);
        if (name is null)
        {
            return;
        }

        if (node == _currentNode)
        {
            _current.Title = name;
            Panel.Title = name;
            SaveCurrent();
            return;
        }

        var path = CodeConversationStore.PathFor(node.Group.Folder, node.Id);
        var conversation = CodeConversationStore.Load(path);
        if (conversation is null)
        {
            return;
        }

        conversation.Title = name;
        CodeConversationStore.Save(node.Group.Folder, conversation);
        node.Title = name;
    }

    private void MoveConversation(ConversationNode node, ConversationGroupNode target)
    {
        try
        {
            if (node == _currentNode)
            {
                SaveCurrent();
            }

            CodeConversationStore.Move(node.Group.Folder, target.Folder, node.Id);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            SidebarHint.Text = "Nie udało się przenieść rozmowy: " + e.Message;
            return;
        }

        node.Group.Conversations.Remove(node);
        node.Group = target;
        target.InsertSorted(node);
        target.IsExpanded = true;
        if (node == _currentNode)
        {
            // Folder roboczy rozmowy się zmienił — następne polecenie dostanie nowy folder i dozwoloną ścieżkę.
            _currentGroup = target;
            RebuildSession();
            RefreshWorkFolderText();
            Panel.AddInfo("Rozmowa przeniesiona do grupy „" + target.Name + "” — od teraz folder roboczy: " + target.Folder);
        }

        SidebarHint.Text = "Przeniesiono do grupy „" + target.Name + "”.";
    }

    private void TrashButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement { DataContext: ConversationNode node } && !(Panel.IsBusy && node == _currentNode))
        {
            TrashConversation(node);
        }
    }

    private void TrashConversation(ConversationNode node)
    {
        try
        {
            CodeConversationStore.MoveToTrash(node.Group.Folder, node.Id);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            SidebarHint.Text = "Nie udało się przenieść do kosza: " + e.Message;
            return;
        }

        var group = node.Group;
        group.Conversations.Remove(node);
        if (node == _currentNode)
        {
            _currentNode = null;
            StartNewConversation(group, save: false);
        }

        SidebarHint.Text = "Rozmowa „" + node.DisplayTitle + "” jest w koszu (" + CodeConversationStore.TrashFolder(group.Folder)
            + ") — można ją stamtąd przywrócić przez " + ConversationTrash.KeepDays + " dni, potem program ją skasuje.";
    }

    /// <summary>
    /// Folder roboczy rozmowy = folder jej grupy (tworzony, gdy go nie ma). Kontrola bez klikania: pierwszy istniejący
    /// z dozwolonych folderów, żeby niczego nie tworzyć.
    /// </summary>
    private string? WorkFolder(IReadOnlyList<string> roots)
    {
        if (!_persist || _currentGroup is null)
        {
            return roots.Select(Environment.ExpandEnvironmentVariables).FirstOrDefault(Directory.Exists);
        }

        Directory.CreateDirectory(_currentGroup.Folder);
        return _currentGroup.Folder;
    }

    private void RefreshWorkFolderText()
    {
        WorkFolderText.Text = _currentGroup is null || !_persist
            ? ""
            : "Folder rozmowy (grupa „" + _currentGroup.Name + "”): " + _currentGroup.Folder + " — dozwolony zawsze i bieżący na start.";
    }

    // ───────────────────────────── Model, profile, sesja ─────────────────────────────

    /// <summary>Odświeża opis modelu i komputera, który liczy (po zmianie w nagłówku albo w Ustawieniach), i ostrzeżenie o chmurze.</summary>
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

        // Inny model w trakcie rozmowy: następne polecenie pójdzie do nowego, z całą dotychczasową rozmową.
        if (Panel.Session is not null && !Panel.IsBusy && _sessionModelKey != ModelKey())
        {
            RebuildSession();
            Panel.AddInfo("Od teraz model: " + _context.ModelLabel + " (rozmowa trwa dalej).");
        }
    }

    private string ModelKey() => (_context.Cloud?.Id ?? "") + "|" + _context.Model + "|" + _context.OllamaUrl + "|" + _context.ContextLength + "|" + _context.Temperature;

    /// <summary>Ustawienia zaznaczonych profili (bez wiedzy) — do opisów przed sesją.</summary>
    private List<ProfileSettings> SelectedSettings
        => EffectiveProfiles.Select(n => CloudViewModel.SafeSettings(Path.Combine(_context.ProfilesRoot, n)) ?? new ProfileSettings()).ToList();

    /// <summary>„internet: tak” albo dlaczego nie (przełącznik w oknie, pole „internet” w profil.json — wszystkie zaznaczone profile).</summary>
    private string InternetText()
    {
        if (_context.Web is null)
        {
            return "internet: zablokowany";
        }

        var names = EffectiveProfiles;
        var blocked = names.Where(n => CloudViewModel.SafeSettings(Path.Combine(_context.ProfilesRoot, n))?.Internet != true).ToList();
        return names.Count == 0 ? "internet: —"
            : blocked.Count == 0 ? "internet: tak"
            : names.Count == 1 ? "internet: nie w tej umiejętności"
            : "internet: nie (" + string.Join(", ", blocked) + ")";
    }

    /// <summary>
    /// Pasek pod nagłówkiem: dokąd idą dane przy modelu w chmurze, a gdy któryś profil na niego nie pozwala (pole „chmura”
    /// w profil.json) — ostrzeżenie. Sesja i tak odmówi (sprawdza to biblioteka), tu widać to przed wysłaniem.
    /// </summary>
    private void RefreshCloudText()
    {
        var names = EffectiveProfiles;
        if (_context.Cloud is not { } cloud || names.Count == 0)
        {
            CloudBar.Visibility = Visibility.Collapsed;
            return;
        }

        CloudBar.Visibility = Visibility.Visible;
        var refusing = names.Where(n => !(CloudViewModel.SafeSettings(Path.Combine(_context.ProfilesRoot, n)) ?? new ProfileSettings()).AllowsCloud(cloud.Id)).ToList();
        if (refusing.Count == 0)
        {
            CloudBar.BorderBrush = _context.ModelBrush ?? (Brush)FindResource("LineBrush");
            CloudText.Foreground = (Brush)FindResource("TextBrush");
            CloudText.Text = "Model w chmurze " + cloud.Name + ": polecenia, wyniki narzędzi i dokumentacja umiejętności " + string.Join(", ", names)
                + " idą przez internet do " + cloud.Name + ". Uprawnienia działają tak samo jak przy modelu lokalnym.";
        }
        else
        {
            var settings = CloudViewModel.SafeSettings(Path.Combine(_context.ProfilesRoot, refusing[0])) ?? new ProfileSettings();
            CloudBar.BorderBrush = (Brush)FindResource("BadBrush");
            CloudText.Foreground = (Brush)FindResource("BadBrush");
            CloudText.Text = "Umiejętność " + refusing[0] + ": " + CloudProvider.DescribeAllowed(settings.CloudProviders)
                + ". Model " + _context.ModelLabel + " (" + cloud.Name + ") tu nie zadziała — wybierz inny model na górze okna"
                + (names.Count > 1 ? " albo odznacz umiejętność " + string.Join(", ", refusing) + "." : ".");
        }
    }

    /// <summary>Plik otwartej rozmowy (null, dopóki nie zapisano) — do kontroli bez klikania.</summary>
    internal string? CurrentConversationPath
        => _currentNode is null ? null : CodeConversationStore.PathFor(_currentNode.Group.Folder, _currentNode.Id);

    /// <summary>
    /// Kontrola bez klikania: profile (np. „tekla+cde”) i ustawienia wykonawcy bez zapisu w ustawieniach użytkownika.
    /// Rozmowy zapisuje tylko z <paramref name="conversations"/> (folder grupy „Niezgrupowane” na czas kontroli);
    /// <paramref name="resume"/> otwiera najnowszą z nich.
    /// </summary>
    internal void ConfigureForTest(string profile, string roots, string? connection, string? person, string? conversations = null, bool resume = false)
    {
        _saveSettings = false;
        _persist = conversations is not null;
        RootsBox.Text = roots;
        _testConnectionFile = connection;
        _testPerson = person;
        var names = profile.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        _loading = true;
        foreach (var box in ProfilesPanel.Children.OfType<CheckBox>())
        {
            box.IsChecked = names.Contains((string)box.Tag);
        }

        _loading = false;
        if (conversations is not null)
        {
            _groups[0].Folder = FullFolder(conversations);
            ReloadGroup(_groups[0]);
        }

        StartNewConversation(_groups[0], save: false);
        ApplyProfiles(syncKnowledge: false);
        if (resume && _groups[0].Conversations.FirstOrDefault() is { } last)
        {
            OpenConversation(last);
        }

        RefreshWorkFolderText();
    }

    /// <summary>Kontrola bez klikania: zrzut rozwiniętej listy umiejętności (osobne okienko — zrzut okna go nie obejmuje).</summary>
    internal async Task SaveSkillsListAsync(string path)
    {
        SkillsButton.IsChecked = true;
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        Snapshot.Save((FrameworkElement)SkillsPopup.Child, path);
        SkillsButton.IsChecked = false;
    }

    /// <summary>Do kontroli bez klikania: wysyła polecenia po kolei.</summary>
    internal async Task RunAsync(IEnumerable<string> commands)
    {
        foreach (var command in commands)
        {
            await Panel.SendAsync(command);
        }
    }

    /// <summary>Zapisuje wybory (profile, foldery, uprawnienia) w ustawieniach programu.</summary>
    public void SaveChoices()
    {
        if (!_saveSettings)
        {
            return;
        }

        var settings = _context.Settings;
        settings.HarnessProfiles = SelectedProfiles;
        settings.HarnessProfile = SelectedProfiles.FirstOrDefault();
        settings.TeklaExchangeFolder = ExchangeBox.Text.Trim();
        settings.FileRoots = RootsBox.Text.Trim();
        settings.CodePermission = _permission == "czyta" ? "czyta" : "pytaj";
        _context.Save?.Invoke();
    }

    /// <summary>Zapisuje rozmowę, kończy sesję i zamyka połączenie z modelem.</summary>
    public void Shutdown()
    {
        SaveCurrent();
        Panel.NewSession();
        (_llm as IDisposable)?.Dispose();
        _llm = null;
    }

    /// <summary>
    /// Po zmianie ustawień (profile, internet, dane CDE, wykonawca, model): zamyka sesję, ale rozmowa zostaje — następne
    /// polecenie utworzy sesję z nowymi ustawieniami i z całą dotychczasową historią.
    /// </summary>
    public void RebuildSession()
    {
        CaptureHistory();
        Panel.ResetSession();
    }

    private HarnessSession CreateSession()
    {
        _context = _contextProvider?.Invoke() ?? _context;
        var names = EffectiveProfiles;
        if (names.Count == 0)
        {
            throw new InvalidOperationException("Wybierz co najmniej jedną umiejętność nad rozmową.");
        }

        var parts = LoadParts(names);
        var profile = HarnessProfile.Combine(parts);
        (_llm as IDisposable)?.Dispose();
        _llm = null;
        _llm = CreateLlm(profile);
        _sessionModelKey = ModelKey();
        var history = _current.History.Select(m => new LlmMessage(m.Role, m.Content)).ToList();
        // Tokeny samych zasad i narzędzi (jedno krótkie zapytanie na sesję) — tylko gdy liczniki są widoczne.
        return BuildSession(parts, profile, _llm, history, _context.Settings.ShowTokens, CreateSubAgentHost(names));
    }

    /// <summary>Profile umiejętności z dysku, z wiedzą z dysku (Źródła wiedzy) — do sesji głównej i sesji podagentów.</summary>
    private List<HarnessProfile> LoadParts(IReadOnlyList<string> names)
    {
        var parts = names.Select(n => HarnessProfile.Load(Path.Combine(_context.ProfilesRoot, n))).ToList();
        for (var i = 0; i < parts.Count; i++)
        {
            LoadDiskKnowledge(names[i], parts[i]);
        }

        return parts;
    }

    /// <summary>
    /// Gospodarz podagentów z Ustawień → Gdzie liczyć → Komputery dla podagentów: null, gdy nie ma żadnego komputera
    /// (ani „ten komputer”). Umiejętności podagenta: wszystkie widoczne na liście; domyślnie te same, co sesja główna.
    /// Model: najpierw model główny (gdy lokalny) i preferowane wybranego profilu, potem to, co jest na danym komputerze.
    /// </summary>
    private ISubAgentHost? CreateSubAgentHost(List<string> mainSkills)
    {
        var settings = _context.Settings;
        if (!settings.AgentsUseThisComputer && settings.AgentMachines.Count == 0)
        {
            return null;
        }

        var available = AvailableSkills;
        var skills = ProfilesPanel.Children.OfType<CheckBox>().Select(c => (string)c.Tag).Where(available.Contains)
            .Select(n => new SubAgentSkill(n, CloudViewModel.SafeDescription(Path.Combine(_context.ProfilesRoot, n)) ?? ""))
            .ToList();
        var preferred = new List<string>();
        if (_context.Cloud is null && !string.IsNullOrWhiteSpace(_context.Model))
        {
            preferred.Add(_context.Model);
        }

        if (WorkProfileBox.SelectedItem is ComboBoxItem { Tag: WorkProfileSetting work })
        {
            preferred.AddRange(work.Models.Where(k => !k.Contains('|')));
        }

        var host = new SubAgentHost(new SubAgentHostOptions
        {
            Machines = settings.AgentMachines,
            UseThisComputer = settings.AgentsUseThisComputer,
            LocalGatewayUrl = _context.OllamaUrl,
            LocalOllamaUrl = _context.LocalOllamaUrl,
            LocalGpuGb = _context.LocalGpuGb,
            LocalGpuText = _context.LocalGpuText,
            Skills = skills,
            DefaultSkills = mainSkills.Where(available.Contains).ToList(),
            ContextLength = _context.ContextLength,
            Temperature = _context.Temperature,
            PreferredModels = preferred.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            // Sesja podagenta: te same wykonawcy, foldery i zgody, co sesja główna; bez historii, liczników i własnych podagentów.
            BuildSession = (names, llm) => Dispatcher.InvokeAsync(() =>
            {
                var parts = LoadParts(names);
                return BuildSession(parts, HarnessProfile.Combine(parts), llm, null, false, null);
            }).Task,
        });
        return host.HasMachines ? host : null;
    }

    /// <summary>Klient modelu sesji głównej: chmura (prosto z tego komputera) albo Ollama przez bramę okna.</summary>
    private ILlmClient CreateLlm(HarnessProfile profile)
    {
        if (_context.Cloud is { } cloud)
        {
            // Model w chmurze idzie prosto z tego komputera (bez bramy i kolejki). Czy profile na niego pozwalają, sprawdza HarnessSession.
            var key = _context.GetApiKey?.Invoke(cloud);
            if (string.IsNullOrEmpty(key))
            {
                throw new InvalidOperationException("Brak klucza API " + cloud.Name + " — wpisz go w Ustawieniach → Modele w chmurze.");
            }

            return CloudLlm.Create(cloud, key, _context.Model, _context.Temperature);
        }

        return new OllamaLlmClient(new OllamaOptions
        {
            Url = _context.OllamaUrl,
            Model = _context.Model,
            ContextLength = _context.ContextLength,
            Temperature = _context.Temperature,
            KeepAlive = "10m",
            ClientName = "Code: " + profile.Name,
            // W kolejce bramy można poczekać, aż inny program albo komputer skończy.
            RequestTimeout = TimeSpan.FromMinutes(10),
        });
    }

    /// <summary>
    /// Sesja z gotowych profili i klienta modelu: wykonawcy według umiejętności (Tekla, SQL, pliki, Office, OCR, RFEM),
    /// notatki o sytuacji, internet gdy wolno. Dla sesji głównej z historią i gospodarzem podagentów; dla podagenta bez nich.
    /// </summary>
    private HarnessSession BuildSession(List<HarnessProfile> parts, HarnessProfile profile, ILlmClient llm, IList<LlmMessage>? history, bool measureSystemPrompt, ISubAgentHost? subAgents)
    {
        var options = new SessionOptions
        {
            ConfirmMutation = ConfirmAsync,
            History = history,
            MeasureSystemPrompt = measureSystemPrompt,
            SubAgents = subAgents,
        };
        if (_context.Web is { } web && profile.Settings.Internet)
        {
            // Internet odblokowany i wszystkie profile na niego pozwalają (cde — nie: dane z bazy nie mogą wyjść w zapytaniu do wyszukiwarki).
            profile = profile.WithInternet();
            options.Web = web;
        }

        var notes = new List<string>();
        var router = new RoutingExecutor();
        FileSystemExecutor? files = null;
        OcrExecutor? ocr = null;
        RfemExecutor? rfem = null;
        // OCR i PDF (Tesseract, PDFium, PDFsharp) z ustawieniami z Ustawienia → OCR, w tych samych folderach co pliki.
        OcrExecutor OcrFor()
        {
            if (ocr is null)
            {
                var (roots, work) = FileRoots();
                ocr = new OcrExecutor(OcrViewModel.ToOptions(_context.Settings, roots, work));
            }

            return ocr;
        }

        foreach (var part in parts)
        {
            var hostTools = part.Tools.Where(t => !t.IsBuiltIn).Select(t => t.Name).ToList();
            if (hostTools.Count == 0)
            {
                continue; // profil samej wiedzy (np. mkk) — nie ma czego wykonywać
            }

            IToolExecutor executor;
            if (part.Settings.SqlTools.Count > 0)
            {
                // Połączenie z Ustawień → Menedżer poświadczeń (wpis PostgreSQL o nazwie z pola „poswiadczenie” profilu).
                // Kontrola bez klikania może podać plik połączenia.
                var credentialName = CredentialName(part.Name, part.Settings);
                var access = _context.GetPostgres?.Invoke(credentialName);
                var connection = !string.IsNullOrWhiteSpace(_testConnectionFile) ? ConnectionFromJson(_testConnectionFile) : access?.ConnectionString;
                if (connection is null)
                {
                    throw new InvalidOperationException("Brak poświadczenia do bazy „" + credentialName + "”. Dodaj je w Ustawieniach → Menedżer poświadczeń (typ PostgreSQL, nazwa bazy „" + credentialName + "”).");
                }

                var person = !string.IsNullOrWhiteSpace(_testPerson) ? _testPerson : access?.PersonId;
                if (string.IsNullOrWhiteSpace(person))
                {
                    throw new InvalidOperationException("Wpisz swój person_id przy bazie „" + credentialName + "” w Ustawieniach → Menedżer poświadczeń — model pyta o „moje” dane.");
                }

                executor = new PostgresReadOnlyExecutor(new PostgresReadOnlyOptions { ConnectionString = connection });
                options.Variables["ja"] = person.Trim();
                if (part.Name == "cde" && !Guid.TryParse(person.Trim(), out _))
                {
                    // W CDE person_id to UUID z bazy; login („jan.kowalski”) da błąd bazy „invalid input syntax for type uuid”.
                    Panel.AddInfo("Uwaga: person_id przy bazie „" + credentialName + "” to „" + person.Trim() + "”, a w CDE identyfikator osoby ma postać "
                        + "UUID z bazy (np. 3f2b…-…), nie login. Popraw go w Ustawieniach → Menedżer poświadczeń — inaczej pytania o „moje” dane skończą się błędem bazy.");
                }
                notes.Add("Pytający ma w bazie identyfikator osoby {{ja}} — w SQL wpisuj dosłownie '{{ja}}', program podstawi wartość.");
            }
            else if (IsFilesProfile(part.Name))
            {
                if (files is null)
                {
                    var (roots, work) = FileRoots();
                    files = new FileSystemExecutor(new FileSystemOptions { AllowedRoots = roots, WorkingFolder = work });
                    notes.Add("Komputer z Windows, użytkownik " + Environment.UserName + ". Folder bieżący na start (folder tej rozmowy): " + files.WorkingFolder
                        + ". Dozwolone foldery: " + string.Join("; ", roots.Select(Environment.ExpandEnvironmentVariables)) + ".");
                }

                executor = files;
            }
            else if (IsOfficeProfile(part.Name))
            {
                // Te same dozwolone foldery co „pliki” i „komputer” (pole nad rozmową); dokumenty Office przez COM zainstalowanego Microsoft 365.
                var (roots, work) = FileRoots();
                executor = new OfficeExecutor(new OfficeOptions { AllowedRoots = roots, WorkingFolder = work });
                notes.Add("Programy Office na tym komputerze: " + OfficeExecutor.InstalledSummary() + "."
                    + (files is null
                        ? " Folder bieżący na start (folder tej rozmowy): " + work + ". Dozwolone foldery: " + string.Join("; ", roots.Select(Environment.ExpandEnvironmentVariables)) + "."
                        : ""));
            }
            else if (IsOcrProfile(part.Name))
            {
                executor = OcrFor();
                var (roots, work) = FileRoots();
                notes.Add("OCR robi Tesseract (języki " + _context.Settings.OcrLanguages + ", ustawienia w Ustawienia → OCR)."
                    + (files is null
                        ? " Folder bieżący na start (folder tej rozmowy): " + work + ". Dozwolone foldery: " + string.Join("; ", roots.Select(Environment.ExpandEnvironmentVariables)) + "."
                        : ""));
            }
            else if (IsRfemProfile(part.Name))
            {
                // Modele RFEM 5 (.rf5) czytane z pliku bez programu Dlubal, tylko do odczytu, w tych samych folderach co pliki.
                var (roots, work) = FileRoots();
                rfem ??= new RfemExecutor(new RfemOptions { AllowedRoots = roots, WorkingFolder = work });
                executor = rfem;
                notes.Add("Pliki RFEM 5 (.rf5) czyta program z pliku, bez RFEM-a; gdy w folderze bieżącym jest jeden plik .rf5, polecenia biorą go bez podawania ścieżki. "
                    + RfemExecutor.LiveStatus()
                    + (files is null
                        ? " Folder bieżący na start (folder tej rozmowy): " + work + ". Dozwolone foldery: " + string.Join("; ", roots.Select(Environment.ExpandEnvironmentVariables)) + "."
                        : ""));
            }
            else if (BridgeRadio.IsChecked == true)
            {
                executor = new TeklaBridgeExecutor(new TeklaBridgeOptions { ExchangeFolder = ExchangeBox.Text.Trim(), ProfileFolder = part.Folder });
            }
            else
            {
                executor = new SimulatedModelExecutor();
                notes.Add("Model Tekli jest symulowany (próba bez Tekli).");
            }

            foreach (var tool in hostTools)
            {
                // Polecenia PDF umiejętności „office” (pdf_czytaj, pdf_polacz…) wykonuje wykonawca OCR/PDF, nie COM Office’a.
                router.Route(tool, IsOfficeProfile(part.Name) && OcrExecutor.PdfTools.Contains(tool) ? OcrFor() : executor);
            }
        }

        notes.Add("Dziś jest " + MainViewModel.TodayText() + ".");
        options.ContextNote = string.Join(" ", notes);
        return new HarnessSession(llm, profile, router, options);
    }

    /// <summary>Dozwolone foldery z pola nad rozmową i folder bieżący (folder rozmowy, zawsze dozwolony) — dla profili plików i Office’a.</summary>
    private (List<string> Roots, string? Work) FileRoots()
    {
        var roots = RootsBox.Text.Split(';').Select(r => r.Trim()).Where(r => r.Length > 0).ToList();
        var work = WorkFolder(roots);
        if (work is not null && !roots.Any(r => SameFolder(Environment.ExpandEnvironmentVariables(r), work)))
        {
            roots.Insert(0, work); // folder rozmowy jest dozwolony zawsze
        }

        return (roots, work);
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

    private void Profile_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        SkillsChanged();
    }

    /// <summary>Po zmianie zaznaczonych umiejętności (ręcznie albo wyborem profilu): sesja, opis, zapis wyboru.</summary>
    private void SkillsChanged()
    {
        var hadConversation = Panel.Entries.Count > 0;
        ApplyProfiles(syncKnowledge: true);
        if (hadConversation)
        {
            RebuildSession();
            Panel.AddInfo(EffectiveProfiles.Count == 0
                ? "Nie ma wybranej umiejętności — wybierz co najmniej jedną."
                : "Od teraz umiejętności: " + string.Join(", ", EffectiveProfiles) + " (rozmowa trwa dalej).");
        }
        else
        {
            Panel.ResetSession();
        }

        _current.Profiles = EffectiveProfiles;
        if (_saveSettings)
        {
            _context.Settings.HarnessProfiles = SelectedProfiles;
            _context.Save?.Invoke();
        }

        RefreshModelText();
    }

    /// <summary>Opcje i podpowiedzi dla zaznaczonych profili (Tekla, CDE, foldery), opis zasad przy kilku profilach.</summary>
    private void ApplyProfiles(bool syncKnowledge)
    {
        var names = EffectiveProfiles;
        var chosen = SelectedProfiles;
        SkillsSummary.Text = chosen.Count == 0 ? "wybierz…" : string.Join(", ", chosen);
        SkillsSummary.Foreground = (Brush)FindResource(chosen.Count == 0 ? "MutedBrush" : "TextBrush");
        SyncWorkProfile();
        bool IsKnowledge(string n) => n != "cde" && !IsFilesProfile(n) && !IsOfficeProfile(n) && !IsOcrProfile(n) && !IsRfemProfile(n) && !IsRuleOnly(n) && !HarnessProfile.HasHostTools(Path.Combine(_context.ProfilesRoot, n));
        var isCde = names.Contains("cde");
        var isOffice = names.Any(IsOfficeProfile);
        var isOcr = names.Any(IsOcrProfile);
        var isRfem = names.Any(IsRfemProfile);
        var isFiles = names.Any(IsFilesProfile) || isOffice || isOcr || isRfem;   // Office, OCR i RFEM używają tego samego pola z dozwolonymi folderami
        var isComputer = names.Contains("komputer");
        var hasKnowledge = names.Any(IsKnowledge);
        var hasTekla = names.Any(n => n != "cde" && !IsFilesProfile(n) && !IsOfficeProfile(n) && !IsOcrProfile(n) && !IsRfemProfile(n) && !IsRuleOnly(n) && !IsKnowledge(n));
        // Umiejętność samych zasad (np. formatowanie) nie jest „rolą” — podpowiedzi liczą tylko pozostałe.
        var roleNames = names.Where(n => !IsRuleOnly(n)).ToList();
        if (roleNames.Count == 0)
        {
            roleNames = names;
        }
        Panel.ResultPreviewLength = isComputer || isOffice || isOcr || isRfem || hasKnowledge ? 3000 : 600;
        TeklaOptions.Visibility = hasTekla ? Visibility.Visible : Visibility.Collapsed;
        CdeOptions.Visibility = isCde ? Visibility.Visible : Visibility.Collapsed;
        RefreshCdeText();
        FilesOptions.Visibility = isFiles ? Visibility.Visible : Visibility.Collapsed;
        RefreshWorkFolderText();

        if (names.Count == 0)
        {
            HintText.Text = "Wybierz co najmniej jedną umiejętność.";
        }
        else if (roleNames.Count == 1)
        {
            var name = roleNames[0];
            HintText.Text = name == "cde"
                ? "SQL od modelu wykonuje się w transakcji tylko do odczytu. Przykłady: „Ile przepracowałem godzin na projekcie WSP?”, „W których dokumentach projektu EDO jest mowa o kotwach?”."
                : name == "komputer"
                    ? "Przykład: „utwórz folder Raporty”, „ile jest PDF-ów z podfolderami?”, „spakuj R02 do zipa”, „otwórz Notatnik”. "
                      + "Polecenia, które tylko czytają, idą od razu; zmiany zależą od uprawnień."
                : name == "pliki"
                    ? "Przykład: „utwórz folder Raporty”, „co jest w tym folderze?”, „otwórz go”. Kasowania nie ma."
                : IsOfficeProfile(name)
                    ? "Przykład: „co jest w notatka.docx?”, „dopisz na końcu raportu akapit…”, „wpisz do B10 sumę B2:B9”, „zapisz raport jako PDF”, "
                      + "„przygotuj maila do … z załącznikiem”. Czytanie idzie od razu; zmiany zależą od uprawnień, a mail zostaje szkicem (nie wysyła). "
                      + "Programy Office na tym komputerze: " + OfficeExecutor.InstalledSummary() + "."
                : IsOcrProfile(name)
                    ? "Przykład: „zrób OCR pliku skan.jpg”, „przeczytaj protokol.pdf (skan)”, „zrób OCR wszystkich skanów w folderze skany”, „czy raport.pdf to skan czy dokument?”. "
                      + "Obrazek wklejony do pola rozmowy (Ctrl+V) jest rozpoznawany od razu. Języki, obrót, układ i reguły: Ustawienia → OCR (teraz: "
                      + _context.Settings.OcrLanguages + ", obrót " + _context.Settings.OcrRotation + ")."
                : IsRfemProfile(name)
                    ? "Przykład: „co jest w modelu hala.rf5?”, „jaki przekrój i długość ma pręt 28?”, „jaka jest siła w pręcie 28 od KO5?”, „które pręty mają HEA 160?”, "
                      + "„jakie są kombinacje?”. Plik .rf5 jest czytany z dysku bez RFEM-a (RFEM 6 .rf6 — nie). Siły są tylko w plikach zapisanych po obliczeniu; "
                      + "wszystko tylko do odczytu, bez pytań o zgodę. Z otwartym RFEM 5: „co jest otwarte w RFEM?”, „jaki przekrój ma zaznaczony pręt?” (RFEM poprosi o kliknięcie), "
                      + "„siły w pręcie 28 od KO5 z otwartego modelu”, „reakcje w węźle 1”. " + RfemExecutor.LiveStatus()
                : name == "tlumaczenie"
                    ? "Wklej tekst po polsku — model odda tłumaczenie na angielski brytyjski (terminy konstrukcyjne bierze ze słownika umiejętności przez szukaj_wiedzy). "
                      + "Razem z umiejętnością office tłumaczy dokumenty: „przeczytaj opis.docx i zapisz tłumaczenie jako opis_EN.docx”. Najlepiej z qwen3.5:9b albo Bielikiem 11B."
                : IsKnowledge(name)
                    ? "Umiejętność oparta na wiedzy: model szuka w dokumentacji (szukaj_wiedzy) i odpowiada, nie wykonuje poleceń. "
                      + (name == "mkk"
                          ? "Przykład: „jak w Hali dodać stężenia dachowe?”, „jaki znak ma siła osiowa w Robocie, a jaki w RFEM?”, „ile zapytań ma plan Free API RFEM 6?”."
                          : name == "tablice"
                              ? "Przykład: „jaki Wy ma IPE 200?”, „ile waży 12 m HEB 200?”, „nośność połączenia IH1.1 IPE 240 M16 S235 8.8”, „rozstaw śrub M20”, „nośność śruby M16 10.9”. "
                                + "Tablice Żyburtowicza (przekroje), DAST (połączenia doczołowe IH) i SZS C5 (śruby, spoiny) — liczby tylko z wiedzy, rachunki kalkulatorem."
                              : "Źródła i aktualizacja dokumentacji: Ustawienia → Umiejętności.")
                    : "Przykład: „wstaw słup HEB300 w punkcie 6000, 0 od zera do 3,53”, potem „zmień go na HEB320”, „dodaj mu podstawę z 4 kotwami M24”.";
        }
        else
        {
            var settings = roleNames.Select(n => CloudViewModel.SafeSettings(Path.Combine(_context.ProfilesRoot, n)) ?? new ProfileSettings()).ToList();
            var rules = new List<string>();
            if (settings.Any(s => !s.Internet))
            {
                rules.Add("internet wyłączony (" + string.Join(", ", roleNames.Where((_, i) => !settings[i].Internet)) + ")");
            }

            var cloud = settings.Skip(1).Aggregate((IEnumerable<string>)settings[0].CloudProviders, (a, s) => a.Intersect(s.CloudProviders, StringComparer.OrdinalIgnoreCase)).ToList();
            rules.Add(cloud.Count == 0 ? "tylko modele lokalne" : "chmura: " + string.Join(", ", cloud.Select(c => CloudProvider.Find(c)?.Family ?? c)));
            HintText.Text = "Kilka umiejętności naraz: " + string.Join(" + ", roleNames) + ". Obowiązują najostrzejsze zasady: " + string.Join(", ", rules)
                + ". Polecenie systemowe to suma umiejętności — przy trzech i więcej ustaw kontekst 32768 (Ustawienia → Czat); mały model gorzej wybiera spośród wielu narzędzi."
                + (SelectedProfiles.Contains("pliki") && isComputer ? " Umiejętność pliki pominięta: komputer ma wszystkie jej polecenia." : "");
        }

        if (syncKnowledge)
        {
            _ = AutoSyncAsync();
        }
    }

    /// <summary>Profile obsługiwane przez wykonawcę plików i poleceń (folder, PowerShell, programy).</summary>
    private static bool IsFilesProfile(string? name) => name == "pliki" || name == "komputer";

    /// <summary>Profil dokumentów Office (Word, Excel, Outlook, PowerPoint przez COM) — <c>OfficeExecutor</c> z tymi samymi dozwolonymi folderami.</summary>
    private static bool IsOfficeProfile(string? name) => name == "office";

    /// <summary>Umiejętność OCR (Tesseract, PDFium) — <c>OcrExecutor</c> z tymi samymi dozwolonymi folderami i ustawieniami z Ustawienia → OCR.</summary>
    private static bool IsOcrProfile(string? name) => name == "ocr";

    /// <summary>Umiejętność RFEM (pliki .rf5 bez Dlubala) — <c>RfemExecutor</c> z tymi samymi dozwolonymi folderami.</summary>
    private static bool IsRfemProfile(string? name) => name == "rfem";

    /// <summary>
    /// Obrazek wklejony do pola polecenia: zapis do <c>.amadeusz\obrazy\</c> folderu rozmowy (albo folderu tymczasowego
    /// w kontroli bez klikania) i OCR z ustawień okna; zwraca blok z nagłówkiem i tekstem do pola. Wyłączony w Ustawieniach → zwraca pusty tekst.
    /// </summary>
    private async Task<string> OcrPastedImageAsync(byte[] png)
    {
        if (!_context.Settings.OcrOnPaste)
        {
            return "";
        }

        var (roots, work) = FileRoots();
        var folder = Path.Combine(work ?? roots.Select(Environment.ExpandEnvironmentVariables).FirstOrDefault(Directory.Exists) ?? Path.GetTempPath(), ".amadeusz", "obrazy");
        return await new OcrViewModel(_context.Settings, () => _context.Save?.Invoke()).RecognizePastedAsync(png, folder);
    }

    /// <summary>
    /// Umiejętność samych zasad odpowiedzi (np. formatowanie): bez poleceń gospodarza i bez narzędzi wbudowanych.
    /// W sesji nie jest rolą (<see cref="HarnessProfile.Combine"/>), a w podpowiedziach okna się jej nie liczy.
    /// </summary>
    private bool IsRuleOnly(string name)
    {
        var folder = Path.Combine(_context.ProfilesRoot, name);
        return !HarnessProfile.HasHostTools(folder) && CloudViewModel.SafeSettings(folder) is { BuiltInTools.Count: 0 };
    }

    private void Executor_Changed(object sender, RoutedEventArgs e)
    {
        if (IsLoaded)
        {
            RebuildSession();
        }
    }

    /// <summary>Przełącznik „Pokaż kroki modelu” nad rozmową: ten sam, co w Ustawieniach → Czat (zapisywany).</summary>
    private void Steps_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        var on = StepsBox.IsChecked == true;
        Panel.ShowModelSteps = on;
        if (_context.Settings.ShowSteps != on)
        {
            _context.Settings.ShowSteps = on;
            if (_saveSettings)
            {
                _context.Save?.Invoke();
            }

            ShowStepsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Zmienił się przełącznik kroków nad rozmową (okno odświeża Ustawienia).</summary>
    public event EventHandler? ShowStepsChanged;

    /// <summary>Ustawienia → Czat → „Pokaż kroki modelu”: ten sam przełącznik, co nad rozmową; działa od razu, także na wpisy sprzed zmiany.</summary>
    public void ApplyShowSteps()
    {
        _context = _contextProvider?.Invoke() ?? _context;
        var wasLoading = _loading;
        _loading = true;
        StepsBox.IsChecked = _context.Settings.ShowSteps;
        _loading = wasLoading;
        Panel.ShowModelSteps = _context.Settings.ShowSteps;
    }

    /// <summary>
    /// Ustawienia → Czat → „Pokaż tokeny i czasy”: liczniki przy wpisach. Nowa sesja przy następnym poleceniu, żeby zmierzyła
    /// (albo przestała mierzyć) tokeny samych zasad i narzędzi; wpisy rozmowy zostają.
    /// </summary>
    public void ApplyShowTokens()
    {
        _context = _contextProvider?.Invoke() ?? _context;
        Panel.ShowTokens = _context.Settings.ShowTokens;
        if (!Panel.IsBusy)
        {
            Panel.ResetSession();
        }
    }

    private string FolderOf(string profile) => Path.Combine(_context.ProfilesRoot, profile);

    private static string RepoCache => SkillsViewModel.RepoCache;

    /// <summary>Sama aktualizacja wiedzy zaznaczonych profili, gdy mają źródła, a od ostatniej minęło ponad 6 godzin.</summary>
    private async Task AutoSyncAsync()
    {
        foreach (var name in EffectiveProfiles)
        {
            var folder = FolderOf(name);
            if (KnowledgeSync.ReadSources(folder).Count == 0)
            {
                continue;
            }

            var last = KnowledgeSync.LastSync(folder);
            if (last == null || DateTime.Now - last.Value >= TimeSpan.FromHours(6))
            {
                await SyncAsync(name);
            }
        }
    }

    /// <summary>
    /// Wiedza umiejętności z dysku (Źródła wiedzy… → Pliki i foldery na dysku), np. wspólny glosariusz biura: do wyszukiwarki
    /// i do glosariusza sesji. Dysk niedostępny = kopia z tego komputera (z uwagą w rozmowie).
    /// </summary>
    private void LoadDiskKnowledge(string skill, HarnessProfile part)
    {
        if (_context.Settings.DiskKnowledge is not { } map || !map.TryGetValue(skill, out var paths) || paths.Count == 0)
        {
            return;
        }

        var result = DiskKnowledge.Collect(skill, paths);
        var problems = result.Notes.ToList();
        foreach (var file in result.Files)
        {
            try
            {
                part.AddKnowledgeFile(file.Path, file.Source);
            }
            catch (IOException e)
            {
                problems.Add(file.Source + ": " + e.Message);
            }
        }

        if (problems.Count > 0)
        {
            Panel.AddInfo("Wiedza z dysku (" + skill + "): " + string.Join(" ", problems));
        }
    }

    /// <summary>Automatyczna aktualizacja dokumentacji umiejętności (ręczna: Ustawienia → Umiejętności → Aktualizuj).</summary>
    private async Task SyncAsync(string profile)
    {
        var folder = FolderOf(profile);
        if (KnowledgeSync.ReadSources(folder).Count == 0)
        {
            return;
        }

        HintText.Text = "Aktualizuję wiedzę umiejętności " + profile + "…";
        try
        {
            var result = await Task.Run(() => KnowledgeSync.SyncAsync(folder, RepoCache));
            HintText.Text = "Wiedza umiejętności " + profile + ": " + result + ". Następne polecenie użyje nowych plików.";
            RebuildSession();
        }
        catch (Exception ex)
        {
            HintText.Text = "Aktualizacja wiedzy nie powiodła się: " + ex.Message;
        }
    }

    /// <summary>Przycisk „Poświadczenia…” — okno główne otwiera Ustawienia → Menedżer poświadczeń.</summary>
    public event EventHandler? CredentialsRequested;

    private void CdeSettings_Click(object sender, RoutedEventArgs e)
    {
        if (CredentialsRequested is null)
        {
            MessageBox.Show(Window.GetWindow(this), "Poświadczenia ustawisz w głównym oknie: Ustawienia → Menedżer poświadczeń.", "Poświadczenia");
            return;
        }

        CredentialsRequested(this, EventArgs.Empty);
    }

    /// <summary>Nazwa poświadczenia profilu: pole „poswiadczenie” w profil.json albo nazwa profilu.</summary>
    private static string CredentialName(string profile, ProfileSettings? settings)
        => string.IsNullOrWhiteSpace(settings?.Credential) ? profile : settings!.Credential.Trim();

    /// <summary>
    /// Po zmianie poświadczeń: profile z polem „poswiadczenie” widać tylko wtedy, gdy taki wpis jest w Menedżerze
    /// poświadczeń (np. kto nie ma bazy CDE, nie widzi profilu cde). Pod rozmową — opis połączenia zaznaczonego profilu z bazą.
    /// </summary>
    public void RefreshCdeText()
    {
        _context = _contextProvider?.Invoke() ?? _context;
        var changed = false;
        _loading = true;
        foreach (var box in ProfilesPanel.Children.OfType<CheckBox>())
        {
            var settings = CloudViewModel.SafeSettings(Path.Combine(_context.ProfilesRoot, (string)box.Tag));
            var needs = settings?.Credential is { Length: > 0 };
            var available = !needs || _testConnectionFile is not null || _context.GetPostgres?.Invoke(CredentialName((string)box.Tag, settings)) is not null;
            box.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
            if (!available && box.IsChecked == true)
            {
                box.IsChecked = false;
                changed = true;
            }
        }

        _loading = false;
        var database = EffectiveProfiles
            .Select(n => (Name: n, Settings: CloudViewModel.SafeSettings(Path.Combine(_context.ProfilesRoot, n))))
            .FirstOrDefault(p => p.Settings?.SqlTools.Count > 0);
        if (database.Name is not null)
        {
            var credentialName = CredentialName(database.Name, database.Settings);
            var access = _context.GetPostgres?.Invoke(credentialName);
            CdeText.Text = access is null
                ? "Baza „" + credentialName + "”: brak poświadczenia — dodaj je w Ustawieniach → Menedżer poświadczeń."
                : "Baza „" + credentialName + "”: " + access.Description + " (tylko odczyt)" + (string.IsNullOrWhiteSpace(access.PersonId) ? " · brak person_id" : "");
            CdeText.Foreground = (Brush)FindResource(access is null || string.IsNullOrWhiteSpace(access.PersonId) ? "BadBrush" : "MutedBrush");
        }

        RebuildWorkProfiles();
        if (changed)
        {
            ApplyProfiles(syncKnowledge: false);
        }
    }

    // ───────────────────────────── Profile (zestawy umiejętności) ─────────────────────────────

    /// <summary>Umiejętności widoczne na liście (bez tych, którym brakuje poświadczenia).</summary>
    private HashSet<string> AvailableSkills => ProfilesPanel.Children.OfType<CheckBox>()
        .Where(c => c.Visibility == Visibility.Visible).Select(c => (string)c.Tag).ToHashSet(StringComparer.Ordinal);

    /// <summary>Umiejętności profilu dostępne na tym komputerze, w kolejności listy umiejętności.</summary>
    private List<string> SkillsOf(WorkProfileSetting profile, HashSet<string> available)
        => ProfilesPanel.Children.OfType<CheckBox>().Select(c => (string)c.Tag).Where(n => available.Contains(n) && profile.Skills.Contains(n)).ToList();

    /// <summary>Wypełnia listę profili (pomija te, z których umiejętności żadnej tu nie ma) i zaznacza pasujący.</summary>
    private void RebuildWorkProfiles()
    {
        var wasLoading = _loading;
        _loading = true;
        var available = AvailableSkills;
        WorkProfileBox.Items.Clear();
        WorkProfileBox.Items.Add(new ComboBoxItem
        {
            Content = "własny wybór",
            Foreground = (Brush)FindResource("MutedBrush"),
            ToolTip = "Zaznaczone umiejętności nie pasują do żadnego profilu. „Zapisz jako profil…” na liście umiejętności zrobi z nich profil.",
        });
        foreach (var profile in _workProfiles)
        {
            var skills = SkillsOf(profile, available);
            if (skills.Count == 0)
            {
                continue;
            }

            var missing = profile.Skills.Where(n => !available.Contains(n)).ToList();
            WorkProfileBox.Items.Add(new ComboBoxItem
            {
                Content = profile.Name + (missing.Count == 0 ? "" : " (bez: " + string.Join(", ", missing) + ")"),
                Tag = profile,
                ToolTip = "Umiejętności: " + string.Join(", ", skills) + (missing.Count == 0 ? "" : " (niedostępne tutaj: " + string.Join(", ", missing) + ")"),
            });
        }

        _loading = wasLoading;
        SyncWorkProfile();
    }

    /// <summary>Zaznacza na liście profil o dokładnie tych umiejętnościach, które są zaznaczone; inaczej „własny wybór”.</summary>
    private void SyncWorkProfile()
    {
        if (WorkProfileBox.Items.Count == 0)
        {
            return;
        }

        var available = AvailableSkills;
        var chosen = SelectedProfiles;
        var matching = WorkProfileBox.Items.OfType<ComboBoxItem>()
            .Where(i => i.Tag is WorkProfileSetting p && SkillsOf(p, available).OrderBy(n => n).SequenceEqual(chosen.OrderBy(n => n)))
            .ToList();
        var match = matching.FirstOrDefault(i => ((WorkProfileSetting)i.Tag).Name == _lastWorkProfile) ?? matching.FirstOrDefault();
        var wasLoading = _loading;
        _loading = true;
        WorkProfileBox.SelectedItem = match ?? WorkProfileBox.Items[0];
        _loading = wasLoading;
        var current = (match?.Tag as WorkProfileSetting)?.Name;
        DeleteWorkProfileButton.IsEnabled = current is not null;
        DeleteWorkProfileButton.Content = current is null ? "Usuń profil" : "Usuń profil „" + current + "”";
    }

    private void WorkProfile_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || WorkProfileBox.SelectedItem is not ComboBoxItem { Tag: WorkProfileSetting profile })
        {
            return;
        }

        SelectWorkProfile(profile);
    }

    /// <summary>Zaznacza umiejętności profilu (dostępne na tym komputerze) i odznacza resztę.</summary>
    private void SelectWorkProfile(WorkProfileSetting profile)
    {
        _lastWorkProfile = profile.Name;
        var skills = SkillsOf(profile, AvailableSkills);
        if (skills.OrderBy(n => n).SequenceEqual(SelectedProfiles.OrderBy(n => n)))
        {
            SyncWorkProfile();
            if (MissingSkillsNote(profile) is { } note)
            {
                HintText.Text = note;
            }

            return;
        }

        _loading = true;
        foreach (var box in ProfilesPanel.Children.OfType<CheckBox>())
        {
            box.IsChecked = skills.Contains((string)box.Tag);
        }

        _loading = false;
        SkillsChanged();
        ApplyPreferredModel(profile);
        var missing = MissingSkillsNote(profile);
        if (missing is not null)
        {
            HintText.Text = (HintText.Text.Length > 0 && HintText.Text.StartsWith("Profil ", StringComparison.Ordinal) ? HintText.Text + " " : "") + missing;
        }
    }

    /// <summary>
    /// Opis umiejętności profilu, których na tym komputerze nie ma (np. „cde” bez wpisu w Menedżerze poświadczeń) — inaczej
    /// profil „Baza CDE” zostawia samo „formatowanie” i model nie ma czym odpowiedzieć. Null = wszystkie są.
    /// </summary>
    private string? MissingSkillsNote(WorkProfileSetting profile)
    {
        var available = AvailableSkills;
        var known = ProfilesPanel.Children.OfType<CheckBox>().Select(c => (string)c.Tag).ToHashSet(StringComparer.Ordinal);
        var missing = profile.Skills.Where(n => !available.Contains(n)).ToList();
        if (missing.Count == 0)
        {
            return null;
        }

        var parts = missing.Select(n =>
        {
            if (!known.Contains(n))
            {
                return "„" + n + "” (nie ma jej w folderze umiejętności)";
            }

            var settings = CloudViewModel.SafeSettings(Path.Combine(_context.ProfilesRoot, n));
            return settings?.Credential is { Length: > 0 }
                ? "„" + n + "” (dodaj wpis „" + CredentialName(n, settings) + "” w Ustawieniach → Menedżer poświadczeń)"
                : "„" + n + "”";
        });
        return "Na tym komputerze profil „" + profile.Name + "” nie ma umiejętności: " + string.Join(", ", parts) + ".";
    }

    /// <summary>Przełącza model na pierwszy dostępny z preferowanych modeli profilu; gdy żadnego nie ma — mówi, który zostaje.</summary>
    private void ApplyPreferredModel(WorkProfileSetting profile)
    {
        if (profile.Models.Count == 0 || _context.SelectPreferredModel is not { } select)
        {
            return;
        }

        var settings = SelectedSettings;
        List<string> allowed = settings.Count == 0 ? [] : settings.Skip(1).Aggregate(
            (IEnumerable<string>)settings[0].CloudProviders, (a, s) => a.Intersect(s.CloudProviders, StringComparer.OrdinalIgnoreCase)).ToList();
        var chosen = select(profile.Models, allowed);
        _context = _contextProvider?.Invoke() ?? _context;
        HintText.Text = chosen is not null
            ? "Profil " + profile.Name + ": model " + chosen.Label + (SameKey(chosen.Key, profile.Models[0]) ? "." : " (lepszych z listy profilu tu nie ma).")
            : "Profil " + profile.Name + ": żadnego z preferowanych modeli (" + string.Join(", ", profile.Models.Select(ModelChoice.ShortKey)) + ") nie ma na maszynie, która liczy — zostaje " + _context.ModelLabel + ".";
        RefreshModelText();
    }

    private static bool SameKey(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private void SaveWorkProfile_Click(object sender, RoutedEventArgs e)
    {
        SkillsButton.IsChecked = false;
        var chosen = SelectedProfiles;
        if (chosen.Count == 0)
        {
            HintText.Text = "Najpierw zaznacz umiejętności, które ma mieć profil.";
            return;
        }

        var name = TextPrompt.Ask(Window.GetWindow(this), "Zapisz jako profil",
            "Nazwa profilu z umiejętnościami: " + string.Join(", ", chosen) + "\nProfil o tej samej nazwie zostanie zastąpiony.",
            _lastWorkProfile ?? "");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        name = name.Trim();
        var index = _workProfiles.FindIndex(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        // Nowy profil zapamiętuje bieżący model jako preferowany; istniejący zachowuje swoją listę modeli.
        var models = index >= 0 ? _workProfiles[index].Models.ToList()
            : _contextProvider?.Invoke().CurrentModelKey is { } current ? [current] : new List<string>();
        var profile = new WorkProfileSetting { Name = name, Skills = chosen, Models = models };
        if (index >= 0)
        {
            _workProfiles[index] = profile;
        }
        else
        {
            _workProfiles.Add(profile);
        }

        _lastWorkProfile = name;
        SaveWorkProfiles();
        HintText.Text = "Zapisano profil „" + name + "”: " + string.Join(", ", chosen) + ".";
    }

    private void DeleteWorkProfile_Click(object sender, RoutedEventArgs e)
    {
        SkillsButton.IsChecked = false;
        if (WorkProfileBox.SelectedItem is not ComboBoxItem { Tag: WorkProfileSetting profile })
        {
            return;
        }

        var answer = MessageBox.Show(Window.GetWindow(this),
            "Usunąć profil „" + profile.Name + "” z listy?\nUmiejętności zostają zaznaczone, a rozmowy nic nie tracą.",
            "Usuń profil", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        _workProfiles.Remove(profile);
        if (_lastWorkProfile == profile.Name)
        {
            _lastWorkProfile = null;
        }

        SaveWorkProfiles();
    }

    private void SaveWorkProfiles()
    {
        if (_saveSettings)
        {
            _context.Settings.WorkProfiles = CopyWorkProfiles(_workProfiles);
            _context.Save?.Invoke();
            WorkProfilesChanged?.Invoke(this, EventArgs.Empty);
        }

        RebuildWorkProfiles();
    }

    /// <summary>Profile zapisane tu („Zapisz jako profil…”, „Usuń profil”) — Ustawienia → Profile odświeżają listę.</summary>
    public event EventHandler? WorkProfilesChanged;

    /// <summary>Po zmianie w Ustawieniach → Profile: wczytuje listę profili od nowa (zaznaczone umiejętności zostają).</summary>
    public void ReloadWorkProfiles()
    {
        _context = _contextProvider?.Invoke() ?? _context;
        _workProfiles = CopyWorkProfiles(_context.Settings.WorkProfiles);
        RebuildWorkProfiles();
    }

    /// <summary>Kopia listy (null = domyślne), żeby zmiany tutaj nie ruszały ustawień przed zapisem.</summary>
    private static List<WorkProfileSetting> CopyWorkProfiles(List<WorkProfileSetting>? profiles)
        => (profiles ?? WorkProfileSetting.Defaults()).Select(p => new WorkProfileSetting { Name = p.Name, Skills = p.Skills.ToList(), Models = p.Models.ToList() }).ToList();

    /// <summary>Kontrola bez klikania: wybiera profil po nazwie (jak z listy).</summary>
    internal bool SelectWorkProfileForTest(string name)
    {
        var item = WorkProfileBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(i => i.Tag is WorkProfileSetting p && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (item is null)
        {
            return false;
        }

        WorkProfileBox.SelectedItem = item; // jak kliknięcie: WorkProfile_Changed
        return true;
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

    /// <summary>Wybór profilu: przełącz na pierwszy dostępny z preferowanych modeli (klucze, dozwoleni dostawcy chmury); null = żadnego nie ma.</summary>
    public Func<IEnumerable<string>, IReadOnlyCollection<string>, ModelItem?>? SelectPreferredModel { get; init; }

    /// <summary>Klucz wybranego modelu (do „Zapisz jako profil…”).</summary>
    public string? CurrentModelKey { get; init; }

    /// <summary>Modele na liście okna (do okna preferowanych modeli profilu).</summary>
    public IReadOnlyList<ModelChoice> ModelChoices { get; init; } = [];

    /// <summary>Połączenie z bazą z Menedżera poświadczeń po nazwie wpisu (hasło odszyfrowane dopiero tutaj); null = brak wpisu.</summary>
    public Func<string, PostgresAccess?>? GetPostgres { get; init; }

    /// <summary>Dostęp do internetu (przełącznik w oknie); null = zablokowany.</summary>
    public WebAccess? Web { get; init; }

    public int ContextLength { get; init; }

    public double Temperature { get; init; }

    /// <summary>Ollama na tym komputerze (lista modeli dla podagenta „ten komputer”; zapytania idą przez <see cref="OllamaUrl"/>).</summary>
    public string LocalOllamaUrl { get; init; } = "";

    /// <summary>Pamięć karty tego komputera w GB (dobór modelu podagenta); null = nieznana.</summary>
    public double? LocalGpuGb { get; init; }

    /// <summary>Opis karty tego komputera, np. „NVIDIA GeForce RTX 4060 (8 GB)”; null = nie wykryto.</summary>
    public string? LocalGpuText { get; init; }

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
