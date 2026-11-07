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
        public ContextUsage(int usedTokens, int maxTokens, bool isEstimate)
        {
            UsedTokens = usedTokens;
            MaxTokens = maxTokens;
            IsEstimate = isEstimate;
        }

        public int UsedTokens { get; }

        public int MaxTokens { get; }

        /// <summary>True, gdy liczba jest szacunkiem (przed pierwszą odpowiedzią modelu albo po zmianie historii).</summary>
        public bool IsEstimate { get; }

        public double Fraction => MaxTokens <= 0 ? 0 : Math.Min(1.0, (double)UsedTokens / MaxTokens);

        public override string ToString()
            => (IsEstimate ? "ok. " : "") + UsedTokens.ToString("N0", Text.Pl) + " / " + MaxTokens.ToString("N0", Text.Pl)
               + " tokenów (" + Math.Round(Fraction * 100).ToString("0", Text.Pl) + "%)";
    }

    /// <summary>Rodzaj zdarzenia sesji (do pokazywania postępu w oknie).</summary>
    public enum HarnessEventKind
    {
        ModelThinking,
        ModelStep,
        ToolStarted,
        ToolFinished,
        Warning,
        Answer,
        Question,
        ContextChanged,
    }

    /// <summary>Zdarzenie sesji. Uwaga: może przyjść z wątku w tle — okno musi przełączyć się na swój wątek.</summary>
    public sealed class HarnessEvent : EventArgs
    {
        public HarnessEvent(HarnessEventKind kind, string text, ExecutedCall call = null, ContextUsage context = null)
        {
            Kind = kind;
            Text = text ?? "";
            Call = call;
            Context = context;
        }

        public HarnessEventKind Kind { get; }

        public string Text { get; }

        public ExecutedCall Call { get; }

        public ContextUsage Context { get; }
    }

    /// <summary>Wywołanie wykonane w turze: co, z jakimi argumentami, z jakim wynikiem.</summary>
    public sealed class ExecutedCall
    {
        public string Tool { get; set; }

        public string ArgumentsJson { get; set; }

        public bool Success { get; set; }

        public string Result { get; set; }

        public int Step { get; set; }

        public bool BuiltIn { get; set; }

        public bool Mutates { get; set; }

        /// <summary>Polecenie oznaczone w profilu jako kończące turę (<c>konczy_ture</c>), np. <c>cd</c>.</summary>
        public bool FinishesTurn { get; set; }

        public override string ToString() => Tool + " " + ArgumentsJson + " → " + (Success ? "" : "[błąd] ") + Result;
    }

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
        public TurnKind Kind { get; set; }

        public string Text { get; set; }

        public IReadOnlyList<ExecutedCall> Calls { get; set; } = new List<ExecutedCall>();

        public IReadOnlyList<string> Warnings { get; set; } = new List<string>();

        /// <summary>Kroki modelu w turze, w surowej postaci JSON (do diagnostyki i testów).</summary>
        public IReadOnlyList<string> Steps { get; set; } = new List<string>();

        /// <summary>False, gdy w odpowiedzi zostały liczby bez pokrycia w wynikach narzędzi.</summary>
        public bool NumbersVerified { get; set; } = true;

        public int ModelCalls { get; set; }

        public TimeSpan Duration { get; set; }

        public ContextUsage Context { get; set; }
    }
}
