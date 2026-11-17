using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LokalneAI.Harness.Llm;
using LokalneAI.Harness.Session;
using LokalneAI.Harness.Tools;

namespace LokalneAI.Harness.Wpf
{
    /// <summary>
    /// Panel rozmowy z pół-harnessem do osadzenia w programie gospodarza. Gospodarz podaje tylko
    /// <see cref="SessionFactory"/> (model + profil + wykonawca); panel pokazuje polecenia, wywołania
    /// narzędzi z wynikami, odpowiedzi i licznik kontekstu. „Nowa sesja” kasuje historię.
    /// </summary>
    public partial class HarnessPanel : UserControl
    {
        private readonly ObservableCollection<PanelEntry> _entries = new ObservableCollection<PanelEntry>();
        private HarnessSession _session;
        private CancellationTokenSource _cts;
        private PanelEntry _thinking;
        private bool _follow = true;

        /// <summary>Tworzy pusty panel. Przed pierwszym poleceniem ustaw <see cref="SessionFactory"/>.</summary>
        public HarnessPanel()
        {
            InitializeComponent();
            EntriesList.ItemsSource = _entries;
            Scroll.ScrollChanged += (s, e) =>
            {
                if (e.ExtentHeightChange == 0)
                {
                    _follow = Scroll.VerticalOffset >= Scroll.ScrollableHeight - 4;
                }
                else if (_follow)
                {
                    Scroll.ScrollToEnd();
                }
            };
        }

        /// <summary>Tworzy nową sesję (model, profil, wykonawca). Wywoływane przy pierwszym poleceniu i po „Nowa sesja”.</summary>
        public Func<HarnessSession> SessionFactory { get; set; }

        /// <summary>Tytuł nad rozmową.</summary>
        public string Title
        {
            get => TitleText.Text;
            set => TitleText.Text = value;
        }

        /// <summary>Pokazywać surowe kroki modelu (JSON) — do diagnostyki.</summary>
        public bool ShowModelSteps { get; set; }

        /// <summary>Ile znaków wyniku polecenia pokazać pod wywołaniem (np. więcej dla wyników PowerShella).</summary>
        public int ResultPreviewLength { get; set; } = 600;

        /// <summary>Wpisy rozmowy (np. do zapisu albo testów).</summary>
        public ObservableCollection<PanelEntry> Entries => _entries;

        /// <summary>Bieżąca sesja albo null.</summary>
        public HarnessSession Session => _session;

        /// <summary>Po każdej turze (odpowiedź, pytanie, błąd).</summary>
        public event EventHandler<TurnResult> TurnCompleted;

        /// <summary>
        /// Po każdym poleceniu, także przerwanym albo zakończonym wyjątkiem (wtedy <see cref="TurnCompleted"/> nie przychodzi) —
        /// np. żeby gospodarz zapisał rozmowę.
        /// </summary>
        public event EventHandler TurnEnded;

        /// <summary>Użytkownik kliknął „Nowa sesja” (po wyczyszczeniu panelu) — np. gospodarz zaczyna nową zapisywaną rozmowę.</summary>
        public event EventHandler NewSessionClicked;

        /// <summary>Napis na przycisku nowej sesji (np. „Nowa rozmowa”).</summary>
        public string NewSessionText
        {
            get => NewSessionButton.Content as string;
            set => NewSessionButton.Content = value;
        }

        /// <summary>Czy trwa polecenie.</summary>
        public bool IsBusy => _cts != null;

        /// <summary>
        /// Gotowe pytanie o zgodę przed poleceniem zmieniającym model — do <see cref="SessionOptions.ConfirmMutation"/>.
        /// Pokazuje okno Tak/Nie z treścią od wykonawcy (np. polecenie PowerShella z ostrzeżeniami) albo z nazwą polecenia
        /// i argumentami. Gdy polecenie może coś zniszczyć (kasuje), domyślnym przyciskiem jest „Nie”.
        /// </summary>
        public Task<bool> ConfirmMutationAsync(ToolCall call, CancellationToken cancellationToken)
        {
            var message = !string.IsNullOrWhiteSpace(call.ConfirmationMessage)
                ? call.ConfirmationMessage
                : "Wykonać polecenie zmieniające model?\n\n" + call.Name + "\n" + DescribeArguments(call.ArgumentsJson);
            var icon = call.ConfirmationDangerous ? MessageBoxImage.Warning : MessageBoxImage.Question;
            var defaultAnswer = call.ConfirmationDangerous ? MessageBoxResult.No : MessageBoxResult.Yes;
            return Dispatcher.InvokeAsync(() =>
            {
                var owner = Window.GetWindow(this);
                var answer = owner != null
                    ? MessageBox.Show(owner, message, "Potwierdź polecenie", MessageBoxButton.YesNo, icon, defaultAnswer)
                    : MessageBox.Show(message, "Potwierdź polecenie", MessageBoxButton.YesNo, icon, defaultAnswer);
                return answer == MessageBoxResult.Yes;
            }).Task;
        }

        /// <summary>Wysyła polecenie (jak klik „Wyślij”).</summary>
        public async Task<TurnResult> SendAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || _cts != null)
            {
                return null;
            }

            if (_session == null && !StartSession())
            {
                return null;
            }

            _entries.Add(new PanelEntry(PanelEntryKind.User, text.Trim()));
            _follow = true;
            SetBusy(true);
            TurnResult turn = null;
            _cts = new CancellationTokenSource();
            try
            {
                turn = await _session.SendAsync(text, _cts.Token);
                if (turn.Kind == TurnKind.Failed)
                {
                    _entries.Add(new PanelEntry(PanelEntryKind.Error, turn.Text));
                }

                _entries.Add(new PanelEntry(PanelEntryKind.Info, string.Format(Pl, "{0:0.0} s · wywołań modelu: {1}", turn.Duration.TotalSeconds, turn.ModelCalls)));
            }
            catch (OperationCanceledException)
            {
                _entries.Add(new PanelEntry(PanelEntryKind.Warning, "Przerwano."));
            }
            catch (LlmException e)
            {
                _entries.Add(new PanelEntry(PanelEntryKind.Error, e.Message));
            }
            catch (Exception e)
            {
                // Panel nie może wywrócić programu-gospodarza (Send_Click jest async void).
                _entries.Add(new PanelEntry(PanelEntryKind.Error, "Błąd: " + e.Message));
            }
            finally
            {
                RemoveThinking();
                _cts.Dispose();
                _cts = null;
                SetBusy(false);
            }

            if (turn != null)
            {
                TurnCompleted?.Invoke(this, turn);
            }

            TurnEnded?.Invoke(this, EventArgs.Empty);
            return turn;
        }

        /// <summary>Zamyka bieżącą sesję (kasuje historię) i czyści panel.</summary>
        public void NewSession()
        {
            _cts?.Cancel();
            CloseSession();
            _entries.Clear();
            UpdateContext(null);
        }

        /// <summary>
        /// Zamyka sesję, ale zostawia wpisy w panelu — następne polecenie utworzy nową sesję przez <see cref="SessionFactory"/>
        /// (np. po zmianie profili; gospodarz może podać jej wcześniejszą historię w <see cref="SessionOptions.History"/>).
        /// </summary>
        public void ResetSession()
        {
            _cts?.Cancel();
            CloseSession();
            RemoveThinking();
            UpdateContext(null);
        }

        /// <summary>Zamyka sesję i pokazuje zapisane wpisy (np. wznowiona rozmowa); następne polecenie utworzy nową sesję.</summary>
        public void LoadEntries(System.Collections.Generic.IEnumerable<PanelEntry> entries)
        {
            NewSession();
            foreach (var entry in entries ?? Enumerable.Empty<PanelEntry>())
            {
                _entries.Add(entry);
            }

            _follow = true;
            Scroll.ScrollToEnd();
        }

        /// <summary>Ustawia kursor w polu polecenia.</summary>
        public void FocusInput() => InputBox.Focus();

        /// <summary>Dopisuje wpis informacyjny (np. „Profile: tekla, cde”).</summary>
        public void AddInfo(string text) => _entries.Add(new PanelEntry(PanelEntryKind.Info, text));

        private static readonly System.Globalization.CultureInfo Pl = System.Globalization.CultureInfo.GetCultureInfo("pl-PL");

        private bool StartSession()
        {
            if (SessionFactory == null)
            {
                _entries.Add(new PanelEntry(PanelEntryKind.Error, "Panel nie ma podłączonej sesji (SessionFactory)."));
                return false;
            }

            try
            {
                _session = SessionFactory();
            }
            catch (Exception e)
            {
                // Błąd konfiguracji gospodarza (zły folder profilu, połączenie) — pokazujemy zamiast wywracać okno.
                _entries.Add(new PanelEntry(PanelEntryKind.Error, "Nie udało się utworzyć sesji: " + e.Message));
                return false;
            }

            _session.Progress += OnProgress;
            UpdateContext(_session.Context);
            return true;
        }

        private void CloseSession()
        {
            if (_session != null)
            {
                _session.Progress -= OnProgress;
                _session.Dispose();
                _session = null;
            }
        }

        private void OnProgress(object sender, HarnessEvent e)
        {
            // Zdarzenia przychodzą z wątku w tle. Invoke (a nie BeginInvoke) czeka, aż okno je pokaże — dzięki temu
            // kolejność wpisów zgadza się z kolejnością pracy sesji. Spóźnione zdarzenia zamkniętej sesji pomijamy.
            Action show = () =>
            {
                if (ReferenceEquals(sender, _session))
                {
                    Show(e);
                }
            };
            if (Dispatcher.CheckAccess())
            {
                show();
            }
            else
            {
                Dispatcher.Invoke(show);
            }
        }

        private void Show(HarnessEvent e)
        {
            switch (e.Kind)
            {
                case HarnessEventKind.ModelThinking:
                    if (_thinking == null)
                    {
                        _thinking = new PanelEntry(PanelEntryKind.Info, e.Text);
                        _entries.Add(_thinking);
                    }
                    else
                    {
                        _thinking.Text = e.Text;
                        _entries.Remove(_thinking);
                        _entries.Add(_thinking);
                    }

                    break;
                case HarnessEventKind.ModelStep:
                    if (ShowModelSteps)
                    {
                        _entries.Insert(Math.Max(0, _entries.Count - (_thinking != null ? 1 : 0)), new PanelEntry(PanelEntryKind.Info, e.Text));
                    }

                    break;
                case HarnessEventKind.ToolStarted:
                    Insert(new PanelEntry(PanelEntryKind.Call, "→ " + e.Call.Tool, DescribeArguments(e.Call.ArgumentsJson)) { Success = null });
                    break;
                case HarnessEventKind.ToolFinished:
                    var open = _entries.LastOrDefault(x => x.Kind == PanelEntryKind.Call && x.Success == null && x.Text == "→ " + e.Call.Tool);
                    if (open == null)
                    {
                        open = new PanelEntry(PanelEntryKind.Call, "→ " + e.Call.Tool, DescribeArguments(e.Call.ArgumentsJson));
                        Insert(open);
                    }

                    open.Success = e.Call.Success;
                    open.Detail = DescribeArguments(e.Call.ArgumentsJson) + "\n" + (e.Call.Success || (e.Call.Result ?? "").StartsWith("BŁĄD", StringComparison.Ordinal) ? "" : "BŁĄD: ") + Shorten(e.Call.Result, ResultPreviewLength);
                    break;
                case HarnessEventKind.Answer:
                    RemoveThinking();
                    _entries.Add(new PanelEntry(PanelEntryKind.Answer, e.Text));
                    break;
                case HarnessEventKind.Question:
                    RemoveThinking();
                    _entries.Add(new PanelEntry(PanelEntryKind.Question, e.Text));
                    break;
                case HarnessEventKind.Warning:
                    Insert(new PanelEntry(PanelEntryKind.Warning, e.Text));
                    break;
                case HarnessEventKind.ContextChanged:
                    UpdateContext(e.Context);
                    break;
            }
        }

        private void Insert(PanelEntry entry)
        {
            // Przed wpisem „model pracuje…”, żeby ten był zawsze na dole.
            var index = _thinking != null && _entries.Contains(_thinking) ? _entries.IndexOf(_thinking) : _entries.Count;
            _entries.Insert(index, entry);
        }

        private void RemoveThinking()
        {
            if (_thinking != null)
            {
                _entries.Remove(_thinking);
                _thinking = null;
            }
        }

        private void UpdateContext(ContextUsage context)
        {
            if (context == null)
            {
                ContextBar.Value = 0;
                ContextText.Text = "Kontekst: nowa sesja";
                return;
            }

            ContextBar.Value = context.Fraction * 100;
            ContextBar.Foreground = context.Fraction >= 0.85 ? Brushes.DarkOrange : new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6));
            ContextText.Text = "Kontekst: " + context;
        }

        private void SetBusy(bool busy)
        {
            SendButton.IsEnabled = !busy;
            NewSessionButton.IsEnabled = !busy;
            StopButton.IsEnabled = busy;
        }

        private async void Send_Click(object sender, RoutedEventArgs e)
        {
            var text = InputBox.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            InputBox.Clear();
            await SendAsync(text);
            InputBox.Focus();
        }

        private void Stop_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

        private void NewSession_Click(object sender, RoutedEventArgs e)
        {
            NewSession();
            NewSessionClicked?.Invoke(this, EventArgs.Empty);
        }

        private void Input_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
            {
                e.Handled = true;
                if (SendButton.IsEnabled)
                {
                    Send_Click(sender, e);
                }
            }
            else if (e.Key == Key.Escape)
            {
                _cts?.Cancel();
            }
        }

        private void Scroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            Scroll.ScrollToVerticalOffset(Scroll.VerticalOffset - (e.Delta * 0.4));
            e.Handled = true;
        }

        /// <summary>Argumenty jako „x_mm=6000, profil=HEB300”; SQL w osobnych liniach.</summary>
        internal static string DescribeArguments(string json)
        {
            try
            {
                using (var document = JsonDocument.Parse(json ?? "{}"))
                {
                    if (document.RootElement.ValueKind != JsonValueKind.Object)
                    {
                        return json;
                    }

                    return string.Join(", ", document.RootElement.EnumerateObject().Select(p =>
                        p.Name == "sql" ? "\n" + p.Value.GetString() + "\n" : p.Name + "=" + (p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.GetRawText())));
                }
            }
            catch (JsonException)
            {
                return json;
            }
        }

        private static string Shorten(string text, int max) => string.IsNullOrEmpty(text) || text.Length <= max ? text : text.Substring(0, max) + "…";
    }
}
