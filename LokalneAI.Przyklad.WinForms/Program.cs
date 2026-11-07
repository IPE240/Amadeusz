using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using LokalneAI.Harness.Llm;
using LokalneAI.Harness.Profiles;
using LokalneAI.Harness.Session;
using LokalneAI.Harness.Tools;
using LokalneAI.Harness.Wpf;

namespace LokalneAI.Przyklad
{
    /// <summary>
    /// Najprostsze osadzenie: okno WinForms, w nim panel, model z Ollamy (lokalnej albo na serwerze w biurze),
    /// profil „tekla” i symulator zamiast prawdziwej Tekli. W moście do Tekli zamiast symulatora wstawia się
    /// <see cref="TeklaBridgeExecutor"/> albo własny wykonawca wołający Tekla Open API wprost.
    ///
    /// Uruchomienie: LokalneAI.Przyklad.exe [adres_ollamy] [model]
    /// Kontrola: LokalneAI.Przyklad.exe --test "polecenie 1||polecenie 2" plik_wyniku.txt
    /// </summary>
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var test = args.Length >= 3 && args[0] == "--test";
            var url = !test && args.Length > 0 ? args[0] : "http://127.0.0.1:11434";
            var model = !test && args.Length > 1 ? args[1] : "SpeakLeash/bielik-11b-v3.0-instruct:Q4_K_M";
            var profileFolder = FindProfile("tekla");

            // 1. Model: jeden klient na program (tu lokalna Ollama; w biurze adres serwera).
            var llm = new OllamaLlmClient(new OllamaOptions { Url = url, Model = model, ContextLength = 16384, KeepAlive = "10m" });

            // 2. Panel + fabryka sesji: profil (wiedza i polecenia) + wykonawca (tu symulator).
            var panel = new HarnessPanel { Title = "Asystent Tekli (przykład WinForms, .NET Framework 4.8)" };
            panel.SessionFactory = () => new HarnessSession(
                llm,
                HarnessProfile.Load(profileFolder),
                new SimulatedModelExecutor(),
                new SessionOptions { ContextNote = "Model Tekli jest symulowany (przykład bez Tekli)." });

            // 3. Okno WinForms z panelem WPF w ElementHost.
            var form = new Form { Text = "LokalneAI — przykład osadzenia", Width = 760, Height = 720, StartPosition = FormStartPosition.CenterScreen };
            form.Controls.Add(new ElementHost { Dock = DockStyle.Fill, Child = panel });

            if (test)
            {
                // Kontrola bez klikania: okno poza ekranem, polecenia po kolei, zapis rozmowy, wyjście.
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new System.Drawing.Point(-5000, 0);
                form.ShowInTaskbar = false;
                form.Shown += async (s, e) =>
                {
                    var code = await RunTestAsync(panel, args[1].Split(new[] { "||" }, StringSplitOptions.RemoveEmptyEntries), args[2]);
                    Environment.ExitCode = code;
                    form.Close();
                };
            }

            Application.Run(form);
            llm.Dispose();
            return Environment.ExitCode;
        }

        private static async Task<int> RunTestAsync(HarnessPanel panel, string[] commands, string output)
        {
            foreach (var command in commands)
            {
                await panel.SendAsync(command.Trim());
            }

            var text = new StringBuilder();
            text.AppendLine("Środowisko: " + Environment.Version + " (" + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription + ")");
            text.AppendLine("Kontekst: " + panel.Session?.Context);
            foreach (var entry in panel.Entries)
            {
                text.AppendLine("[" + entry.Kind + "] " + entry.Mark + " " + entry.Text + (entry.HasDetail ? " | " + entry.Detail.Replace("\n", " ") : ""));
            }

            File.WriteAllText(output, text.ToString(), Encoding.UTF8);
            return panel.Entries.Any(e => e.Kind == PanelEntryKind.Error) ? 1 : 0;
        }

        private static string FindProfile(string name)
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "profile", name);
                if (File.Exists(Path.Combine(candidate, "profil.json")))
                {
                    return candidate;
                }

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Nie znalazłem folderu profile\\" + name);
        }
    }
}
