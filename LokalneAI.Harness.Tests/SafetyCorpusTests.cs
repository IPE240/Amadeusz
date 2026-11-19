using System.Text;
using System.Text.Json;
using LokalneAI.Harness.Profiles;
using LokalneAI.Harness.Tools;

namespace LokalneAI.Harness.Tests;

/// <summary>
/// Strażnik bezpieczeństwa na korpusie typowych wywołań (Dane\straznik_korpus.jsonl: blokada / zawsze_pytaj / dozwolone, w środowisku
/// „C:\Projekty\1095_WSP”, dozwolone C:\Projekty i C:\ODS). Nic nie jest wykonywane. Ten sam korpus ocenia
/// <c>LokalneAI.Eval straznik --plik …</c> z tabelą wyników.
/// </summary>
public class SafetyCorpusTests
{
    private const string Work = @"C:\Projekty\1095_WSP";
    private static readonly string[] Allowed = { @"C:\Projekty", @"C:\ODS" };

    private static readonly HarnessProfile Tools = HarnessProfile.Combine(new[] { "komputer", "office", "ocr" }
        .Select(n => HarnessProfile.Load(Paths.Profile(n)))).WithInternet();

    [Fact]
    public void Korpus_ZadenSzkodliwyNieJestLagodniejszyNizOczekiwanoAZwyklaPracaPrzechodzi()
    {
        var file = Path.Combine(Paths.Root, "LokalneAI.Harness.Tests", "Dane", "straznik_korpus.jsonl");
        var lines = File.ReadAllLines(file, Encoding.UTF8).Where(l => l.Trim().Length > 0).ToList();
        Assert.True(lines.Count >= 250, "Korpus ma tylko " + lines.Count + " wywołań.");
        var problems = new List<string>();
        foreach (var line in lines)
        {
            using var document = JsonDocument.Parse(line);
            var item = document.RootElement;
            var id = item.GetProperty("id").GetString();
            var tool = item.GetProperty("narzedzie").GetString();
            var expected = item.GetProperty("oczekiwane").GetString();
            var internet = !item.TryGetProperty("internet", out var i) || i.ValueKind != JsonValueKind.False;
            var guard = new SafetyGuard(new SafetyOptions { ProtectedFolders = { Paths.Root, @"C:\ODS\TeklaBridge\exchange" }, WorkingFolder = Work });
            if (expected != "blokada")
            {
                // W zwykłej pracy adres w poleceniu podaje użytkownik.
                guard.RememberUserText(item.GetProperty("argumenty").GetRawText().Replace("\\\\", "\\"));
            }

            var definition = Tools.FindTool(tool) ?? throw new InvalidOperationException(id + ": brak narzędzia " + tool);
            var call = new ToolCall(definition, item.GetProperty("argumenty").Clone(), 1, 1);
            var verdict = BuiltInTools.IsWeb(tool) ? guard.CheckWeb(call) : guard.Check(call, Work, internet);
            var policy = tool == "powershell" ? PowerShellPolicy.Analyze(call.GetString("polecenie"), Work, Allowed) : null;
            var rejectedByFolders = policy != null && policy.Verdict == ConfirmationKind.Reject;
            var asksAlways = verdict.Kind == SafetyVerdictKind.AlwaysAsk || verdict.Kind == SafetyVerdictKind.Allow && policy != null && policy.Deletes;
            var ok = expected switch
            {
                "blokada" => verdict.Kind == SafetyVerdictKind.Block || rejectedByFolders,
                "zawsze_pytaj" => asksAlways || rejectedByFolders,
                _ => verdict.Kind == SafetyVerdictKind.Allow, // dotychczasowa zasada folderów może odrzucić (np. AppData spoza dozwolonych)
            };
            if (!ok)
            {
                problems.Add(id + " oczekiwane " + expected + ", jest " + verdict + (rejectedByFolders ? " (ocena folderów: odrzuca)" : "") + ": " + Text.Shorten(item.GetProperty("argumenty").GetRawText(), 120));
            }
        }

        Assert.True(problems.Count == 0, "Niezgodne z korpusem (" + problems.Count + "):\n" + string.Join("\n", problems));
    }
}
