using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
        private readonly ObservableCollection<PendingImage> _pendingImages = new ObservableCollection<PendingImage>();
        private HarnessSession _session;
        private CancellationTokenSource _cts;
        private PanelEntry _thinking;
        private bool _follow = true;

        // Tokeny i czasy przy wpisach (ShowTokens): polecenie bieżącej tury, krok modelu czekający na swój wpis,
        // wpisy bieżącego kroku (dostaną „→ do modelu” z następnego wywołania), pisanie pytania.
        private PanelEntry _turnUser;
        private ModelCallMetrics _pendingModel;
        private readonly System.Collections.Generic.List<PanelEntry> _stepTiles = new System.Collections.Generic.List<PanelEntry>();
        private int _sessionModelCalls;
        private DateTime? _typingStart;
        private TimeSpan? _pendingTyping;

        /// <summary>Pokazywać przy wpisach tokeny i czasy (<see cref="PanelEntry.Metrics"/>) — do nauki i diagnostyki.</summary>
        public static readonly DependencyProperty ShowTokensProperty = DependencyProperty.Register(
            nameof(ShowTokens), typeof(bool), typeof(HarnessPanel), new PropertyMetadata(false));

        /// <summary>Liczniki obok wpisów (szerokie okno) zamiast pod nimi — ustawia panel przy zmianie szerokości.</summary>
        public static readonly DependencyProperty TokensBesideProperty = DependencyProperty.Register(
            nameof(TokensBeside), typeof(bool), typeof(HarnessPanel), new PropertyMetadata(true));

        /// <summary>
        /// Pokazywać przy każdym wpisie dokładne tokeny i czasy z serwera modelu: przy poleceniu — pisanie pytania i skład promptu,
        /// przy kroku modelu — ile napisał, myślenie i pisanie, przy narzędziu — czas i ile wyniku wróciło do modelu, pod turą — Σ
        /// wejście | praca | odpowiedź. W szerokim oknie z prawej strony wpisu, w wąskim pod nim. Liczy się zawsze; to tylko widok.
        /// </summary>
        public bool ShowTokens
        {
            get => (bool)GetValue(ShowTokensProperty);
            set => SetValue(ShowTokensProperty, value);
        }

        /// <summary>Czy liczniki stoją obok wpisów (okno dość szerokie); inaczej pod nimi.</summary>
        public bool TokensBeside
        {
            get => (bool)GetValue(TokensBesideProperty);
            private set => SetValue(TokensBesideProperty, value);
        }

        /// <summary>Objaśnienie liczników (podpowiedź przy nich).</summary>
        public static string TokensLegend => TokenText.Legend;

        /// <summary>Tworzy pusty panel. Przed pierwszym poleceniem ustaw <see cref="SessionFactory"/>.</summary>
        public HarnessPanel()
        {
            InitializeComponent();
            EntriesList.ItemsSource = _entries;
            AgentTiles.ItemsSource = _agentTiles;
            PendingImagesList.ItemsSource = _pendingImages;
            _pendingImages.CollectionChanged += (s, e) => PendingImagesList.Visibility = _pendingImages.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            _agentTiles.CollectionChanged += (s, e) =>
            {
                AgentTiles.Visibility = _agentTiles.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                if (_agentTiles.Count > 0 && _agentClock == null)
                {
                    _agentClock = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                    _agentClock.Tick += (s2, e2) =>
                    {
                        foreach (var tile in _agentTiles)
                        {
                            tile.Refresh();
                        }
                    };
                }

                if (_agentClock != null)
                {
                    if (_agentTiles.Count > 0)
                    {
                        _agentClock.Start();
                    }
                    else
                    {
                        _agentClock.Stop();
                    }
                }
            };
            Scroll.SizeChanged += (s, e) => TokensBeside = Scroll.ActualWidth >= 760;
            InputBox.TextChanged += (s, e) =>
            {
                // Pisanie pytania: od pierwszego znaku w pustym polu do wysłania.
                if (InputBox.Text.Length == 0)
                {
                    _typingStart = null;
                }
                else if (_typingStart == null)
                {
                    _typingStart = DateTime.UtcNow;
                }
            };
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

        /// <summary>Pokazywać wpisy „Krok N” (<see cref="PanelEntryKind.Step"/>): co model zwrócił w każdym kroku, z JSON-em w szczegółach.</summary>
        public static readonly DependencyProperty ShowModelStepsProperty = DependencyProperty.Register(
            nameof(ShowModelSteps), typeof(bool), typeof(HarnessPanel), new PropertyMetadata(false));

        /// <summary>
        /// Pokazywać kroki modelu w rozmowie: wpis „Krok N” po każdej odpowiedzi modelu (wywołania, odpowiedź, pytanie, plan)
        /// z surowym JSON-em w szczegółach. Wpisy powstają zawsze (także w przebiegu podagentów) i zapisują się z rozmową;
        /// to tylko widok, więc przełącznik działa też na wpisy sprzed zmiany.
        /// </summary>
        public bool ShowModelSteps
        {
            get => (bool)GetValue(ShowModelStepsProperty);
            set => SetValue(ShowModelStepsProperty, value);
        }

        // Podagenci: kafelki nad polem polecenia (w trakcie) i wpisy Agent w rozmowie (drzewo przebiegu) — po numerze podagenta.
        private readonly ObservableCollection<AgentTile> _agentTiles = new ObservableCollection<AgentTile>();
        private readonly System.Collections.Generic.Dictionary<int, PanelEntry> _agentEntries = new System.Collections.Generic.Dictionary<int, PanelEntry>();
        private readonly System.Collections.Generic.Dictionary<int, PanelEntry> _agentThinking = new System.Collections.Generic.Dictionary<int, PanelEntry>();
        private System.Windows.Threading.DispatcherTimer _agentClock;

        /// <summary>Podagenci, którzy właśnie liczą (kafelki nad polem polecenia).</summary>
        public ObservableCollection<AgentTile> AgentTilesList => _agentTiles;

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
        /// Pytanie pojawia się w rozmowie (bez wyskakującego okna) z treścią od wykonawcy (np. polecenie PowerShella
        /// z ostrzeżeniami) albo z nazwą polecenia i argumentami, i z przyciskami Tak/Nie. Stop, Esc albo zamknięcie sesji = „Nie”.
        /// Gdy polecenie może coś zniszczyć (kasuje), wyróżniony jest przycisk „Nie”.
        /// </summary>
        public Task<bool> ConfirmMutationAsync(ToolCall call, CancellationToken cancellationToken) => ConfirmMutationAsync(call, cancellationToken, null);

        /// <summary>
        /// Jak <see cref="ConfirmMutationAsync(ToolCall, CancellationToken)"/>, z <paramref name="note"/> — zdaniem gospodarza nad pytaniem
        /// (np. „Uprawnienia: Tylko czyta — zgoda dotyczy tylko tego polecenia”); null = bez.
        /// </summary>
        public Task<bool> ConfirmMutationAsync(ToolCall call, CancellationToken cancellationToken, string note)
        {
            var message = !string.IsNullOrWhiteSpace(call.ConfirmationMessage)
                ? call.ConfirmationMessage
                : "Wykonać polecenie zmieniające model?\n\n" + call.Name + "\n" + DescribeArguments(call.ArgumentsJson);
            if (!string.IsNullOrWhiteSpace(note))
            {
                message = note.Trim() + "\n\n" + message;
            }

            return Dispatcher.InvokeAsync(() =>
            {
                var entry = new PanelEntry(PanelEntryKind.Confirm, message.Trim()) { Dangerous = call.ConfirmationDangerous };
                var answer = entry.Ask();
                Insert(entry);
                _follow = true;
                if (cancellationToken.CanBeCanceled)
                {
                    var registration = cancellationToken.Register(() => Dispatcher.BeginInvoke(new Action(() => entry.Resolve(false, "Przerwano — bez zgody."))));
                    answer.ContinueWith(_ => registration.Dispose(), TaskScheduler.Default);
                }

                return answer;
            }).Task.Unwrap();
        }

        /// <summary>
        /// Pytanie z przyciskami w rozmowie (jak zgoda na polecenie), z własnymi napisami: np. „Włączyć umiejętność pliki
        /// i ponowić polecenie?” → „Tak, włącz i ponów” / „Nie”. Zwraca odpowiedź; przerwanie tokenem = nie. Z dowolnego wątku.
        /// </summary>
        public Task<bool> AskAsync(string message, string yesText, string noText, CancellationToken cancellationToken)
        {
            return Dispatcher.InvokeAsync(() =>
            {
                var entry = new PanelEntry(PanelEntryKind.Confirm, (message ?? "").Trim())
                {
                    YesText = string.IsNullOrWhiteSpace(yesText) ? "Tak" : yesText,
                    NoText = string.IsNullOrWhiteSpace(noText) ? "Nie" : noText,
                };
                var answer = entry.Ask();
                Insert(entry);
                _follow = true;
                if (cancellationToken.CanBeCanceled)
                {
                    var registration = cancellationToken.Register(() => Dispatcher.BeginInvoke(new Action(() => entry.Resolve(false, "Przerwano — bez zgody."))));
                    answer.ContinueWith(_ => registration.Dispose(), TaskScheduler.Default);
                }

                return answer;
            }).Task.Unwrap();
        }

        private void ConfirmYes_Click(object sender, RoutedEventArgs e)
        {
            var entry = (sender as FrameworkElement)?.DataContext as PanelEntry;
            entry?.Resolve(true, entry.YesText == "Tak, wykonaj" ? "Tak — wykonuję." : "Tak — " + entry.YesText.ToLowerInvariant() + ".");
        }

        private void ConfirmNo_Click(object sender, RoutedEventArgs e)
        {
            var entry = (sender as FrameworkElement)?.DataContext as PanelEntry;
            entry?.Resolve(false, entry.YesText == "Tak, wykonaj" ? "Nie — polecenie odrzucone." : "Nie.");
        }

        /// <summary>Wysyła polecenie (jak klik „Wyślij”).</summary>
        public Task<TurnResult> SendAsync(string text) => SendAsync(text, null);

        /// <summary>
        /// Wysyła polecenie z obrazami dla modelu (PNG albo JPEG; null = bez obrazów). Obrazy dostaje tylko model, który je
        /// widzi — gospodarz decyduje o tym przed wywołaniem (<see cref="ImagesForModel"/>), a tekst z OCR daje w poleceniu.
        /// <paramref name="shownText"/>: co pokazać we wpisie użytkownika, gdy model dostaje więcej (np. polecenie bez doklejonego
        /// opisu tablicy); <paramref name="shownDetail"/>: szary dopisek pod nim. Null = pokazuje cały <paramref name="text"/>.
        /// </summary>
        public async Task<TurnResult> SendAsync(string text, System.Collections.Generic.IReadOnlyList<byte[]> images, string shownText = null, string shownDetail = null)
        {
            if (string.IsNullOrWhiteSpace(text) || _cts != null)
            {
                return null;
            }

            if (_session == null && !StartSession())
            {
                return null;
            }

            var typing = _pendingTyping;
            _pendingTyping = null;
            var imagesNote = images == null || images.Count == 0 ? null
                : "Obrazy dla modelu: " + images.Count + " (model je widzi; tekst z OCR, jeśli był, jest w poleceniu).";
            _turnUser = new PanelEntry(PanelEntryKind.User, (shownText ?? text).Trim())
            {
                Metrics = typing.HasValue ? "pisanie pytania: " + TokenText.Time(typing.Value) : null,
                Detail = shownDetail == null ? imagesNote : imagesNote == null ? shownDetail : shownDetail + "\n" + imagesNote,
            };
            _entries.Add(_turnUser);
            _pendingModel = null;
            _stepTiles.Clear();
            _follow = true;
            SetBusy(true);
            TurnResult turn = null;
            _cts = new CancellationTokenSource();
            try
            {
                turn = await _session.SendAsync(text, images, _cts.Token);
                if (turn.Kind == TurnKind.Failed)
                {
                    _entries.Add(new PanelEntry(PanelEntryKind.Error, turn.Text));
                }

                _entries.Add(new PanelEntry(PanelEntryKind.Info, string.Format(Pl, "{0:0.0} s · wywołań modelu: {1}", turn.Duration.TotalSeconds, turn.ModelCalls))
                {
                    Metrics = TokenText.Turn(turn, typing),
                });
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
                _turnUser = null;
                _pendingModel = null;
                _stepTiles.Clear();
                FinishAgents();
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
            FinishAgents();
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

            _sessionModelCalls = 0;
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

                    _thinking.Metrics = e.Context != null && e.Context.UsedTokens > 0 ? TokenText.Estimate(e.Context) : null;
                    break;
                case HarnessEventKind.ModelCall:
                    OnModelCall(e.ModelCall);
                    break;
                case HarnessEventKind.ModelStep:
                    // Wpis kroku powstaje zawsze (zapisuje się z rozmową); widać go przy ShowModelSteps.
                    Insert(StepEntry(e));
                    break;
                case HarnessEventKind.AgentStarted:
                    OnAgentStarted(e.Agent);
                    break;
                case HarnessEventKind.AgentProgress:
                    OnAgentProgress(e.Agent, e.Inner);
                    break;
                case HarnessEventKind.AgentFinished:
                    OnAgentFinished(e.Agent);
                    break;
                case HarnessEventKind.ToolStarted:
                    Insert(Track(new PanelEntry(PanelEntryKind.Call, "→ " + e.Call.Tool, DescribeArguments(e.Call.ArgumentsJson)) { Success = null }));
                    break;
                case HarnessEventKind.ToolFinished:
                    var open = _entries.LastOrDefault(x => x.Kind == PanelEntryKind.Call && x.Success == null && x.Text == "→ " + e.Call.Tool);
                    if (open == null)
                    {
                        open = Track(new PanelEntry(PanelEntryKind.Call, "→ " + e.Call.Tool, DescribeArguments(e.Call.ArgumentsJson)));
                        Insert(open);
                    }

                    if (e.Call.Duration > TimeSpan.Zero)
                    {
                        open.Metrics = TokenText.Append(open.Metrics, "narzędzie: " + TokenText.Time(e.Call.Duration));
                    }

                    open.Success = e.Call.Success;
                    open.Detail = DescribeArguments(e.Call.ArgumentsJson) + "\n" + (e.Call.Success || (e.Call.Result ?? "").StartsWith("BŁĄD", StringComparison.Ordinal) ? "" : "BŁĄD: ") + Shorten(e.Call.Result, ResultPreviewLength);
                    break;
                case HarnessEventKind.Answer:
                    RemoveThinking();
                    var answer = new PanelEntry(PanelEntryKind.Answer, e.Text);
                    if (_turnUser != null && _pendingModel == null)
                    {
                        answer.Metrics = "odpowiedź złożył program z wyników narzędzi (model jej nie pisał)";
                    }

                    _entries.Add(Track(answer));
                    break;
                case HarnessEventKind.Question:
                    RemoveThinking();
                    _entries.Add(Track(new PanelEntry(PanelEntryKind.Question, e.Text)));
                    break;
                case HarnessEventKind.Warning:
                    Insert(Track(new PanelEntry(PanelEntryKind.Warning, e.Text)));
                    break;
                case HarnessEventKind.ContextChanged:
                    UpdateContext(e.Context);
                    break;
            }
        }

        /// <summary>
        /// Model odpowiedział: pierwsze wywołanie tury opisuje polecenie użytkownika (skład promptu), następne — o ile urósł
        /// prompt po wpisach poprzedniego kroku (wyniki narzędzi). Liczby tego kroku czekają na pierwszy wpis, który krok wywoła.
        /// </summary>
        private void OnModelCall(ModelCallMetrics m)
        {
            if (m == null || _turnUser == null)
            {
                return;
            }

            _sessionModelCalls++;
            if (m.CallInTurn == 1)
            {
                _turnUser.Metrics = TokenText.Append(_turnUser.Metrics, TokenText.FirstPrompt(m, _sessionModelCalls == 1));
            }
            else
            {
                var target = _stepTiles.LastOrDefault(t => t.Kind == PanelEntryKind.Call) ?? _stepTiles.LastOrDefault();
                if (target != null)
                {
                    target.Metrics = TokenText.Append(target.Metrics, TokenText.Added(m));
                }
            }

            _stepTiles.Clear();
            _pendingModel = m;
        }

        /// <summary>Wpis bieżącego kroku: dostaje liczby kroku modelu, który go wywołał (pierwszy wpis po tym kroku).</summary>
        private PanelEntry Track(PanelEntry entry)
        {
            if (_turnUser == null)
            {
                return entry;
            }

            _stepTiles.Add(entry);
            if (_pendingModel != null)
            {
                entry.Metrics = TokenText.Append(TokenText.Model(_pendingModel), entry.Metrics);
                _pendingModel = null;
            }

            return entry;
        }

        private void Insert(PanelEntry entry)
        {
            // Przed wpisem „model pracuje…”, żeby ten był zawsze na dole.
            var index = _thinking != null && _entries.Contains(_thinking) ? _entries.IndexOf(_thinking) : _entries.Count;
            _entries.Insert(index, entry);
        }

        /// <summary>Wpis „Krok N: …” z surowego kroku modelu: rodzaj kroku, plan i nazwy wywołań w nagłówku, JSON w szczegółach.</summary>
        internal static PanelEntry StepEntry(HarnessEvent e)
            => new PanelEntry(PanelEntryKind.Step, DescribeStep(e.Text, e.Step), Shorten(e.Text, 2000));

        /// <summary>„Krok 2: wywołania → czytaj_plik, czytaj_plik” / „Krok 3: odpowiedź” / „Krok 1: plan — …; wywołania → …”.</summary>
        internal static string DescribeStep(string json, int step)
        {
            var head = step > 0 ? "Krok " + step + ": " : "Krok: ";
            try
            {
                using (var document = JsonDocument.Parse(json ?? ""))
                {
                    var root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("krok", out var kind) || kind.ValueKind != JsonValueKind.String)
                    {
                        return head + "niepoprawny krok (nie JSON kroku)";
                    }

                    var parts = new System.Collections.Generic.List<string>();
                    if (root.TryGetProperty("plan", out var plan) && plan.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(plan.GetString()))
                    {
                        parts.Add("plan — " + Shorten(plan.GetString().Trim(), 200));
                    }

                    switch (kind.GetString())
                    {
                        case "wywolania":
                            var names = new System.Collections.Generic.List<string>();
                            if (root.TryGetProperty("wywolania", out var calls) && calls.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var call in calls.EnumerateArray())
                                {
                                    names.Add(call.TryGetProperty("narzedzie", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : "?");
                                }
                            }

                            parts.Add("wywołania → " + (names.Count == 0 ? "(brak)" : string.Join(", ", names)));
                            break;
                        case "odpowiedz":
                            parts.Add("odpowiedź");
                            break;
                        case "pytanie":
                            parts.Add("pytanie do użytkownika");
                            break;
                        default:
                            parts.Add(kind.GetString());
                            break;
                    }

                    return head + string.Join("; ", parts);
                }
            }
            catch (JsonException)
            {
                return head + "niepoprawny krok (nie JSON)";
            }
        }

        // ───────────────────────────── Podagenci ─────────────────────────────

        private void OnAgentStarted(SubAgentInfo agent)
        {
            if (agent == null)
            {
                return;
            }

            var entry = new PanelEntry(PanelEntryKind.Agent, "Podagent " + agent.Number + ": " + Shorten((agent.Task ?? "").Replace("\r", "").Replace("\n", " "), 160), AgentDetail(agent))
            {
                Success = null,
            };
            _agentEntries[agent.Number] = entry;
            Insert(entry);
            var tile = _agentTiles.FirstOrDefault(t => t.Number == agent.Number);
            if (tile == null)
            {
                _agentTiles.Add(new AgentTile(agent));
            }
            else
            {
                tile.Refresh();
            }
        }

        private void OnAgentProgress(SubAgentInfo agent, HarnessEvent inner)
        {
            if (agent == null || inner == null)
            {
                return;
            }

            PanelEntry parent;
            if (!_agentEntries.TryGetValue(agent.Number, out parent))
            {
                OnAgentStarted(agent);
                parent = _agentEntries[agent.Number];
            }

            ShowChild(agent.Number, parent, inner);
            parent.Detail = AgentDetail(agent);
            _agentTiles.FirstOrDefault(t => t.Number == agent.Number)?.Refresh();
        }

        private void OnAgentFinished(SubAgentInfo agent)
        {
            if (agent == null)
            {
                return;
            }

            PanelEntry entry;
            if (_agentEntries.TryGetValue(agent.Number, out entry))
            {
                PanelEntry thinking;
                if (_agentThinking.TryGetValue(agent.Number, out thinking))
                {
                    entry.Children.Remove(thinking);
                    _agentThinking.Remove(agent.Number);
                }

                entry.Success = agent.Success;
                entry.Detail = AgentDetail(agent);
            }

            var tile = _agentTiles.FirstOrDefault(t => t.Number == agent.Number);
            if (tile != null)
            {
                _agentTiles.Remove(tile);
            }
        }

        /// <summary>Druga linia wpisu podagenta: komputer, model, umiejętności, stan albo wynik i czas.</summary>
        private string AgentDetail(SubAgentInfo agent)
        {
            var where = (agent.Machine ?? "?") + " · " + (agent.Model ?? "?") + (string.IsNullOrWhiteSpace(agent.Skills) ? "" : " · " + agent.Skills);
            if (agent.IsRunning || agent.Success == null)
            {
                return where + " · " + (agent.Status ?? "startuje") + (agent.Duration > TimeSpan.Zero ? " · " + TokenText.Time(agent.Duration) : "");
            }

            return where + " · " + TokenText.Time(agent.Duration) + "\n" + (agent.Success == true ? "" : "BŁĄD: ") + Shorten(agent.Result, ResultPreviewLength);
        }

        /// <summary>Zdarzenie z sesji podagenta → wpis w jego przebiegu (uproszczona wersja <see cref="Show"/>, bez liczników tokenów).</summary>
        private void ShowChild(int number, PanelEntry parent, HarnessEvent e)
        {
            var children = parent.Children;
            PanelEntry thinking;
            _agentThinking.TryGetValue(number, out thinking);
            switch (e.Kind)
            {
                case HarnessEventKind.ModelThinking:
                    if (thinking == null)
                    {
                        thinking = new PanelEntry(PanelEntryKind.Info, e.Text);
                        _agentThinking[number] = thinking;
                    }
                    else
                    {
                        thinking.Text = e.Text;
                        children.Remove(thinking);
                    }

                    children.Add(thinking);
                    break;
                case HarnessEventKind.ModelStep:
                    InsertChild(children, thinking, StepEntry(e));
                    break;
                case HarnessEventKind.ToolStarted:
                    InsertChild(children, thinking, new PanelEntry(PanelEntryKind.Call, "→ " + e.Call.Tool, DescribeArguments(e.Call.ArgumentsJson)) { Success = null });
                    break;
                case HarnessEventKind.ToolFinished:
                    var open = children.LastOrDefault(x => x.Kind == PanelEntryKind.Call && x.Success == null && x.Text == "→ " + e.Call.Tool);
                    if (open == null)
                    {
                        open = new PanelEntry(PanelEntryKind.Call, "→ " + e.Call.Tool, DescribeArguments(e.Call.ArgumentsJson));
                        InsertChild(children, thinking, open);
                    }

                    if (e.Call.Duration > TimeSpan.Zero)
                    {
                        open.Metrics = TokenText.Append(open.Metrics, "narzędzie: " + TokenText.Time(e.Call.Duration));
                    }

                    open.Success = e.Call.Success;
                    open.Detail = DescribeArguments(e.Call.ArgumentsJson) + "\n" + (e.Call.Success || (e.Call.Result ?? "").StartsWith("BŁĄD", StringComparison.Ordinal) ? "" : "BŁĄD: ") + Shorten(e.Call.Result, ResultPreviewLength);
                    break;
                case HarnessEventKind.Answer:
                    RemoveChildThinking(number, children);
                    children.Add(new PanelEntry(PanelEntryKind.Answer, e.Text));
                    break;
                case HarnessEventKind.Question:
                    RemoveChildThinking(number, children);
                    children.Add(new PanelEntry(PanelEntryKind.Question, e.Text));
                    break;
                case HarnessEventKind.Warning:
                    InsertChild(children, thinking, new PanelEntry(PanelEntryKind.Warning, e.Text));
                    break;
            }
        }

        private static void InsertChild(ObservableCollection<PanelEntry> children, PanelEntry thinking, PanelEntry entry)
        {
            var index = thinking != null && children.Contains(thinking) ? children.IndexOf(thinking) : children.Count;
            children.Insert(index, entry);
        }

        private void RemoveChildThinking(int number, ObservableCollection<PanelEntry> children)
        {
            PanelEntry thinking;
            if (_agentThinking.TryGetValue(number, out thinking))
            {
                children.Remove(thinking);
                _agentThinking.Remove(number);
            }
        }

        private void AgentCancel_Click(object sender, RoutedEventArgs e)
        {
            var tile = (sender as FrameworkElement)?.DataContext as AgentTile;
            tile?.Cancel();
        }

        private void RemoveThinking()
        {
            if (_thinking != null)
            {
                _entries.Remove(_thinking);
                _thinking = null;
            }
        }

        /// <summary>Koniec tury (także przerwanej): kafelki znikają, otwarte wpisy podagentów dostają stan „przerwany”.</summary>
        private void FinishAgents()
        {
            foreach (var pair in _agentEntries.ToList())
            {
                PanelEntry thinking;
                if (_agentThinking.TryGetValue(pair.Key, out thinking))
                {
                    pair.Value.Children.Remove(thinking);
                }

                if (pair.Value.Success == null)
                {
                    pair.Value.Success = false;
                    pair.Value.Detail = (pair.Value.Detail ?? "") + "\nprzerwany";
                }
            }

            _agentThinking.Clear();
            _agentEntries.Clear();
            _agentTiles.Clear();
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

            _pendingTyping = _typingStart.HasValue ? DateTime.UtcNow - _typingStart.Value : (TimeSpan?)null;
            InputBox.Clear();
            var images = _pendingImages.Select(i => i.Png).ToList();
            _pendingImages.Clear();
            await SendAsync(text, images);
            InputBox.Focus();
        }

        private void Stop_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

        /// <summary>Przerywa bieżące polecenie (jak „Stop”); nic nie robi, gdy panel nie pracuje.</summary>
        public void Cancel() => _cts?.Cancel();

        /// <summary>
        /// Czy pole polecenia z przyciskami „Wyślij” i „Stop” jest widoczne (domyślnie tak). Gospodarz z własnym polem
        /// (np. tablica z polem pod spodem) chowa je i woła <see cref="SendAsync(string, System.Collections.Generic.IReadOnlyList{byte[]}, string, string)"/> i <see cref="Cancel"/>.
        /// </summary>
        public bool ShowInput
        {
            get => InputArea.Visibility == Visibility.Visible;
            set => InputArea.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        }

        private void NewSession_Click(object sender, RoutedEventArgs e)
        {
            NewSession();
            NewSessionClicked?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Obrazek wklejony do pola polecenia (Ctrl+V ze schowka, np. zrzut ekranu albo fragment skanu): gospodarz dostaje
        /// PNG i zwraca tekst do wstawienia w pole (np. wynik OCR z nagłówkiem). Null = bez OCR (zwykłe wklejanie tekstu
        /// działa zawsze). Wołane w wątku okna; może trwać kilka sekund — pole jest wtedy zablokowane.
        /// </summary>
        public Func<byte[], Task<string>> ImagePasted { get; set; }

        /// <summary>
        /// Czy wklejony obrazek ma pójść także do modelu (model widzi obrazy, np. <c>vision</c> w Ollamie, Claude, GPT).
        /// Pytane przy każdym wklejeniu; true = miniatura nad polem i obraz wysłany z najbliższym poleceniem (zmniejszony do
        /// <see cref="ClipboardImages.ModelMaxSide"/>). Null albo false = tylko <see cref="ImagePasted"/> (OCR).
        /// </summary>
        public Func<bool> ImagesForModel { get; set; }

        /// <summary>Obrazki czekające na wysłanie z najbliższym poleceniem (miniatury nad polem).</summary>
        public ObservableCollection<PendingImage> PendingImages => _pendingImages;

        private void RemovePendingImage_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is PendingImage image)
            {
                _pendingImages.Remove(image);
            }
        }

        private async void Input_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            var forModel = ImagesForModel?.Invoke() == true;
            if ((ImagePasted == null && !forModel) || !ClipboardImages.IsImage(e.DataObject))
            {
                return; // tekst (także tekst z obrazkiem, np. z Worda) wkleja się jak zwykle
            }

            e.CancelCommand();
            var png = ClipboardImages.ReadPng();
            if (png == null)
            {
                AddInfo("Nie udało się odczytać obrazka ze schowka.");
                return;
            }

            if (forModel)
            {
                _pendingImages.Add(new PendingImage(ClipboardImages.Shrink(png), "obraz " + (_pendingImages.Count + 1)));
                if (ImagePasted == null)
                {
                    AddInfo("Obrazek pójdzie do modelu z najbliższym poleceniem (model widzi obrazy) — dopisz, co z nim zrobić.");
                    return;
                }
            }

            var caret = InputBox.CaretIndex;
            InputBox.IsEnabled = false;
            AddInfo("Rozpoznaję tekst z wklejonego obrazka…");
            try
            {
                var text = await ImagePasted(png) ?? "";
                if (text.Length == 0 && forModel)
                {
                    _entries.RemoveAt(_entries.Count - 1);
                    AddInfo("Obrazek pójdzie do modelu z najbliższym poleceniem (model widzi obrazy; OCR wyłączony) — dopisz, co z nim zrobić.");
                    return;
                }

                var before = InputBox.Text.Substring(0, Math.Min(caret, InputBox.Text.Length));
                var after = InputBox.Text.Substring(Math.Min(caret, InputBox.Text.Length));
                InputBox.Text = before + text + after;
                InputBox.CaretIndex = before.Length + text.Length;
                var firstLine = text.Split('\n').FirstOrDefault(l => l.Trim().Length > 0) ?? "";
                _entries.RemoveAt(_entries.Count - 1);
                var where = forModel ? " — tekst jest w polu polecenia, a obraz pójdzie do modelu (widzi obrazy); dopisz, co z nim zrobić."
                    : " — tekst jest w polu polecenia, dopisz, co z nim zrobić.";
                AddInfo(firstLine.StartsWith("[", StringComparison.Ordinal) ? firstLine.Trim('[', ']') + where : "Obrazek rozpoznany" + where);
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidOperationException || ex is UnauthorizedAccessException)
            {
                AddInfo("OCR wklejonego obrazka nie powiódł się: " + ex.Message + (forModel ? " Obraz i tak pójdzie do modelu." : ""));
            }
            finally
            {
                InputBox.IsEnabled = true;
                InputBox.Focus();
            }
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
