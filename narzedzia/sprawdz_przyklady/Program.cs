// Uruchamia przykłady z docs\biblioteki-dla-agentow.md bez prawdziwego modelu (udawany model, szablon profilu).
// Kod wyjścia: 0 = wszystko działa, 1 = któryś przykład nie działa.
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LokalneAI.Harness.Profiles;

namespace MojProgram
{
public static class Program
{
    private static int _nextId = 1001;

    // Zastępstwo wywołania programu gospodarza z sekcji 8.
    public static Task<int> WstawSlupAsync(double x, string profil, CancellationToken cancellationToken)
        => Task.FromResult(Interlocked.Increment(ref _nextId) - 1);

    public static async Task<int> Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        var profileFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "profile", "moj_program");
        var failures = 0;

        async Task Run(string name, Func<Task> example)
        {
            try
            {
                await example();
                Console.WriteLine("OK    " + name);
            }
            catch (Exception e)
            {
                failures++;
                Console.WriteLine("BŁĄD  " + name + ": " + e.Message);
            }
        }

        await Run("profil z szablonu wczytuje się z bin\\…\\profile\\moj_program", () =>
        {
            var profile = HarnessProfile.Load(profileFolder);
            Assert.Equal("moj_program", profile.Name);
            Assert.True(profile.Knowledge.ChunkCount > 0, "brak wiedzy z lokalne-ai\\wiedza");
            Assert.True(profile.Knowledge.Search("wyszukiwarka pomija").Count == 0, "CZYTAJ.md trafił do wiedzy");
            return Task.CompletedTask;
        });
        await Run("sekcja 13: test wykonawcy", Testy.TestWykonawcy);
        await Run("sekcja 13: test sesji z udawanym modelem", () => Testy.TestSesji(profileFolder));
        await Run("sekcja 7: przepis C (dwa polecenia, zgoda na zmianę)", async () =>
        {
            var llm = new UdawanyModel(
                "{\"krok\":\"wywolania\",\"wywolania\":[{\"narzedzie\":\"wstaw_slup\",\"argumenty\":{\"x_mm\":6000,\"y_mm\":0,\"poziom_dol_m\":0,\"poziom_gora_m\":3.53,\"profil\":\"HEB300\"}}]}",
                "{\"krok\":\"pytanie\",\"tekst\":\"Tego polecenia nie mam w katalogu. Mogę wstawić nowy słup.\"}");
            await Przepisy.PrzepisC(llm, profileFolder, CancellationToken.None);
            Assert.Equal(1, Przepisy.Pytania);
        });

        Console.WriteLine(failures == 0 ? "Wszystkie przykłady działają." : "Nie działa: " + failures);
        return failures == 0 ? 0 : 1;
    }
}
}
