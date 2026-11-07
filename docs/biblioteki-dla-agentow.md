# Biblioteki LokalneAI: instrukcja dla agentów AI

Czytelnik: agent AI, który pisze kod (Claude Fable, Claude Opus, Cursor i inni) w tym repozytorium albo w programie
biura, który ma dostać asystenta. Dokument jest kompletny: po jego lekturze nie trzeba zgadywać. Gdy czegoś brakuje,
zajrzyj do komentarzy `///` w kodzie (ma je każdy publiczny element) i do testów w `LokalneAI.Harness.Tests`,
a potem uzupełnij ten plik. Tło dla ludzi: [osadzanie.md](osadzanie.md), format profilu: [profile.md](profile.md).

Kod z przepisów niżej jest skopiowany do projektu [narzedzia/sprawdz_przyklady](../narzedzia/sprawdz_przyklady),
który kompiluje go na .NET 10 i .NET Framework 4.8 w C# 7.3 (domyślny język projektów na .NET Framework 4.8)
i uruchamia z udawanym modelem.

## 1. Jak to działa

```
program gospodarza                                   serwer modelu
┌──────────────────────────────────────────────┐     ┌─────────────────┐
│ HarnessPanel (WPF) albo własne okno          │     │ Ollama          │
│   └ HarnessSession ── ILlmClient ────────────┼────►│ (Bielik itd.)   │
│        │  profil: system.md, karta.md,       │     └─────────────────┘
│        │  narzedzia.json, przyklady, wiedza  │
│        └ IToolExecutor (kod gospodarza) ─────┼──► Tekla, baza, pliki…
└──────────────────────────────────────────────┘
```

1. Użytkownik pisze polecenie po polsku. `HarnessSession` wysyła je do modelu razem z poleceniem systemowym złożonym
   z profilu.
2. Model odpowiada krokiem w JSON, wymuszonym schematem: wywołania narzędzi, odpowiedź albo pytanie.
3. Biblioteka sprawdza argumenty ze schematem z `narzedzia.json`, pyta o zgodę (jeśli trzeba) i przekazuje wywołanie
   wykonawcy gospodarza (`IToolExecutor`).
4. Wynik wraca do modelu. Pętla trwa do odpowiedzi albo pytania (najwyżej `max_krokow` kroków).

Model niczego nie wykonuje sam. Wybiera tylko narzędzie z listy i podaje argumenty. Wykonuje sprawdzony kod gospodarza.

## 2. Zasady, których nie wolno łamać

1. **Każda akcja to narzędzie z `narzedzia.json` i kod w wykonawcy.** Nie dodawaj narzędzi typu „wykonaj kod”,
   „dowolny SQL” czy „dowolne polecenie” bez strażnika. Wzór strażnika: `SqlGuard` i `PowerShellPolicy`.
2. **`SessionOptions.ConfirmMutation = null` oznacza zgodę bez pytania.** Gdy profil ma polecenia zmieniające dane
   (`"zmienia_model": true` albo wykonawca z `IConfirmingExecutor`), zawsze podłącz pytanie: `panel.ConfirmMutationAsync`
   albo własne okno.
3. **Jeden `ILlmClient` na program.** Sesja służy jednej mini-rozmowie („wstaw słup”, „zmień go…”). `Dispose` kasuje
   jej historię.
4. **Wykonawca jest wołany z wątku w tle.** Jeśli API gospodarza wymaga wątku okna, przełącz się sam
   (`Dispatcher.InvokeAsync`, `Control.Invoke`).
5. **Biblioteki muszą działać na .NET Framework 4.8.** `LokalneAI.Harness` to .NET Standard 2.0. Nie dodawaj zależności
   bez wersji dla netstandard2.0 i nie używaj API spoza niego.
6. **Bezpieczeństwo danych.**
   - Baza: rola tylko do odczytu.
   - Hasła tylko w plikach `*.local.json` (są poza gitem).
   - Do repozytorium nie trafiają dane osobowe: nazwiska z bazy, godziny osób, `person_id`.
7. **Bez prób na żywym środowisku.**
   - Nie testuj na dysku `A:` (`\\10.0.0.30\budowa`).
   - Nie zapisuj do folderu `exchange` mostu TeklaBridge, bo skrypt wykona się na otwartym modelu Tekli.
     W testach używaj `SimulatedModelExecutor`.
   - Nie wołaj `POST /api/pull` w Ollamie, żeby coś sprawdzić: to aktualizuje model.
8. **Teksty dla modelu i użytkownika piszemy po polsku:** opisy narzędzi, `system.md`, wyniki `ToolResult` i komunikaty
   błędów. Identyfikatory w kodzie są po angielsku, a klucze w plikach profilu po polsku (`nazwa`, `opis`, `parametry`).

## 3. Co dodać do programu

| Potrzebujesz | Użyj | Projekt |
|---|---|---|
| zawsze | `HarnessSession`, `HarnessProfile`, `OllamaLlmClient` | `LokalneAI.Harness` |
| gotowego panelu rozmowy z licznikiem kontekstu (WPF albo WinForms) | `HarnessPanel` | `LokalneAI.Harness.Wpf` |
| poleceń w Twoim programie (wstaw, zmień, policz…) | własny `IToolExecutor` albo `DelegateExecutor` | `LokalneAI.Harness` |
| kilku wykonawców naraz (np. Tekla i pliki) | `RoutingExecutor` | `LokalneAI.Harness` |
| pytań do bazy CDE (SQL tylko do odczytu) | `PostgresReadOnlyExecutor` i profil `cde` | `LokalneAI.Harness.Postgres` |
| folderów, plików, PowerShella i uruchamiania programów | `FileSystemExecutor` i profil `pliki` albo `komputer` | `LokalneAI.Harness` |
| poleceń w Tekli przez most TeklaBridge | `TeklaBridgeExecutor` i profil `tekla` | `LokalneAI.Harness` |
| prób bez Tekli | `SimulatedModelExecutor` | `LokalneAI.Harness` |
| serwera innego niż Ollama (llama.cpp, LM Studio, vLLM) | `OpenAiCompatibleLlmClient` | `LokalneAI.Harness` |
| aktualizacji wiedzy z repozytoriów | `KnowledgeSync.SyncAsync` | `LokalneAI.Harness` |

## 4. Podłączenie do projektu

Biblioteka nie jest jeszcze paczką NuGet. Repozytorium:
`https://github.com/Optimal-Design-of-Structures/Amadeusz.git` (prywatne). Zalecany sposób to submoduł git
i referencja do projektu. **Zanim dodasz submoduł do cudzego repozytorium, zapytaj użytkownika.**

```powershell
git submodule add https://github.com/Optimal-Design-of-Structures/Amadeusz.git zewnetrzne/Amadeusz
```

```xml
<ItemGroup>
  <ProjectReference Include="..\zewnetrzne\Amadeusz\LokalneAI.Harness\LokalneAI.Harness.csproj" />
  <!-- panel rozmowy (WPF; w WinForms przez ElementHost): -->
  <ProjectReference Include="..\zewnetrzne\Amadeusz\LokalneAI.Harness.Wpf\LokalneAI.Harness.Wpf.csproj" />
</ItemGroup>

<!-- Profil asystenta leży w repozytorium programu (lokalne-ai\profil), wiedza obok (lokalne-ai\wiedza).
     Przy budowaniu oba trafiają do bin\…\profile\<nazwa>\. -->
<ItemGroup>
  <None Include="..\lokalne-ai\profil\**\*" LinkBase="profile\moj_program" CopyToOutputDirectory="PreserveNewest" />
  <None Include="..\lokalne-ai\wiedza\**\*" LinkBase="profile\moj_program\wiedza" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

- Ścieżki są względne do pliku `.csproj` gospodarza. Popraw je do układu repozytorium.
- **WinForms na .NET Framework 4.8:** dodaj `<Reference Include="WindowsFormsIntegration" />` i `<UseWPF>true</UseWPF>`.
- **WinForms na .NET 10:** wystarczą `<UseWindowsForms>true</UseWindowsForms>` i `<UseWPF>true</UseWPF>`.
- **Budowanie** wymaga .NET SDK 10, jak całe repozytorium Amadeusz. Dotyczy to także gospodarza na .NET Framework 4.8.
- **Projekt gospodarza w starym formacie** (`.csproj` bez `Sdk=`): zapytaj użytkownika, czy przenieść go na format SDK,
  czy dołączyć zbudowane pliki (`LokalneAI.Harness.dll` z `.xml` i `System.Text.Json` z zależnościami).
- **Folder profilu musi być zapisywalny**, jeśli działa aktualizacja wiedzy (`wiedza\zrodla\`). Program zainstalowany
  w Program Files powinien przy pierwszym starcie skopiować profil do `%LOCALAPPDATA%\<Program>\profile\`.

## 5. Przepis A: panel w oknie WPF

```xml
<Window x:Class="MojProgram.OknoAsystenta"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:ai="clr-namespace:LokalneAI.Harness.Wpf;assembly=LokalneAI.Harness.Wpf"
        Title="Asystent" Width="720" Height="760">
    <ai:HarnessPanel x:Name="Panel" />
</Window>
```

```csharp
using System;
using System.IO;
using System.Windows;
using LokalneAI.Harness.Llm;
using LokalneAI.Harness.Profiles;
using LokalneAI.Harness.Session;
using LokalneAI.Harness.Tools;

namespace MojProgram   // x:Class w XAML = przestrzeń nazw + nazwa klasy
{
    public partial class OknoAsystenta : Window
    {
        // Jeden klient na program (tu: na okno). Adres i model z ustawień programu, nie na sztywno.
        private readonly OllamaLlmClient _llm = new OllamaLlmClient(new OllamaOptions
        {
            Url = LocalGateway.ChooseUrl(),                     // okno LokalneAI działa: jego kolejka i komputer z ustawień; nie działa: Ollama na tym PC
            ClientName = "MojProgram",                          // nazwa w kolejce okna LokalneAI
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
```

Panel sam pokazuje polecenia, wywołania z wynikami, odpowiedzi, licznik kontekstu i przyciski „Wyślij”, „Stop”
i „Nowa sesja”. Zdarzenie `Panel.TurnCompleted` daje `TurnResult` po każdej turze, np. do odświeżenia widoku programu.
`Panel.SendAsync("…")` wysyła polecenie z kodu.

## 6. Przepis B: panel w oknie WinForms

```csharp
using System.Windows.Forms;
using System.Windows.Forms.Integration;     // referencja WindowsFormsIntegration (.NET Framework 4.8)
using LokalneAI.Harness.Wpf;

var panel = new HarnessPanel { Title = "Asystent" };
panel.SessionFactory = () => new HarnessSession(llm, HarnessProfile.Load(profileFolder), new MojWykonawca(),
    new SessionOptions { ConfirmMutation = panel.ConfirmMutationAsync });
form.Controls.Add(new ElementHost { Dock = DockStyle.Fill, Child = panel });
```

Pełny działający przykład: [LokalneAI.Przyklad.WinForms/Program.cs](../LokalneAI.Przyklad.WinForms/Program.cs).

## 7. Przepis C: bez panelu (własne okno, pole tekstowe, usługa)

```csharp
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
```

- `SendAsync` w trakcie poprzedniego polecenia rzuca `InvalidOperationException`. Jedna sesja wykonuje jedno polecenie
  naraz.
- Przerwanie: `CancellationToken` daje `OperationCanceledException`.
- Serwer niedostępny: `LlmUnavailableException` (komunikat dla człowieka w `Message`). Inny błąd serwera albo
  przekroczony czas: `LlmException`.
- `session.Context` to licznik kontekstu, np. „8 063 / 16 384 tokenów (49%)”. Powyżej 85% sesja skraca starszą historię
  i zgłasza ostrzeżenie.

`MojeOknoZgody` ma sygnaturę `Func<ToolCall, CancellationToken, Task<bool>>`. Treść pytania weź
z `call.ConfirmationMessage`, a gdy jest pusta, z `call.Name` i `call.ArgumentsJson`. Przy `call.ConfirmationDangerous`
domyślną odpowiedzią ma być „Nie”.

## 8. Własny wykonawca

```csharp
using System.Threading;
using System.Threading.Tasks;
using LokalneAI.Harness.Tools;

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
```

Kontrakt wykonawcy:

- **Argumenty są już sprawdzone** ze schematem z `narzedzia.json`: typy, wymagane, `enum`, `minimum` i `maximum`.
  Zmienne sesji (`{{ja}}`) są podstawione. Inne odczyty: `call.Arguments` (`JsonElement`) i `call.ArgumentsJson`.
- **Wynik to tekst dla modelu.** Ma być krótki i konkretny: „OK id=1001”, „Utworzono folder Raporty”,
  „3 pliki: a.pdf, b.pdf, c.pdf”. Długie wyniki utnij do ok. 4000 znaków, bo zajmują kontekst i spowalniają model.
- **Błąd zwracaj jako `ToolResult.Error`** ze zdaniem, co poszło nie tak i czego brakuje. Model poprawi wywołanie albo
  zapyta użytkownika. Wyjątek z wykonawcy biblioteka też zamieni na błąd, ale z mniej pomocnym tekstem.
- **Wątek w tle i `cancellationToken`.** Szanuj przerwanie przy długich operacjach.
- **Najprostsza wersja bez klasy:** `new DelegateExecutor(async (call, ct) => { … })`.
- **Kilku wykonawców:**
  ```csharp
  new RoutingExecutor(fallback: plikiExecutor).Route("wstaw_slup", tekla).Route("zmien_profil", tekla)
  ```

## 9. Zgoda użytkownika

| Mechanizm | Kiedy | Jak |
|---|---|---|
| `"zmienia_model": true` w `narzedzia.json` | polecenie zawsze coś zmienia | sesja woła `ConfirmMutation` przed każdym wywołaniem |
| `IConfirmingExecutor.Decide(call)` | decyzja zależy od argumentów (np. PowerShell: czytanie bez pytania, kasowanie z pytaniem) | zwróć `ConfirmationDecision.NotNeeded`, `.Ask(treść, niebezpieczne)` albo `.Reject(powód)`; wtedy `zmienia_model` się nie liczy, a `null` = jak bez interfejsu |
| `RoutingExecutor` | kilku wykonawców | przekazuje `Decide` do wykonawcy, do którego trafi polecenie |

- **Użytkownik odmówi:** model dostaje błąd „ODRZUCONE — użytkownik nie zgodził się na to polecenie.”. Profil powinien
  mieć w `system.md` zasadę, żeby po ODRZUCONE nie ponawiać wywołania.
- **`Reject`:** model dostaje powód, a użytkownik nie widzi okna.
- **`ConfirmMutation` = null:** zgoda bez pytania, patrz sekcja 2.

## 10. Nowe polecenie dla modelu: krok po kroku

1. **`narzedzia.json`:** nazwa (małe litery, `_`), opis w jednym zdaniu i parametry jako JSON Schema
   z `"additionalProperties": false`.
   - Jednostka w nazwie parametru: `_mm`, `_m`, `_stopni`.
   - Wymagane są tylko parametry, bez których nie da się wykonać polecenia.
   - Zamknięte listy wartości jako `enum`.
   - `"zmienia_model": true`, jeśli polecenie coś zmienia.
2. **Wykonawca:** `case "nazwa":` w `ExecuteAsync` (sekcja 8).
3. **`przyklady.jsonl`:** co najmniej 2 przykłady, w tym jeden z dopytaniem, gdy brakuje danych. Przykłady uczą model
   bardziej niż opisy. Bez nich małe modele mylą polecenia.
4. **`testy.jsonl`:** przypadek dla `LokalneAI.Eval` (format: [profile.md](profile.md)).
5. **Test jednostkowy wykonawcy** (sekcja 13).
6. **Sprawdzenie, jeśli działa Ollama:**
   - `LokalneAI.Eval rozmiar --profil <folder>` mierzy, ile tokenów zajmuje polecenie systemowe; ma się mieścić
     w `ContextLength` z zapasem;
   - `LokalneAI.Eval test --profil <folder> --model <tag> --ctx 16384` przechodzi testy z prawdziwym modelem.

Szablon pustego profilu: [szablony/profil](../szablony/profil). Pełny format: [profile.md](profile.md).

## 11. Wiedza profilu

- **`karta.md`:** pojęcia, jednostki i pułapki. Trafia do każdego polecenia, więc ma być krótka.
- **`wiedza\*.md` i `*.txt`:** dłuższa dokumentacja. Model sięga po nią narzędziem `szukaj_wiedzy` (włącz je
  w `profil.json`: `"narzedzia_wbudowane": ["szukaj_wiedzy"]`).
  - Wyszukiwarka zwraca 3 najlepsze fragmenty po najwyżej 900 znaków.
  - Fragment to sekcja pod nagłówkiem `#` (dłuższe są dzielone co ok. 1500 znaków).
  - `CZYTAJ.md` jest pomijany, bo to opis folderu dla ludzi.
- **`zrodla.json`:** repozytoria i adresy, z których dokumentacja pobiera się do `wiedza\zrodla\`. Gospodarz sam decyduje,
  kiedy aktualizować (okno testowe robi to co 6 godzin):
  ```csharp
  var last = KnowledgeSync.LastSync(profileFolder);
  if (last == null || DateTime.Now - last.Value > TimeSpan.FromHours(6))
  {
      var result = await Task.Run(() => KnowledgeSync.SyncAsync(profileFolder,
          Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MojProgram", "repo")));
      // result.Errors: np. brak dostępu do prywatnego repozytorium; wiedza zostaje w poprzedniej wersji
  }
  ```
  Indeks wiedzy powstaje w `HarnessProfile.Load`, więc nowe pliki działają od następnej sesji.

Gdzie wkładać dokumentację biura i jak ją pisać: [wiedza-dla-modeli.md](wiedza-dla-modeli.md).

## 12. Model i serwer

- Domyślny model biura: `SpeakLeash/bielik-11b-v3.0-instruct:Q4_K_M` (Bielik 11B). Szybszy i słabszy:
  `SpeakLeash/bielik-minitron-7B-v3.0-instruct:Q4_K_M`. Porównanie: [wybor-modelu.md](wybor-modelu.md).
- **`OllamaOptions.ContextLength`:** domyślnie 8192. Profil `tekla` potrzebuje 16384, bo jego prompt to ok. 7,8 tys.
  tokenów. Za krótki kontekst to błąd bez komunikatu: Ollama obetnie początek polecenia systemowego.
- **`Temperature` 0,1** do poleceń i SQL. Profil może ją zmienić (`"temperatura"` w `profil.json`).
- **Adres: `LocalGateway.ChooseUrl()`**, chyba że program ma własne ustawienie adresu. Gdy na tym PC działa okno
  LokalneAI, zwraca jego bramę `http://127.0.0.1:11435`. Wtedy zapytania wszystkich programów idą przez jedną
  kolejkę, a liczy komputer wybrany w oknie (ten albo mocniejszy w biurze, [siec.md](siec.md)). Gdy okno nie działa,
  zwraca Ollamę na tym PC. Wybór zapada raz, przy tworzeniu klienta.
- **`OllamaOptions.ClientName`**: nazwa programu w kolejce okna („Tekla”, „CDE”). Bez niej w kolejce widać „?”.
- Brama przepuszcza tylko czat, generowanie, embeddingi i listę modeli. `ollama pull` i usuwanie modeli robi się
  w Ollamie danego komputera.

## 13. Testy

Wykonawcę testuj bez modelu, budując wywołanie ręcznie:

```csharp
var definition = new ToolDefinition("wstaw_slup", "Wstawia słup.",
    "{\"type\":\"object\",\"properties\":{\"x_mm\":{\"type\":\"number\"},\"profil\":{\"type\":\"string\"}},\"required\":[\"x_mm\",\"profil\"],\"additionalProperties\":false}",
    mutates: true);
var call = new ToolCall(definition, JsonDocument.Parse("{\"x_mm\":6000,\"profil\":\"HEB300\"}").RootElement, step: 1, index: 1);
var result = await new MojWykonawca().ExecuteAsync(call, CancellationToken.None);
Assert.True(result.Success);
Assert.Matches(@"id=\d+", result.Text);
```

Całą sesję testuj z udawanym modelem, który zwraca przygotowane kroki:

```csharp
sealed class UdawanyModel : ILlmClient
{
    private readonly Queue<string> _kroki;
    public UdawanyModel(params string[] kroki) => _kroki = new Queue<string>(kroki);
    public string ModelName => "udawany";
    public int ContextLength => 8192;
    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
        => Task.FromResult(new LlmResponse { Content = _kroki.Dequeue(), PromptTokens = 1000, OutputTokens = 50 });
}

// Profil z szablony\profil: wstaw_slup wymaga x_mm, y_mm, poziom_dol_m, poziom_gora_m i profil.
var llm = new UdawanyModel(
    "{\"krok\":\"wywolania\",\"wywolania\":[{\"narzedzie\":\"wstaw_slup\",\"argumenty\":{\"x_mm\":6000,\"y_mm\":0,\"poziom_dol_m\":0,\"poziom_gora_m\":3.53,\"profil\":\"HEB300\"}}]}",
    "{\"krok\":\"odpowiedz\",\"tekst\":\"Wstawiłem słup HEB300.\"}");
using (var session = new HarnessSession(llm, HarnessProfile.Load(profileFolder), new MojWykonawca()))
{
    var turn = await session.SendAsync("wstaw słup HEB300 w 6000, 0 od zera do 3,53");
    Assert.Equal(TurnKind.Answer, turn.Kind);
    Assert.Equal("wstaw_slup", turn.Calls[0].Tool);
    Assert.True(turn.Calls[0].Success);              // argument niezgodny ze schematem = Success false i błąd dla modelu
}
```

Krok modelu ma jeden z trzech kształtów:
- `{"krok":"wywolania","wywolania":[{"narzedzie":…,"argumenty":{…}}]}`
- `{"krok":"odpowiedz","tekst":…}`
- `{"krok":"pytanie","tekst":…}`

W profilu z `"zakonczenie": "auto"` po udanych wywołaniach nie ma drugiego wołania modelu, bo biblioteka sama
podsumowuje. Udawany model potrzebuje wtedy tylko pierwszego kroku.

Na koniec, jeśli działa Ollama: `LokalneAI.Eval test` (sekcja 10). Do prób całego okna w tym repozytorium służy
`LokalneAI.exe --autotest … --harness <profil>` ([README](../README.md)).

## 14. Typowe problemy

| Objaw | Przyczyna | Poprawka |
|---|---|---|
| model podaje 3.53 tam, gdzie trzeba 3530 mm | jednostka nie wynika z nazwy parametru | nazwy `poziom_m`, `x_mm`; przykład w `przyklady.jsonl` |
| model „wykonał”, a nic się nie stało, albo zmyśla wynik | brak narzędzia na tę czynność albo wynik bez konkretów | dodaj narzędzie; wynik z konkretem („OK id=…”, liczba plików) |
| model myli podobne polecenia | za mało przykładów | 2–3 przykłady na polecenie, w tym na granicy między poleceniami |
| odpowiedzi coraz wolniejsze, model gubi zasady | duży kontekst: długa karta, długie wyniki | skróć `karta.md` (przenieś do `wiedza\`), utnij wyniki, `LokalneAI.Eval rozmiar` |
| model nie zna początku `system.md` | `ContextLength` mniejsze niż prompt | zwiększ `ContextLength` (sekcja 12) |
| `InvalidOperationException` w oknie po zdarzeniu `Progress` | zmiana kontrolek z wątku w tle | `Dispatcher.InvokeAsync` |
| `InvalidOperationException: Sesja jeszcze pracuje` | drugie `SendAsync` przed końcem pierwszego | zablokuj „Wyślij” do końca tury (panel robi to sam) |
| polecenie zmieniające wykonało się bez pytania | `ConfirmMutation` = null | podłącz pytanie (sekcja 9) |
| `szukaj_wiedzy` nic nie znajduje | słowa w notatkach inne niż w pytaniach albo brak narzędzia w `profil.json` | [wiedza-dla-modeli.md](wiedza-dla-modeli.md); `"narzedzia_wbudowane": ["szukaj_wiedzy"]` |
| odpowiedź długo nie przychodzi, choć model jest szybki | zapytanie czeka w kolejce okna LokalneAI (liczy inny program albo inny komputer) | okno LokalneAI → Ustawienia → kolejka; ewentualnie dłuższy `RequestTimeout` |
| „Nie ma połączenia z PC-…” | w oknie LokalneAI wybrano inny komputer, a on jest wyłączony albo nie udostępnia | okno LokalneAI → Ustawienia → Gdzie liczyć |
| aktualizacja wiedzy: brak dostępu | folder profilu tylko do odczytu albo `git` bez logowania do prywatnego repozytorium | profil w `%LOCALAPPDATA%`; `git clone` raz ręcznie |

## 15. Mapa API

| Przestrzeń nazw | Typ | Do czego |
|---|---|---|
| `LokalneAI.Harness.Session` | `HarnessSession` | mini-sesja: `SendAsync`, `Progress`, `Context`, `Dispose` |
| | `SessionOptions` | `ConfirmMutation`, `ContextNote`, `Variables` (`{{ja}}`), `CompactAt`, `KeepRecentMessages`, `ToolResultRole` |
| | `TurnResult`, `TurnKind` | wynik polecenia: `Answer`, `Question` albo `Failed`; `Calls`, `Warnings`, `NumbersVerified` |
| | `HarnessEvent`, `HarnessEventKind` | zdarzenia postępu (`Progress`) |
| | `ExecutedCall` | wykonane wywołanie (w `TurnResult.Calls` i zdarzeniach) |
| | `ContextUsage` | licznik kontekstu |
| | `PromptBuilder` | podgląd polecenia systemowego |
| | `StepSchema` | schemat JSON kroku modelu (do testów i diagnostyki) |
| `LokalneAI.Harness.Profiles` | `HarnessProfile` | profil z folderu: `HarnessProfile.Load(folder)` |
| | `ProfileSettings`, `FinishMode`, `SqlRule`, `FewShotExample` | zawartość `profil.json` i `przyklady.jsonl` |
| | `ToolDefinition` | narzędzie: nazwa, opis, schemat parametrów, `Mutates`, `FinishesTurn` |
| `LokalneAI.Harness.Tools` | `IToolExecutor` | wykonawca gospodarza |
| | `ToolCall`, `ToolResult` | wywołanie od modelu (`GetString`, `GetNumber`) i wynik (`Ok`, `Error`) |
| | `IConfirmingExecutor`, `ConfirmationDecision`, `ConfirmationKind` | zgoda zależna od argumentów |
| | `DelegateExecutor`, `RoutingExecutor` | wykonawca z funkcji; rozdzielanie po nazwie |
| | `SimulatedModelExecutor` | atrapa modelu konstrukcji do prób bez Tekli |
| | `ScriptedExecutor`, `ExpectedCall` | oczekiwane wywołania w testach i w `LokalneAI.Eval` |
| | `FileSystemExecutor`, `FileSystemOptions` | pliki, foldery, PowerShell, programy (profile `pliki` i `komputer`) |
| | `PowerShellPolicy`, `CommandAnalysis` | ocena polecenia PowerShella: czyta, pyta albo odrzuca |
| | `PowerShellRunner`, `CommandRunResult` | uruchomienie polecenia PowerShella z limitem czasu |
| | `TeklaBridgeExecutor`, `TeklaBridgeOptions` | most TeklaBridge (szablony C# z profilu `tekla`) |
| | `KnowledgeIndex` | wyszukiwarka `szukaj_wiedzy` w `wiedza\` |
| | `KnowledgeSync`, `KnowledgeSource`, `KnowledgeSyncResult` | aktualizacja wiedzy z `zrodla.json` |
| | `BuiltInTools`, `Calculator` | narzędzia wbudowane: `kalkulator`, `szukaj_wiedzy` |
| `LokalneAI.Harness.Llm` | `ILlmClient` | dostęp do modelu (własna implementacja: testy, inny serwer) |
| | `OllamaLlmClient`, `OllamaOptions` | Ollama `/api/chat`; `ClientName` = nazwa w kolejce okna |
| | `LocalGateway` | brama okna LokalneAI na tym PC: `ChooseUrl()`, `IsRunningAsync()` |
| | `OpenAiCompatibleLlmClient`, `OpenAiCompatibleOptions` | serwery z API OpenAI `/v1/chat/completions` |
| | `LlmRequest`, `LlmResponse`, `LlmMessage` | zapytanie i odpowiedź modelu |
| | `LlmException`, `LlmUnavailableException` | błędy serwera modelu |
| `LokalneAI.Harness.Validation` | `SqlGuard` | strażnik SQL: jedno zapytanie, tylko odczyt |
| | `SchemaValidator` | sprawdzenie argumentów z JSON Schema |
| | `NumberGuard` | czy liczby w odpowiedzi są w wynikach narzędzi |
| `LokalneAI.Harness.Wpf` | `HarnessPanel` | panel rozmowy: `SessionFactory`, `ConfirmMutationAsync`, `SendAsync`, `NewSession`, `TurnCompleted` |
| | `PanelEntry`, `PanelEntryKind` | wpisy rozmowy w panelu |
| `LokalneAI.Harness.Postgres` | `PostgresReadOnlyExecutor`, `PostgresReadOnlyOptions` | SQL tylko do odczytu (profil `cde`) |
| | `QueryResult` | wynik zapytania |

## 16. Gdy zmieniasz bibliotekę

- **Nowy albo zmieniony element publiczny:** dopisz komentarz `///` (bez niego budowanie daje ostrzeżenie CS1591)
  i popraw ten plik. Test `DokumentacjaTests` sprawdza, czy każdy publiczny typ z `LokalneAI.Harness` jest w mapie
  API (sekcja 15) i czy działają linki w dokumentacji.
- **Zmiana sposobu użycia:** popraw przepisy (sekcje 5–8 i 13) i projekt [narzedzia/sprawdz_przyklady](../narzedzia/sprawdz_przyklady).
- **Zmiana formatu profilu:** popraw [profile.md](profile.md), [szablony/profil](../szablony/profil) i ten plik.
- **Zasady dla ludzi:** [zasady-opisow-dla-ai.md](zasady-opisow-dla-ai.md).
