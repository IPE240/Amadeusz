// Przepisy B i C oraz aktualizacja wiedzy z docs\biblioteki-dla-agentow.md (sekcje 6, 7 i 11).
// Kod w metodach jest skopiowany 1:1; parametry i metody pomocnicze zastępują program gospodarza.
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using LokalneAI.Harness.Llm;
using LokalneAI.Harness.Profiles;
using LokalneAI.Harness.Session;
using LokalneAI.Harness.Tools;
using LokalneAI.Harness.Wpf;

namespace MojProgram
{
internal static class Przepisy
{
    // Sekcja 6 — wymaga wątku STA (tworzy kontrolki); tu tylko kompilacja.
    public static void PrzepisB(ILlmClient llm, string profileFolder, Form form)
    {
        var panel = new HarnessPanel { Title = "Asystent" };
        panel.SessionFactory = () => new HarnessSession(llm, HarnessProfile.Load(profileFolder), new MojWykonawca(),
            new SessionOptions { ConfirmMutation = panel.ConfirmMutationAsync });
        form.Controls.Add(new ElementHost { Dock = DockStyle.Fill, Child = panel });
    }

    // Sekcja 7.
    public static async Task PrzepisC(ILlmClient llm, string profileFolder, CancellationToken cancellationToken)
    {
        using (var session = new HarnessSession(llm, HarnessProfile.Load(profileFolder), new MojWykonawca(),
            new SessionOptions { ConfirmMutation = MojeOknoZgody }))
        {
            // Zdarzenia przychodzą z wątku w tle. W oknie: Dispatcher.InvokeAsync / Control.BeginInvoke.
            session.Progress += (s, e) => Log(e.Kind + ": " + e.Text);

            var turn = await session.SendAsync("wstaw słup HEB300 w 6000, 0 od zera do 3,53", cancellationToken);
            switch (turn.Kind)
            {
                case TurnKind.Answer: Pokaz(turn.Text); break;       // turn.Calls = co wykonano, turn.Warnings = uwagi
                case TurnKind.Question: Zapytaj(turn.Text); break;   // odpowiedź użytkownika = następne SendAsync w tej sesji
                case TurnKind.Failed: Blad(turn.Text); break;        // limit kroków albo błąd modelu
            }

            turn = await session.SendAsync("zmień go na HEB320", cancellationToken);   // sesja pamięta id z wyniku „OK id=…”
        }   // Dispose kasuje historię
    }

    // Sekcja 11.
    public static async Task AktualizujWiedze(string profileFolder)
    {
        var last = KnowledgeSync.LastSync(profileFolder);
        if (last == null || DateTime.Now - last.Value > TimeSpan.FromHours(6))
        {
            var result = await Task.Run(() => KnowledgeSync.SyncAsync(profileFolder,
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MojProgram", "repo")));
            // result.Errors: np. brak dostępu do prywatnego repozytorium; wiedza zostaje w poprzedniej wersji
        }
    }

    // Zastępstwa programu gospodarza.
    public static int Pytania;

    private static Task<bool> MojeOknoZgody(ToolCall call, CancellationToken cancellationToken)
    {
        Pytania++;
        Console.WriteLine("  [zgoda] " + (call.ConfirmationMessage ?? call.Name + " " + call.ArgumentsJson) + (call.ConfirmationDangerous ? " (niebezpieczne)" : ""));
        return Task.FromResult(true);
    }

    private static void Log(string text) => Console.WriteLine("  [postęp] " + text);

    private static void Pokaz(string text) => Console.WriteLine("  [odpowiedź] " + text);

    private static void Zapytaj(string text) => Console.WriteLine("  [pytanie] " + text);

    private static void Blad(string text) => Console.WriteLine("  [błąd] " + text);
}
}
