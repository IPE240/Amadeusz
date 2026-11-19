# Podpiąć inny program do Amadeusza: zasada (adres bramy zamiast Ollamy)

Każdy program, który umie rozmawiać z Ollamą, działa z Amadeuszem: w polu „adres Ollamy” / „Ollama base URL” / „host” wpisz `http://127.0.0.1:11435` zamiast `http://127.0.0.1:11434`. Program, który umie API OpenAI: base URL `http://127.0.0.1:11435/v1`, klucz dowolny niepusty. Zyski: wspólna kolejka z czatem, liczenie tam, gdzie wybrano w „Gdzie liczyć”, liczniki tokenów w Ustawieniach → Kolejka. Warto dodać nagłówek `X-LokalneAI-Program` z nazwą programu — tak będzie widoczny w kolejce.

# Open WebUI, Continue (VS Code), LangChain, ollama-python, n8n — ustawienia

- Open WebUI: zmienna `OLLAMA_BASE_URL=http://127.0.0.1:11435` (Open WebUI woła model po stronie serwera, więc działa).
- Continue w VS Code: dostawca `ollama`, `apiBase: http://127.0.0.1:11435`.
- LangChain: `ChatOllama(base_url="http://127.0.0.1:11435", model="qwen3.5:9b-q4_K_M")`.
- biblioteka `ollama` w Pythonie: `ollama.Client(host="http://127.0.0.1:11435", headers={"X-LokalneAI-Program": "Mój skrypt"})`.
- inne (n8n, AnythingLLM, LibreChat…): pole adresu Ollamy albo „OpenAI-compatible base URL” jak wyżej.
Przycisk „Pull / pobierz model” w tych programach nie zadziała (404) — model pobiera się `ollama pull` na komputerze, który liczy.

# Program .NET (C#): biblioteka LokalneAI.Harness, LocalGateway

```csharp
var llm = new OllamaLlmClient(new OllamaOptions
{
    Url = LocalGateway.ChooseUrl(),   // brama okna, gdy działa; inaczej Ollama 127.0.0.1:11434
    ClientName = "Tekla",             // nazwa w kolejce (nagłówek X-LokalneAI-Program)
    Model = "SpeakLeash/bielik-11b-v3.0-instruct:Q4_K_M",
    ContextLength = 16384,
});
```
Biblioteka działa na .NET Framework 4.8 i .NET 10. Pełny przepis z umiejętnościami (HarnessProfile, HarnessSession, panel WPF): `docs\biblioteki-dla-agentow.md` i `docs\osadzanie.md` w repozytorium Amadeusza.

# Czego nie da się podpiąć: przeglądarka, Claude Code, pobieranie modeli

- **Strona w przeglądarce**, która woła model z JavaScriptu: 403 (brama odrzuca nagłówek Origin, żeby obca strona nie użyła modelu). Działają programy i serwery wołające bramę po swojej stronie.
- **Klienci API Anthropic** (Claude Code, trasa `/v1/messages`): brama nie ma tej trasy — 404.
- **Pobieranie, usuwanie, tworzenie modeli** przez bramę: 404.
- **Wywołania narzędzi (tools) przez API OpenAI** przechodzą do Ollamy bez zmian; czy działają, zależy od modelu.

# Podpiąć program na innym komputerze w biurze (NAZWA-PC:11435)

Adres: `http://NAZWA-PC:11435` (albo IP). Warunki: na tamtym PC działa Amadeusz z włączonym „Pozwalaj na przeliczanie zapytań z innych komputerów” (Ustawienia → Udostępnianie), otwartym portem w zaporze, i oba komputery są w tej samej podsieci (sieć prywatna). Lepiej jednak podpinać program do własnej bramy `127.0.0.1:11435` i w swoim oknie ustawić „Gdzie liczyć → Na innym komputerze” — wtedy zmiana komputera nie wymaga zmian w programie.
