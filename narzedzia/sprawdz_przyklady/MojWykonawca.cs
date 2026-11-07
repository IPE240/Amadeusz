// Sekcja 8 z docs\biblioteki-dla-agentow.md, skopiowana 1:1.
using System.Threading;
using System.Threading.Tasks;
using LokalneAI.Harness.Tools;

namespace MojProgram
{
public sealed class MojWykonawca : IToolExecutor
{
    public async Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken)
    {
        switch (call.Name)                                   // nazwy z narzedzia.json
        {
            case "wstaw_slup":
                var x = call.GetNumber("x_mm");              // double? — null, gdy argumentu nie ma
                var profil = call.GetString("profil");       // string — null, gdy nie ma
                if (x == null || string.IsNullOrWhiteSpace(profil))
                {
                    return ToolResult.Error("Brak X albo profilu. Podaj X w mm i profil, np. HEB300.");
                }

                var id = await Program.WstawSlupAsync(x.Value, profil, cancellationToken);
                return ToolResult.Ok("OK id=" + id);         // „id=<liczba>” model zapamięta na „zmień go…”

            default:
                return ToolResult.Error("Nieobsługiwane polecenie: " + call.Name);
        }
    }
}
}
