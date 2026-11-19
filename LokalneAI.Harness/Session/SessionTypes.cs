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

        /// <summary>
        /// Dostęp do internetu dla narzędzi <c>szukaj_w_internecie</c> i <c>czytaj_strone</c> (profil musi je mieć — <see cref="Profiles.HarnessProfile.WithInternet"/>).
        /// Null = wyłączony: model dostaje błąd zamiast wyników.
        /// </summary>
        public Tools.WebAccess Web { get; set; }

        /// <summary>
        /// Wcześniejsza rozmowa do wznowienia (np. zapisana w pliku przez gospodarza): wiadomości <c>user</c> i <c>assistant</c>
        /// z <see cref="HarnessSession.History"/>, bez polecenia systemowego. Null = nowa rozmowa. Polecenie systemowe składa się
        /// od nowa z bieżącego profilu, więc rozmowę można wznowić z innymi profilami niż te, z którymi się zaczęła.
        /// </summary>
        public IList<Llm.LlmMessage> History { get; set; }

        /// <summary>
        /// Przed pierwszym wywołaniem modelu policz dokładnie tokeny samego polecenia systemowego (zasady, narzędzia, wiedza)
        /// jednym dodatkowym zapytaniem z jednym tokenem odpowiedzi (<see cref="ModelCallMetrics.SystemTokens"/>). Tylko dla
        /// <see cref="Llm.OllamaLlmClient"/> — do chmury nie idzie dodatkowe płatne zapytanie. Kosztuje ok. sekundy czytania promptu,
        /// które i tak by nastąpiło: następne zapytanie ma ten sam początek i Ollama bierze go z pamięci podręcznej.
        /// </summary>
        public bool MeasureSystemPrompt { get; set; }

        /// <summary>
        /// Gospodarz podagentów (<see cref="ISubAgentHost"/>): gdy ustawiony, sesja dostaje narzędzie <c>zlec_podagentowi</c>
        /// i sekcję „Podagenci” w poleceniu systemowym; kilka zleceń z jednego kroku modelu liczy się równolegle. Null = bez podagentów.
        /// Sesji podagenta nie podawaj gospodarza (podagent nie zleca dalej).
        /// </summary>
        public ISubAgentHost SubAgents { get; set; }

        /// <summary>Najwięcej znaków wyniku podagenta, które wracają do modelu agenta głównego (reszta ucięta z dopiskiem); 0 = bez limitu.</summary>
        public int SubAgentResultLimit { get; set; } = 6000;
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
        /// <summary>
        /// Model odpowiedział: dokładne tokeny i czasy tego wywołania (<see cref="HarnessEvent.ModelCall"/>). Przychodzi po
        /// odczytaniu kroku, a przed wywołaniami narzędzi, odpowiedzią albo pytaniem, które ten krok wywołał.
        /// </summary>
        ModelCall,
        /// <summary>Podagent wystartował (<see cref="HarnessEvent.Agent"/>: numer, zadanie, komputer, model).</summary>
        AgentStarted,
        /// <summary>Zdarzenie z sesji podagenta (<see cref="HarnessEvent.Inner"/>) — krok, wywołanie, wynik; <see cref="HarnessEvent.Agent"/> mówi którego.</summary>
        AgentProgress,
        /// <summary>Podagent skończył (<see cref="HarnessEvent.Agent"/>: <see cref="SubAgentInfo.Success"/>, <see cref="SubAgentInfo.Result"/>, czas).</summary>
        AgentFinished,
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

        /// <summary>
        /// Licznik kontekstu (przy <see cref="HarnessEventKind.ContextChanged"/>); przy <see cref="HarnessEventKind.ModelThinking"/>
        /// szacunek promptu przed wysłaniem (<see cref="ContextUsage.IsEstimate"/>), inaczej null.
        /// </summary>
        public ContextUsage Context { get; }

        /// <summary>Tokeny i czasy wywołania modelu (przy <see cref="HarnessEventKind.ModelCall"/>); inaczej null.</summary>
        public ModelCallMetrics ModelCall { get; internal set; }

        /// <summary>Numer kroku modelu w turze (przy <see cref="HarnessEventKind.ModelThinking"/> i <see cref="HarnessEventKind.ModelStep"/>); 0 = nie dotyczy.</summary>
        public int Step { get; internal set; }

        /// <summary>Podagent, którego dotyczy zdarzenie (<see cref="HarnessEventKind.AgentStarted"/>, <see cref="HarnessEventKind.AgentProgress"/>, <see cref="HarnessEventKind.AgentFinished"/>); inaczej null.</summary>
        public SubAgentInfo Agent { get; internal set; }

        /// <summary>Zdarzenie z sesji podagenta (przy <see cref="HarnessEventKind.AgentProgress"/>); inaczej null.</summary>
        public HarnessEvent Inner { get; internal set; }
    }

    /// <summary>
    /// Jedno wywołanie modelu w turze: dokładne liczby tokenów i czasy z serwera (Ollama: <c>prompt_eval_count</c>, <c>eval_count</c>,
    /// <c>load_duration</c>, <c>prompt_eval_duration</c>, <c>eval_duration</c>, <c>total_duration</c>) i czas zmierzony przez sesję.
    /// </summary>
    public sealed class ModelCallMetrics
    {
        /// <summary>Numer wywołania w turze (od 1).</summary>
        public int CallInTurn { get; set; }

        /// <summary>Numer kroku modelu w turze (od 1).</summary>
        public int Step { get; set; }

        /// <summary>Co model zwrócił: <c>wywolania</c>, <c>odpowiedz</c>, <c>pytanie</c> albo <c>bledny_krok</c>.</summary>
        public string Outcome { get; set; }

        /// <summary>Model, który odpowiedział (z serwera); null, gdy serwer nie podał.</summary>
        public string Model { get; set; }

        /// <summary>
        /// Tokeny całego promptu (polecenie systemowe, historia, nowe wiadomości). Ollama liczy tu cały prompt, także część
        /// wziętą z pamięci podręcznej — o tym, ile model naprawdę przeliczył, mówi <see cref="PromptTime"/>.
        /// </summary>
        public int PromptTokens { get; set; }

        /// <summary>Tokeny napisane przez model w tym wywołaniu (krok w JSON).</summary>
        public int OutputTokens { get; set; }

        /// <summary>Szacunek tokenów promptu przed wysłaniem (znaki przeliczone współczynnikiem z poprzednich odpowiedzi).</summary>
        public int EstimatedPromptTokens { get; set; }

        /// <summary>
        /// Tokeny dopisane do rozmowy od poprzedniego wywołania: prompt − (poprzedni prompt + poprzednia odpowiedź modelu),
        /// z kilkoma tokenami szablonu rozmowy. Przy pierwszym wywołaniu sesji: prompt − <see cref="SystemTokens"/>.
        /// Null, gdy nie da się policzyć (brak pomiaru polecenia systemowego, skrócona historia).
        /// </summary>
        public int? AddedTokens { get; set; }

        /// <summary>Co zostało dopisane (<see cref="AddedTokens"/>): np. „pytanie”, „wyniki narzędzi”, „uwagi programu”.</summary>
        public string AddedKind { get; set; }

        /// <summary>Tokeny samego polecenia systemowego (zasady, narzędzia, wiedza) — gdy <see cref="SessionOptions.MeasureSystemPrompt"/>.</summary>
        public int? SystemTokens { get; set; }

        /// <summary>
        /// Czas pomiaru <see cref="SystemTokens"/> tuż przed tym wywołaniem (tylko pierwsze wywołanie sesji): to zapytanie czyta
        /// zasady i często ładuje model, więc samo wywołanie ma potem krótkie „myślenie” (zasady są już w pamięci podręcznej).
        /// </summary>
        public TimeSpan SystemMeasureTime { get; set; }

        /// <summary>Z <see cref="SystemMeasureTime"/>: ładowanie modelu do pamięci.</summary>
        public TimeSpan SystemMeasureLoadTime { get; set; }

        /// <summary>Czas ładowania modelu do pamięci (zero, gdy był załadowany).</summary>
        public TimeSpan LoadTime { get; set; }

        /// <summary>Czas czytania promptu („myślenie” przed pierwszym tokenem; z pamięci podręcznej prawie zero).</summary>
        public TimeSpan PromptTime { get; set; }

        /// <summary>Czas pisania odpowiedzi.</summary>
        public TimeSpan OutputTime { get; set; }

        /// <summary>Cała praca serwera modelu (ładowanie + czytanie + pisanie); zero, gdy serwer nie podaje.</summary>
        public TimeSpan ServerTime { get; set; }

        /// <summary>Czas zapytania zmierzony przez sesję: od wysłania do odebrania odpowiedzi.</summary>
        public TimeSpan WallTime { get; set; }

        /// <summary>Kolejka, sieć i przesyłanie: <see cref="WallTime"/> − <see cref="ServerTime"/> (zero, gdy serwer nie podaje czasów).</summary>
        public TimeSpan QueueTime => ServerTime > TimeSpan.Zero && WallTime > ServerTime ? WallTime - ServerTime : TimeSpan.Zero;
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

        /// <summary>Czas wykonania narzędzia (z czekaniem na zgodę użytkownika, jeśli pytało); zero, gdy odrzucone przed wykonaniem.</summary>
        public TimeSpan Duration { get; set; }

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

        /// <summary>Tokeny i czasy każdego wywołania modelu w tej turze, po kolei.</summary>
        public IReadOnlyList<ModelCallMetrics> ModelCallDetails { get; set; } = new List<ModelCallMetrics>();
    }
}
