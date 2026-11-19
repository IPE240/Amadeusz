using System.ComponentModel;

namespace LokalneAI.Harness.Wpf
{
    /// <summary>Rodzaj wpisu w rozmowie panelu.</summary>
    public enum PanelEntryKind
    {
        /// <summary>Polecenie użytkownika.</summary>
        User,
        /// <summary>Wywołanie narzędzia z wynikiem.</summary>
        Call,
        /// <summary>Odpowiedź.</summary>
        Answer,
        /// <summary>Pytanie modelu.</summary>
        Question,
        /// <summary>Ostrzeżenie.</summary>
        Warning,
        /// <summary>Błąd.</summary>
        Error,
        /// <summary>Informacja (czas tury, liczba wywołań modelu).</summary>
        Info,
        /// <summary>Pytanie o zgodę przed poleceniem, które coś zmienia — z przyciskami Tak/Nie w rozmowie.</summary>
        Confirm,
        /// <summary>Krok modelu: co zwrócił w danym kroku (wywołania, odpowiedź, pytanie, plan); widoczny przy <see cref="HarnessPanel.ShowModelSteps"/>.</summary>
        Step,
        /// <summary>Podagent: zadanie, komputer, model i wynik; w <see cref="PanelEntry.Children"/> jego przebieg (rozwijane drzewo).</summary>
        Agent,
    }

    /// <summary>Jeden wpis w rozmowie: polecenie użytkownika, wywołanie narzędzia, odpowiedź…</summary>
    public sealed class PanelEntry : INotifyPropertyChanged
    {
        private string _text;
        private string _detail;
        private bool? _success;

        /// <summary>Tworzy wpis.</summary>
        public PanelEntry(PanelEntryKind kind, string text, string detail = null)
        {
            Kind = kind;
            _text = text;
            _detail = detail;
        }

        /// <summary>Zmiana tekstu, szczegółów albo wyniku (odświeża widok).</summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>Rodzaj wpisu.</summary>
        public PanelEntryKind Kind { get; }

        /// <summary>Główny tekst wpisu.</summary>
        public string Text
        {
            get => _text;
            set
            {
                _text = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text)));
            }
        }

        /// <summary>Druga linia: argumenty wywołania albo wynik.</summary>
        public string Detail
        {
            get => _detail;
            set
            {
                _detail = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Detail)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasDetail)));
            }
        }

        /// <summary>Czy jest druga linia (<see cref="Detail"/>).</summary>
        public bool HasDetail => !string.IsNullOrEmpty(_detail);

        private string _metrics;

        /// <summary>
        /// Tokeny i czasy przy wpisie (np. „model: 41 tok. (wywołanie)”, „narzędzie: 50 ms”), w kilku liniach; widać je, gdy
        /// <see cref="HarnessPanel.ShowTokens"/>. Panel wypełnia je sam ze zdarzeń sesji; null = brak.
        /// </summary>
        public string Metrics
        {
            get => _metrics;
            set
            {
                _metrics = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Metrics)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasMetrics)));
            }
        }

        /// <summary>Czy są tokeny i czasy (<see cref="Metrics"/>).</summary>
        public bool HasMetrics => !string.IsNullOrEmpty(_metrics);

        /// <summary>Wynik wywołania: true = OK, false = błąd, null = jeszcze trwa.</summary>
        public bool? Success
        {
            get => _success;
            set
            {
                _success = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Success)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Mark)));
            }
        }

        /// <summary>Znaczek przy wywołaniu: ⏳ trwa, ✓ OK, ✗ błąd; przy pytaniu o zgodę: ? czeka, ✓ tak, ✗ nie.</summary>
        public string Mark => Kind == PanelEntryKind.Confirm ? (_success == null ? "?" : _success == true ? "✓" : "✗")
            : Kind != PanelEntryKind.Call ? "" : _success == null ? "⏳" : _success == true ? "✓" : "✗";

        private System.Threading.Tasks.TaskCompletionSource<bool> _answer;

        /// <summary>Pytanie o zgodę czeka na kliknięcie (przyciski Tak/Nie widoczne).</summary>
        public bool IsPending => _answer != null && !_answer.Task.IsCompleted;

        /// <summary>Polecenie może coś zniszczyć (kasuje) — przycisk „Nie” jest wyróżniony.</summary>
        public bool Dangerous { get; set; }

        /// <summary>Napis przycisku zgody (domyślnie „Tak, wykonaj”).</summary>
        public string YesText { get; set; } = "Tak, wykonaj";

        /// <summary>Napis przycisku odmowy (domyślnie „Nie”).</summary>
        public string NoText { get; set; } = "Nie";

        private System.Collections.ObjectModel.ObservableCollection<PanelEntry> _children;
        private bool _isExpanded;

        /// <summary>
        /// Wpisy podrzędne (wpis <see cref="PanelEntryKind.Agent"/>: przebieg podagenta — polecenie, kroki, wywołania, odpowiedź).
        /// Tworzone przy pierwszym użyciu; <see cref="HasChildren"/> mówi, czy coś w nich jest.
        /// </summary>
        public System.Collections.ObjectModel.ObservableCollection<PanelEntry> Children
        {
            get
            {
                if (_children == null)
                {
                    _children = new System.Collections.ObjectModel.ObservableCollection<PanelEntry>();
                    _children.CollectionChanged += (s, e) =>
                    {
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasChildren)));
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ChildrenText)));
                    };
                }

                return _children;
            }
        }

        /// <summary>Czy wpis ma wpisy podrzędne.</summary>
        public bool HasChildren => _children != null && _children.Count > 0;

        /// <summary>Napis przycisku rozwijania: „▸ przebieg (12 wpisów)” albo „▾ zwiń przebieg”.</summary>
        public string ChildrenText
        {
            get
            {
                var count = _children == null ? 0 : _children.Count;
                return _isExpanded ? "▾ zwiń przebieg" : "▸ przebieg (" + count + (count == 1 ? " wpis" : count >= 2 && count <= 4 ? " wpisy" : " wpisów") + ")";
            }
        }

        /// <summary>Wpisy podrzędne rozwinięte (drzewo); domyślnie zwinięte — widać tylko nagłówek i wynik.</summary>
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                _isExpanded = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ChildrenText)));
            }
        }

        /// <summary>Zaczyna czekać na odpowiedź Tak/Nie (<see cref="PanelEntryKind.Confirm"/>).</summary>
        internal System.Threading.Tasks.Task<bool> Ask()
        {
            _answer = new System.Threading.Tasks.TaskCompletionSource<bool>();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPending)));
            return _answer.Task;
        }

        /// <summary>Odpowiedź na pytanie o zgodę (kliknięcie albo Stop = nie). Druga odpowiedź nic nie zmienia.</summary>
        internal void Resolve(bool yes, string note)
        {
            if (_answer == null || !_answer.TrySetResult(yes))
            {
                return;
            }

            Success = yes;
            Detail = note;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPending)));
        }
    }
}
