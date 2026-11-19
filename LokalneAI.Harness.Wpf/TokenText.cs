using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LokalneAI.Harness.Session;

namespace LokalneAI.Harness.Wpf
{
    /// <summary>
    /// Opisy tokenów i czasów przy wpisach panelu (<see cref="PanelEntry.Metrics"/>) z dokładnych liczb serwera modelu
    /// (<see cref="ModelCallMetrics"/>). Podział tokenów tury: wejście (pytanie), praca (kroki modelu w JSON i wyniki narzędzi
    /// wracające do modelu), odpowiedź (ostatni krok modelu z odpowiedzią albo pytaniem).
    /// </summary>
    internal static class TokenText
    {
        private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

        /// <summary>Objaśnienie pojęć — podpowiedź po najechaniu na liczniki.</summary>
        internal const string Legend =
            "Tokeny to kawałki tekstu, na które model tnie tekst (słownik modelu; po polsku ok. 2 znaki na token).\n"
            + "Liczby są dokładne, z serwera modelu (Ollama: prompt_eval_count, eval_count). Przed odpowiedzią — szacunek (≈).\n\n"
            + "prompt — wszystko, co model czyta w danym wywołaniu: zasady i narzędzia, historia, nowe wiadomości.\n"
            + "  Ollama liczy cały prompt, także część z pamięci podręcznej (ten sam początek co poprzednio czyta się prawie od razu).\n"
            + "→ do modelu — o ile urósł prompt od poprzedniego wywołania (pytanie, wyniki narzędzi, uwagi programu).\n"
            + "model — tokeny napisane przez model: krok w JSON (wywołanie narzędzia, odpowiedź albo pytanie).\n\n"
            + "Σ tury: wejście = pytanie | praca = kroki modelu i wyniki narzędzi | odpowiedź = ostatni krok modelu.\n\n"
            + "Czasy: pisanie pytania (od pierwszego znaku do Enter), ładowanie modelu do pamięci karty, myślenie (czytanie promptu\n"
            + "przed pierwszym tokenem), pisanie (generowanie tokenów), narzędzie (wykonanie polecenia), kolejka i sieć (czekanie\n"
            + "w kolejce i przesyłanie, pokazywane od 1 s), pomiar zasad (raz na sesję: model czyta same zasady i narzędzia, żeby\n"
            + "policzyć ich tokeny — potem pierwsze wywołanie ma je już w pamięci podręcznej i „myśli” krócej).";

        internal static string Tokens(int value) => value.ToString("N0", Pl) + " tok.";

        internal static string Number(int value) => value.ToString("N0", Pl);

        internal static string Time(TimeSpan time)
        {
            if (time < TimeSpan.FromSeconds(1))
            {
                return Math.Max(0, (int)Math.Round(time.TotalMilliseconds)).ToString(Pl) + " ms";
            }

            return time < TimeSpan.FromMinutes(1)
                ? time.TotalSeconds.ToString(time < TimeSpan.FromSeconds(10) ? "0.0" : "0", Pl) + " s"
                : ((int)time.TotalMinutes).ToString(Pl) + " min " + time.Seconds.ToString("00", Pl) + " s";
        }

        /// <summary>„≈ 3 400 tok. promptu” przy „Model pracuje…”, zanim przyjdą dokładne liczby.</summary>
        internal static string Estimate(ContextUsage estimate)
            => "≈ " + Tokens(estimate.UsedTokens) + " promptu (szacunek; dokładnie po odpowiedzi)";

        /// <summary>Przy poleceniu użytkownika: z czego składa się pierwszy prompt tury.</summary>
        internal static string FirstPrompt(ModelCallMetrics m, bool firstInSession)
        {
            var lines = new List<string>();
            if (firstInSession && m.SystemTokens.HasValue)
            {
                lines.Add("zasady i narzędzia: " + Tokens(m.SystemTokens.Value));
            }

            if (m.SystemMeasureTime > TimeSpan.Zero)
            {
                lines.Add("pomiar zasad: " + Time(m.SystemMeasureTime)
                    + (m.SystemMeasureLoadTime >= TimeSpan.FromMilliseconds(300) ? " (w tym ładowanie modelu " + Time(m.SystemMeasureLoadTime) + ")" : ""));
            }

            if (m.AddedTokens.HasValue)
            {
                lines.Add(Label(m.AddedKind, "pytanie") + ": " + Tokens(m.AddedTokens.Value));
            }

            if (m.PromptTokens > 0)
            {
                lines.Add("prompt: " + Tokens(m.PromptTokens) + Guess(m));
            }

            return string.Join("\n", lines);
        }

        /// <summary>Przy wpisie po kroku modelu: ile napisał, ile czytał i jak długo.</summary>
        internal static string Model(ModelCallMetrics m)
        {
            var lines = new List<string> { "model: " + Tokens(m.OutputTokens) + " (" + OutcomeText(m.Outcome) + ")" };
            if (m.ServerTime > TimeSpan.Zero)
            {
                var parts = new List<string>();
                if (m.LoadTime >= TimeSpan.FromMilliseconds(300))
                {
                    parts.Add("ładowanie " + Time(m.LoadTime));
                }

                parts.Add("myślenie " + Time(m.PromptTime));
                parts.Add("pisanie " + Time(m.OutputTime));
                lines.Add(string.Join(" · ", parts));
                if (m.QueueTime >= TimeSpan.FromSeconds(1))
                {
                    lines.Add("kolejka i sieć " + Time(m.QueueTime));
                }
            }
            else
            {
                lines.Add("czas " + Time(m.WallTime));
            }

            if (m.CallInTurn > 1 && m.PromptTokens > 0)
            {
                lines.Add("prompt: " + Tokens(m.PromptTokens) + Guess(m));
            }

            return string.Join("\n", lines);
        }

        /// <summary>Przy ostatnim wpisie kroku: o ile urósł prompt następnego wywołania (wyniki narzędzi, uwagi programu).</summary>
        internal static string Added(ModelCallMetrics next)
            => next.AddedTokens.HasValue
                ? "→ do modelu: " + Tokens(next.AddedTokens.Value) + " (" + Label(next.AddedKind, "wyniki narzędzi") + ")"
                : null;

        /// <summary>Podsumowanie tury: Σ wejście | praca | odpowiedź, prompt łącznie, kontekst i czasy.</summary>
        internal static string Turn(TurnResult turn, TimeSpan? typing)
        {
            var calls = turn.ModelCallDetails ?? new List<ModelCallMetrics>();
            var lines = new List<string>();
            if (calls.Count > 0)
            {
                var last = calls[calls.Count - 1];
                var lastIsAnswer = last.Outcome == "odpowiedz" || last.Outcome == "pytanie";
                var input = calls[0].AddedTokens;
                var work = calls.Skip(1).Sum(c => c.AddedTokens ?? 0) + calls.Take(calls.Count - 1).Sum(c => c.OutputTokens)
                    + (lastIsAnswer ? 0 : last.OutputTokens);
                var output = lastIsAnswer ? last.OutputTokens : 0;
                var unknown = calls.Skip(1).Any(c => !c.AddedTokens.HasValue);
                lines.Add("Σ wejście " + (input.HasValue ? Number(input.Value) : "?") + " | praca " + (unknown ? "≥ " : "") + Number(work)
                    + " | odpowiedź " + Number(output) + " tok.");
                lines.Add("model przeczytał " + Tokens(calls.Sum(c => c.PromptTokens)) + " w " + calls.Count + " "
                    + (calls.Count == 1 ? "wywołaniu" : "wywołaniach"));
            }

            if (turn.Context != null && !turn.Context.IsEstimate)
            {
                lines.Add("kontekst: " + Number(turn.Context.UsedTokens) + " / " + Tokens(turn.Context.MaxTokens));
            }

            var times = new List<string>();
            if (typing.HasValue)
            {
                times.Add("pisanie pytania " + Time(typing.Value));
            }

            var load = Sum(calls.Select(c => c.LoadTime + c.SystemMeasureLoadTime));
            if (load >= TimeSpan.FromMilliseconds(300))
            {
                times.Add("ładowanie " + Time(load));
            }

            var measure = Sum(calls.Select(c => c.SystemMeasureTime - c.SystemMeasureLoadTime));
            if (measure >= TimeSpan.FromMilliseconds(100))
            {
                times.Add("pomiar zasad " + Time(measure));
            }

            if (calls.Any(c => c.ServerTime > TimeSpan.Zero))
            {
                times.Add("myślenie " + Time(Sum(calls.Select(c => c.PromptTime))));
                times.Add("pisanie " + Time(Sum(calls.Select(c => c.OutputTime))));
            }
            else if (calls.Count > 0)
            {
                times.Add("model " + Time(Sum(calls.Select(c => c.WallTime))));
            }

            var tools = Sum((turn.Calls ?? new List<ExecutedCall>()).Select(c => c.Duration));
            if (turn.Calls != null && turn.Calls.Count > 0)
            {
                times.Add("narzędzia " + Time(tools));
            }

            var queue = Sum(calls.Select(c => c.QueueTime));
            if (queue >= TimeSpan.FromSeconds(1))
            {
                times.Add("kolejka i sieć " + Time(queue));
            }

            if (times.Count > 0)
            {
                lines.Add(string.Join(" · ", times));
            }

            return string.Join("\n", lines);
        }

        internal static string Append(string existing, string line)
            => string.IsNullOrEmpty(line) ? existing : string.IsNullOrEmpty(existing) ? line : existing + "\n" + line;

        private static string Guess(ModelCallMetrics m)
            => m.EstimatedPromptTokens > 0 ? " (szacunek był " + Number(m.EstimatedPromptTokens) + ")" : "";

        private static string Label(string kind, string fallback) => string.IsNullOrWhiteSpace(kind) ? fallback : kind;

        private static string OutcomeText(string outcome)
        {
            switch (outcome)
            {
                case "wywolania": return "wywołanie narzędzi";
                case "odpowiedz": return "odpowiedź";
                case "pytanie": return "pytanie";
                case "bledny_krok": return "błędny krok";
                default: return outcome;
            }
        }

        private static TimeSpan Sum(IEnumerable<TimeSpan> times) => times.Aggregate(TimeSpan.Zero, (a, b) => a + b);
    }
}
