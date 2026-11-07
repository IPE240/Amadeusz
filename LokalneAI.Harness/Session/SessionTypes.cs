using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LokalneAI.Harness.Tools;

namespace LokalneAI.Harness.Session
{
    /// <summary>Ustawienia jednej mini-sesji.</summary>
    public sealed class SessionOptions
    {
        /// <summary>
        /// Zmienne sesji podstawiane w argumentach narzędzi, np. <c>ja</c> → uuid osoby pytającej.
        /// Model pisze <c>{{ja}}</c>, a biblioteka wstawia wartość — model nie przepisuje długich identyfikatorów.
        /// </summary>
        public IDictionary<string, string> Variables { get; set; } = new Dictionary<string, string>();

        /// <summary>Tekst o sytuacji (kto pyta, jaki model jest otwarty) doklejany do polecenia systemowego.</summary>
        public string ContextNote { get; set; }

        /// <summary>Pytanie o zgodę przed poleceniem zmieniającym model/dane; null = bez pytania.</summary>
        public Func<ToolCall, CancellationToken, Task<bool>> ConfirmMutation { get; set; }

        /// <summary>Rola wiadomości z wynikami narzędzi: <c>user</c> działa z każdym modelem.</summary>
        public string ToolResultRole { get; set; } = "user";

        /// <summary>Od jakiego zapełnienia kontekstu (0–1) skracać starszą część sesji.</summary>
        public double CompactAt { get; set; } = 0.85;

        /// <summary>Ile najnowszych wiadomości zostaje w całości po skróceniu.</summary>
        public int KeepRecentMessages { get; set; } = 8;
    }

    /// <summary>Zajętość kontekstu modelu — do licznika w oknie gospodarza.</summary>
    public sealed class ContextUsage
    {
        /// <summary>Tworzy stan licznika kontekstu.</summary>
        public ContextUsage(int usedTokens, int maxTokens, bool isEstimate)
        {
            UsedTokens = usedTokens;
            MaxTokens = maxTokens;
            IsEstimate = isEstimate;
        }

        /// <summary>Zajęte tokeny: polecenie systemowe, historia i ostatnia odpowiedź.</summary>
        public int UsedTokens { get; }

        /// <summary>Długość kontekstu modelu.</summary>
        public int MaxTokens { get; }

        /// <summary>True, gdy liczba jest szacunkiem (przed pierwszą odpowiedzią modelu albo po zmianie historii).</summary>
        public bool IsEstimate { get; }

        /// <summary>Zapełnienie od 0 do 1.</summary>
        public double Fraction => MaxTokens <= 0 ? 0 : Math.Min(1.0, (double)UsedTokens / MaxTokens);

        /// <summary>Np. „8 063 / 16 384 tokenów (49%)”; szacunek z „ok.” na początku.</summary>
        public override string ToString()
            => (IsEstimate ? "ok. " : "") + UsedTokens.ToString("N0", Text.Pl) + " / " + MaxTokens.ToString("N0", Text.Pl)
               + " tokenów (" + Math.Round(Fraction * 100).ToString("0", Text.Pl) + "%)";
    }

    /// <summary>Rodzaj zdarzenia sesji (do pokazywania postępu w oknie).</summary>
    public enum HarnessEventKind
    {
        /// <summary>Model zaczyna krok („Model pracuje (krok N)…”).</summary>
        ModelThinking,
        /// <summary>Surowy krok modelu (JSON), do diagnostyki.</summary>
        ModelStep,
        /// <summary>Wywołanie narzędzia startuje (<see cref="HarnessEvent.Call"/>).</summary>
        ToolStarted,
        /// <summary>Wywołanie się skończyło, także odrzucone albo z błędem (wynik w <see cref="HarnessEvent.Call"/>).</summary>
        ToolFinished,
        /// <summary>Ostrzeżenie: niepoprawny krok modelu, liczby bez pokrycia, skrócenie historii.</summary>
        Warning,
        /// <summary>Odpowiedź końcowa tury.</summary>
        Answer,
        /// <summary>Pytanie modelu do użytkownika.</summary>
        Question,
        /// <summary>Zmienił się licznik kontekstu (<see cref="HarnessEvent.Context"/>).</summary>
        ContextChanged,
    }

    /// <summary>Zdarzenie sesji. Uwaga: może przyjść z wątku w tle — okno musi przełączyć się na swój wątek.</summary>
    public sealed class HarnessEvent : EventArgs
    {
        /// <summary>Tworzy zdarzenie.</summary>
        public HarnessEvent(HarnessEventKind kind, string text, ExecutedCall call = null, ContextUsage context = null)
        {
            Kind = kind;
            Text = text ?? "";
            Call = call;
            Context = context;
        }

        /// <summary>Rodzaj zdarzenia.</summary>
        public HarnessEventKind Kind { get; }

        /// <summary>Tekst do pokazania; nigdy null.</summary>
        public string Text { get; }

        /// <summary>Wywołanie (przy <see cref="HarnessEventKind.ToolStarted"/> i <see cref="HarnessEventKind.ToolFinished"/>); inaczej null.</summary>
        public ExecutedCall Call { get; }

        /// <summary>Licznik kontekstu (przy <see cref="HarnessEventKind.ContextChanged"/>); inaczej null.</summary>
        public ContextUsage Context { get; }
    }

    /// <summary>Wywołanie wykonane w turze: co, z jakimi argumentami, z jakim wynikiem.</summary>
    public sealed class ExecutedCall
    {
        /// <summary>Nazwa narzędzia.</summary>
        public string Tool { get; set; }

        /// <summary>Argumenty jako zwarty JSON, po podstawieniu zmiennych sesji.</summary>
        public string ArgumentsJson { get; set; }

        /// <summary>Czy wywołanie się udało.</summary>
        public bool Success { get; set; }

        /// <summary>Tekst wyniku, który dostał model.</summary>
        public string Result { get; set; }

        /// <summary>Numer kroku modelu w turze (od 1).</summary>
        public int Step { get; set; }

        /// <summary>Narzędzie wbudowane biblioteki (<c>kalkulator</c>, <c>szukaj_wiedzy</c>), nie gospodarza.</summary>
        public bool BuiltIn { get; set; }

        /// <summary>Wywołanie coś zmieniało i wymagało zgody.</summary>
        public bool Mutates { get; set; }

        /// <summary>Polecenie oznaczone w profilu jako kończące turę (<c>konczy_ture</c>), np. <c>cd</c>.</summary>
        public bool FinishesTurn { get; set; }

        /// <summary>Np. „wstaw_slup {…} → OK id=1001”.</summary>
        public override string ToString() => Tool + " " + ArgumentsJson + " → " + (Success ? "" : "[błąd] ") + Result;
    }

    /// <summary>Jak skończyła się tura.</summary>
    public enum TurnKind
    {
        /// <summary>Model zakończył turę odpowiedzią.</summary>
        Answer,

        /// <summary>Model pyta użytkownika o brakujące dane.</summary>
        Question,

        /// <summary>Nie udało się (limit kroków, błąd modelu).</summary>
        Failed,
    }

    /// <summary>Wynik jednego polecenia użytkownika.</summary>
    public sealed class TurnResult
    {
        /// <summary>Odpowiedź, pytanie albo niepowodzenie.</summary>
        public TurnKind Kind { get; set; }

        /// <summary>Tekst dla użytkownika: odpowiedź, pytanie albo opis błędu.</summary>
        public string Text { get; set; }

        /// <summary>Wywołania wykonane w tej turze, po kolei.</summary>
        public IReadOnlyList<ExecutedCall> Calls { get; set; } = new List<ExecutedCall>();

        /// <summary>Ostrzeżenia tury: niepoprawny krok modelu, liczby bez pokrycia, skrócenie historii.</summary>
        public IReadOnlyList<string> Warnings { get; set; } = new List<string>();

        /// <summary>Kroki modelu w turze, w surowej postaci JSON (do diagnostyki i testów).</summary>
        public IReadOnlyList<string> Steps { get; set; } = new List<string>();

        /// <summary>False, gdy w odpowiedzi zostały liczby bez pokrycia w wynikach narzędzi.</summary>
        public bool NumbersVerified { get; set; } = true;

        /// <summary>Ile razy w tej turze wołano model.</summary>
        public int ModelCalls { get; set; }

        /// <summary>Czas całej tury.</summary>
        public TimeSpan Duration { get; set; }

        /// <summary>Licznik kontekstu po turze.</summary>
        public ContextUsage Context { get; set; }
    }
}
