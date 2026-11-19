using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LokalneAI.Harness.Llm;

namespace LokalneAI.Harness.Session
{
    /// <summary>
    /// Zlecenie dla podagenta (narzędzie wbudowane <c>zlec_podagentowi</c>): osobna sesja z własnym modelem, zwykle na innym
    /// komputerze biura. Podagent nie widzi rozmowy agenta głównego — dostaje tylko <see cref="Task"/> i <see cref="Context"/>.
    /// </summary>
    public sealed class SubAgentRequest
    {
        /// <summary>Numer podagenta w sesji (od 1) — do kafelków i wpisów w oknie.</summary>
        public int Number { get; set; }

        /// <summary>Co ma zrobić (pole <c>zadanie</c>) — samodzielny opis: co, gdzie, w jakiej postaci oddać wynik.</summary>
        public string Task { get; set; }

        /// <summary>
        /// Umiejętności podagenta (pole <c>umiejetnosci</c>, nazwy folderów z <c>profile\</c>); null albo pusta lista =
        /// gospodarz dobiera sam (zwykle te same, co sesja główna).
        /// </summary>
        public IReadOnlyList<string> Skills { get; set; }

        /// <summary>Dane do zadania (pole <c>kontekst</c>): fragmenty wyników, listy, liczby; null = brak.</summary>
        public string Context { get; set; }
    }

    /// <summary>Wynik pracy podagenta — wraca do modelu agenta głównego jako wynik narzędzia.</summary>
    public sealed class SubAgentResult
    {
        /// <summary>Czy podagent skończył odpowiedzią (nie pytaniem, błędem ani przerwaniem).</summary>
        public bool Success { get; set; }

        /// <summary>Odpowiedź podagenta albo opis, dlaczego się nie udało.</summary>
        public string Text { get; set; }

        /// <summary>Komputer, który liczył.</summary>
        public string Machine { get; set; }

        /// <summary>Model, którym liczył.</summary>
        public string Model { get; set; }

        /// <summary>Czas całej pracy podagenta.</summary>
        public TimeSpan Duration { get; set; }

        /// <summary>Pełny wynik tury podagenta (wywołania, kroki, ostrzeżenia); null, gdy tura się nie odbyła.</summary>
        public TurnResult Turn { get; set; }
    }

    /// <summary>Umiejętność, którą gospodarz może dać podagentowi (do listy w poleceniu systemowym agenta głównego).</summary>
    public sealed class SubAgentSkill
    {
        /// <summary>Tworzy opis umiejętności.</summary>
        public SubAgentSkill(string name, string description)
        {
            Name = name ?? "";
            Description = description ?? "";
        }

        /// <summary>Nazwa (folder z <c>profile\</c>), np. <c>pliki</c>.</summary>
        public string Name { get; }

        /// <summary>Krótki opis po polsku (z <c>opis</c> w <c>profil.json</c>).</summary>
        public string Description { get; }
    }

    /// <summary>
    /// Stan jednego podagenta — do kafelka nad polem polecenia i wpisu w rozmowie. Gospodarz (okno) czyta go przy każdym
    /// zdarzeniu <see cref="HarnessEventKind.AgentProgress"/>; zmiany pól nie są zdarzeniami same w sobie.
    /// </summary>
    public sealed class SubAgentInfo
    {
        /// <summary>Numer podagenta w sesji (od 1).</summary>
        public int Number { get; set; }

        /// <summary>Zadanie (pełny tekst).</summary>
        public string Task { get; set; }

        /// <summary>Umiejętności podagenta, np. „pliki, rfem”.</summary>
        public string Skills { get; set; }

        /// <summary>Komputer, który liczy, np. „ten komputer” albo „PC-ODS12”.</summary>
        public string Machine { get; set; }

        /// <summary>Model podagenta (krótka nazwa).</summary>
        public string Model { get; set; }

        /// <summary>Co teraz robi: „Model pracuje (krok 2)…”, „→ czytaj_plik”, „odpowiada”.</summary>
        public string Status { get; set; }

        /// <summary>Bieżący krok modelu podagenta (od 1); 0 przed pierwszym.</summary>
        public int Step { get; set; }

        /// <summary>Czy jeszcze pracuje.</summary>
        public bool IsRunning { get; set; }

        /// <summary>Jak skończył: true = odpowiedź, false = błąd, pytanie albo przerwanie; null = jeszcze pracuje.</summary>
        public bool? Success { get; set; }

        /// <summary>Wynik (odpowiedź albo opis błędu) po zakończeniu; null w trakcie.</summary>
        public string Result { get; set; }

        /// <summary>Czas pracy (po zakończeniu; w trakcie — od startu).</summary>
        public TimeSpan Duration { get; set; }

        /// <summary>Kiedy wystartował (UTC) — do licznika sekund na kafelku.</summary>
        public DateTime Started { get; set; }

        /// <summary>Przerywa tego jednego podagenta (przycisk „Przerwij” na kafelku); null, gdy jeszcze nie wystartował.</summary>
        public Action Cancel { get; set; }

        /// <summary>Jedna linia do okna: „Podagent 2 · PC-ODS12 · gemma4:12b · pliki”.</summary>
        public string Describe()
            => "Podagent " + Number + " · " + (Machine ?? "?") + " · " + (Model ?? "?") + (string.IsNullOrWhiteSpace(Skills) ? "" : " · " + Skills);

        /// <summary>Uaktualnia <see cref="Status"/> i <see cref="Step"/> ze zdarzenia sesji podagenta.</summary>
        public void Observe(HarnessEvent e)
        {
            if (e == null)
            {
                return;
            }

            switch (e.Kind)
            {
                case HarnessEventKind.ModelThinking:
                    if (e.Step > 0)
                    {
                        Step = e.Step;
                    }

                    Status = e.Text;
                    break;
                case HarnessEventKind.ToolStarted:
                    Status = "→ " + (e.Call != null ? e.Call.Tool : e.Text);
                    break;
                case HarnessEventKind.ToolFinished:
                    Status = (e.Call != null ? e.Call.Tool : e.Text) + (e.Call == null || e.Call.Success ? " ✓" : " ✗");
                    break;
                case HarnessEventKind.Answer:
                    Status = "odpowiedział";
                    break;
                case HarnessEventKind.Question:
                    Status = "pyta zamiast odpowiedzieć";
                    break;
                case HarnessEventKind.Warning:
                    Status = "uwaga: " + e.Text;
                    break;
            }
        }
    }

    /// <summary>
    /// Gospodarz podagentów: dobiera komputer i model, buduje sesję podagenta (profil, wykonawca) i uruchamia ją przez
    /// <see cref="SubAgentRunner.RunAsync"/>. Podłączany przez <see cref="SessionOptions.SubAgents"/>; wtedy sesja dostaje
    /// narzędzie <c>zlec_podagentowi</c> (<see cref="Tools.BuiltInTools.DelegateName"/>), a kilka zleceń z jednego kroku
    /// modelu wykonuje się równolegle. Sesja podagenta nie dostaje własnego gospodarza (podagenci nie zlecają dalej).
    /// </summary>
    public interface ISubAgentHost
    {
        /// <summary>Umiejętności, które podagent może dostać (pole <c>umiejetnosci</c>); sesja sprawdza nazwy od modelu.</summary>
        IReadOnlyList<SubAgentSkill> Skills { get; }

        /// <summary>Zdanie o komputerach podagentów do polecenia systemowego, np. „ten komputer (RTX 4060, 8 GB), PC-ODS12 (RTX 4090, 24 GB)”; null = bez zdania.</summary>
        string Describe();

        /// <summary>
        /// Uruchamia podagenta i czeka na jego wynik. <paramref name="report"/> dostaje zdarzenia <see cref="HarnessEventKind.AgentStarted"/>,
        /// <see cref="HarnessEventKind.AgentProgress"/> i <see cref="HarnessEventKind.AgentFinished"/> (sesja główna przekazuje je
        /// do swojego <see cref="HarnessSession.Progress"/>). Przerwanie <paramref name="cancellationToken"/> (Stop całej tury)
        /// kończy <see cref="OperationCanceledException"/>; inne błędy wracają w <see cref="SubAgentResult"/>.
        /// </summary>
        Task<SubAgentResult> RunAsync(SubAgentRequest request, Action<HarnessEvent> report, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Przebieg pracy jednego podagenta na gotowej sesji: zdarzenia sesji podagenta idą dalej jako
    /// <see cref="HarnessEventKind.AgentProgress"/>, przycisk „Przerwij” działa przez <see cref="SubAgentInfo.Cancel"/>,
    /// a pytanie podagenta do użytkownika wraca agentowi głównemu jako nieudany wynik (podagent nie rozmawia z użytkownikiem).
    /// </summary>
    public static class SubAgentRunner
    {
        /// <summary>
        /// Prowadzi podagenta do końca tury. <paramref name="session"/> to sesja podagenta (bez <see cref="SessionOptions.SubAgents"/>),
        /// <paramref name="info"/> — wypełnione pola <see cref="SubAgentInfo.Number"/>, <see cref="SubAgentInfo.Machine"/>,
        /// <see cref="SubAgentInfo.Model"/>, <see cref="SubAgentInfo.Skills"/>. Sesji nie zamyka — robi to gospodarz.
        /// Stop całej tury (<paramref name="turnCancellation"/>) = <see cref="OperationCanceledException"/>; przerwanie samego
        /// podagenta (kafelek) i błędy modelu = wynik z <see cref="SubAgentResult.Success"/> false.
        /// </summary>
        public static async Task<SubAgentResult> RunAsync(HarnessSession session, SubAgentRequest request, SubAgentInfo info,
            Action<HarnessEvent> report, CancellationToken turnCancellation)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            info = info ?? new SubAgentInfo { Number = request.Number };
            info.Task = request.Task;
            info.Started = DateTime.UtcNow;
            info.IsRunning = true;
            info.Status = "startuje";
            var clock = Stopwatch.StartNew();
            using (var own = CancellationTokenSource.CreateLinkedTokenSource(turnCancellation))
            {
                info.Cancel = () => { try { own.Cancel(); } catch (ObjectDisposedException) { } };
                EventHandler<HarnessEvent> forward = (s, e) =>
                {
                    info.Observe(e);
                    info.Duration = clock.Elapsed;
                    Report(report, new HarnessEvent(HarnessEventKind.AgentProgress, e.Text, e.Call, e.Context) { Agent = info, Inner = e, Step = e.Step, ModelCall = e.ModelCall });
                };
                session.Progress += forward;
                Report(report, new HarnessEvent(HarnessEventKind.AgentStarted, info.Describe()) { Agent = info });
                var result = new SubAgentResult { Machine = info.Machine, Model = info.Model };
                try
                {
                    var turn = await session.SendAsync(BuildTask(request), own.Token).ConfigureAwait(false);
                    result.Turn = turn;
                    result.Success = turn.Kind == TurnKind.Answer;
                    result.Text = turn.Kind == TurnKind.Question ? "Podagent pyta zamiast odpowiedzieć: " + turn.Text
                        : turn.Kind == TurnKind.Failed ? "Podagent nie skończył: " + turn.Text
                        : turn.Text;
                }
                catch (OperationCanceledException)
                {
                    if (turnCancellation.IsCancellationRequested)
                    {
                        info.IsRunning = false;
                        info.Success = false;
                        info.Status = "przerwany (Stop)";
                        info.Result = "Przerwany razem z całym poleceniem.";
                        info.Duration = clock.Elapsed;
                        session.Progress -= forward;
                        Report(report, new HarnessEvent(HarnessEventKind.AgentFinished, info.Describe() + ": " + info.Result) { Agent = info });
                        throw;
                    }

                    result.Success = false;
                    result.Text = "Podagent przerwany przez użytkownika (przycisk Przerwij).";
                }
                catch (LlmException e)
                {
                    result.Success = false;
                    result.Text = "Model podagenta nie odpowiedział: " + e.Message;
                }
                catch (Exception e) when (!(e is OperationCanceledException))
                {
                    result.Success = false;
                    result.Text = "Błąd podagenta: " + e.Message;
                }
                finally
                {
                    session.Progress -= forward;
                    info.Cancel = null;
                }

                result.Duration = clock.Elapsed;
                info.Duration = clock.Elapsed;
                info.IsRunning = false;
                info.Success = result.Success;
                info.Result = result.Text;
                info.Status = result.Success ? "gotowe" : "nie udało się";
                Report(report, new HarnessEvent(HarnessEventKind.AgentFinished, info.Describe() + ": " + info.Status) { Agent = info });
                return result;
            }
        }

        /// <summary>Polecenie dla sesji podagenta złożone ze zlecenia: zadanie, zasady odpowiedzi i dane.</summary>
        public static string BuildTask(SubAgentRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var builder = new StringBuilder();
            builder.AppendLine("Zadanie zlecone przez agenta głównego (nie widzisz jego rozmowy z użytkownikiem). Odpowiedz zwięźle samym wynikiem — "
                + "agent główny przekaże go dalej. Nie zadawaj pytań: gdy czegoś brakuje, zrób, co się da, i napisz, czego zabrakło.");
            builder.AppendLine();
            builder.AppendLine((request.Task ?? "").Trim());
            if (!string.IsNullOrWhiteSpace(request.Context))
            {
                builder.AppendLine().AppendLine("Dane do zadania:").AppendLine(request.Context.Trim());
            }

            return builder.ToString().TrimEnd();
        }

        /// <summary>Tekst wyniku podagenta dla modelu agenta głównego: nagłówek z numerem, komputerem, modelem i czasem, potem treść (ucięta do limitu).</summary>
        public static string FormatResult(SubAgentResult result, int number, int limit)
        {
            if (result == null)
            {
                return "BŁĄD podagent " + number + ": brak wyniku.";
            }

            var text = (result.Text ?? "").Trim();
            if (limit > 0 && text.Length > limit)
            {
                text = text.Substring(0, limit) + "… (wynik ucięty do " + limit + " znaków)";
            }

            var where = string.Join(", ", new[] { result.Machine, result.Model, result.Duration > TimeSpan.Zero ? Math.Round(result.Duration.TotalSeconds).ToString("0", Text.Pl) + " s" : null }
                .Where(p => !string.IsNullOrWhiteSpace(p)));
            return (result.Success ? "Podagent " + number : "BŁĄD podagent " + number) + (where.Length > 0 ? " (" + where + ")" : "") + ": " + text;
        }

        /// <summary>Nazwy umiejętności z pola <c>umiejetnosci</c> (po przecinkach, średnikach albo spacjach), bez powtórzeń.</summary>
        public static IReadOnlyList<string> ParseSkills(string text)
            => (text ?? "").Split(new[] { ',', ';', ' ', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim().ToLowerInvariant()).Where(s => s.Length > 0).Distinct(StringComparer.Ordinal).ToList();

        private static void Report(Action<HarnessEvent> report, HarnessEvent e)
        {
            try
            {
                report?.Invoke(e);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                // Okno gospodarza nie może przerwać pracy podagenta.
            }
        }
    }
}
