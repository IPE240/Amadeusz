// Przepis A z docs\biblioteki-dla-agentow.md (sekcja 5), skopiowany 1:1.
using System;
using System.IO;
using System.Windows;
using LokalneAI.Harness.Llm;
using LokalneAI.Harness.Profiles;
using LokalneAI.Harness.Session;
using LokalneAI.Harness.Tools;

namespace MojProgram
{
public partial class OknoAsystenta : Window
{
    // Jeden klient na program (tu: na okno). Adres i model z ustawień programu, nie na sztywno.
    private readonly OllamaLlmClient _llm = new OllamaLlmClient(new OllamaOptions
    {
        Url = "http://127.0.0.1:11434",                     // albo serwer w biurze, np. http://serwer-ai:11434
        Model = "SpeakLeash/bielik-11b-v3.0-instruct:Q4_K_M",
        ContextLength = 16384,                              // mniej niż prompt profilu = Ollama po cichu obetnie początek
        KeepAlive = "10m",
    });

    public OknoAsystenta()
    {
        InitializeComponent();
        var profileFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "profile", "moj_program");
        Panel.Title = "Asystent";
        // Fabryka: wołana przy pierwszym poleceniu i po „Nowa sesja”. Profil wczytuje się od nowa (świeża wiedza).
        Panel.SessionFactory = () => new HarnessSession(
            _llm,
            HarnessProfile.Load(profileFolder),
            new MojWykonawca(),                              // sekcja 8
            new SessionOptions
            {
                ConfirmMutation = Panel.ConfirmMutationAsync,    // okno Tak/Nie przed każdą zmianą
                ContextNote = "Otwarty projekt: 1095_WSP.",      // stan programu, który model ma znać
            });
        Closed += (s, e) => { Panel.NewSession(); _llm.Dispose(); };
    }
}
}
