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

        /// <summary>Znaczek przy wywołaniu: ⏳ trwa, ✓ OK, ✗ błąd.</summary>
        public string Mark => Kind != PanelEntryKind.Call ? "" : _success == null ? "⏳" : _success == true ? "✓" : "✗";
    }
}
