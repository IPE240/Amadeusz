# Osadzanie pół-harnessu w programie

Dla agentów AI jest zwięzła i kompletna wersja z przepisami sprawdzanymi kompilacją:
[biblioteki-dla-agentow.md](biblioteki-dla-agentow.md).

Pół-harness to biblioteka, która łączy program biura z lokalnym modelem językowym.
Podział pracy wygląda tak:

- **Model** (Ollama na tym PC albo na serwerze w biurze) rozumie polecenie po polsku i wybiera narzędzie z argumentami.
- **Biblioteka** (`LokalneAI.Harness`):
  - składa polecenie systemowe z profilu i wymusza na modelu format odpowiedzi (JSON według schematu);
  - sprawdza argumenty, a SQL przepuszcza tylko do odczytu;
  - pilnuje licznika kontekstu i prowadzi mini-sesję.
- **Program-gospodarz** wykonuje polecenia: wstawia słup w Tekli, czyta bazę, rysuje. Robi to przez *wykonawcę* (`IToolExecutor`).

Model nigdy nie pisze kodu wykonywanego w Tekli ani nie dotyka bazy sam. Podaje tylko nazwę polecenia i argumenty,
a resztę robi sprawdzony kod gospodarza.

## Co dodać do projektu

| Projekt | Kiedy | Działa na |
|---|---|---|
| `LokalneAI.Harness` | zawsze | .NET Standard 2.0 (.NET Framework 4.8 i .NET 10) |
| `LokalneAI.Harness.Wpf` | gotowy panel rozmowy z licznikiem kontekstu | .NET Framework 4.8 i .NET 10 (WPF; w WinForms przez `ElementHost`) |
| `LokalneAI.Harness.Postgres` | pytania do bazy CDE (profil `cde`) | .NET Standard 2.0, Npgsql 8 |

Do tego folder profilu, np. `profile\tekla`, kopiowany obok programu.

## Najkrótszy przykład (WinForms, .NET Framework 4.8)

Pełny działający przykład: [LokalneAI.Przyklad.WinForms/Program.cs](../LokalneAI.Przyklad.WinForms/Program.cs).

```csharp
// 1. Model — jeden klient na program.
var llm = new OllamaLlmClient(new OllamaOptions
{
    Url = "http://127.0.0.1:11434",          // albo serwer w biurze, np. http://serwer-ai:11434
    Model = "SpeakLeash/bielik-11b-v3.0-instruct:Q4_K_M",
    ContextLength = 16384,                    // prompt profilu zajmuje 4–8 tys. tokenów
});

// 2. Panel i fabryka sesji: profil + wykonawca.
var panel = new HarnessPanel { Title = "Asystent Tekli" };
panel.SessionFactory = () => new HarnessSession(
    llm,
    HarnessProfile.Load(@"C:\Program\profile\tekla"),
    new TeklaBridgeExecutor(new TeklaBridgeOptions
    {
        ExchangeFolder = @"C:\Dominik\Aplikacje\TeklaBridge\exchange",
        ProfileFolder = @"C:\Program\profile\tekla",
    }),
    new SessionOptions { ConfirmMutation = panel.ConfirmMutationAsync });   // pytaj przed zmianą modelu

// 3. W oknie WinForms panel WPF wchodzi przez ElementHost (referencja: WindowsFormsIntegration).
form.Controls.Add(new ElementHost { Dock = DockStyle.Fill, Child = panel });
```

W WPF wystarczy `<wpf:HarnessPanel x:Name="Panel"/>` i ta sama `SessionFactory` w kodzie.

## Bez panelu: własne okno albo pole w istniejącym oknie

```csharp
using var session = new HarnessSession(llm, profile, executor, options);
session.Progress += (s, e) => Log(e.Kind + ": " + e.Text);    // uwaga: wątek w tle
var turn = await session.SendAsync("wstaw słup HEB300 w punkcie 6000, 0 od zera do 3,53");
// turn.Kind: Answer / Question / Failed; turn.Text; turn.Calls (co wykonano); session.Context (licznik)
turn = await session.SendAsync("zmień go na HEB320");          // ta sama sesja pamięta id słupa
// Dispose (koniec using) kasuje historię sesji.
```

## Własny wykonawca

Wykonawca dostaje sprawdzone wywołanie i zwraca tekst wyniku. Wynik trafia z powrotem do modelu.

```csharp
var executor = new DelegateExecutor(async (call, ct) =>
{
    switch (call.Name)
    {
        case "wstaw_slup":
            var id = MojaTekla.WstawSlup(call.GetNumber("x_mm").Value, call.GetNumber("y_mm").Value,
                                         call.GetNumber("poziom_dol_m").Value, call.GetNumber("poziom_gora_m").Value,
                                         call.GetString("profil"));
            return ToolResult.Ok("OK id=" + id);        // „id=…” model zapamięta na następne polecenia
        default:
            return ToolResult.Error("Nieobsługiwane: " + call.Name);
    }
});
```

Zasady:

- **Zwracaj `id=<liczba>`** po wstawieniu elementu. Model użyje go w poleceniach „zmień go…”. W jednym kroku
  model może też wpisać `"id": 0`, czyli „element z poprzedniego wywołania” (ustawienie `id_zero_to_poprzedni`).
- **Błędy zwracaj jako `ToolResult.Error("…")`** ze zdaniem, co poszło nie tak. Model spróbuje poprawić
  wywołanie albo zapyta użytkownika. Wyjątek z wykonawcy też zostanie zamieniony na błąd dla modelu.
- **Zmiany modelu**: `SessionOptions.ConfirmMutation` pyta o zgodę przed poleceniami z `"zmienia_model": true`.
- **Zgoda zależna od argumentów**: wykonawca, który implementuje `IConfirmingExecutor`, sam ocenia każde wywołanie
  (`Decide` → `ConfirmationDecision.NotNeeded`, `Ask(treść, niebezpieczne)` albo `Reject(powód)`). Wtedy
  `zmienia_model` z profilu się nie liczy. Tak działa PowerShell w profilu `komputer`: polecenie, które tylko czyta,
  idzie bez pytania; zmiana idzie z pytaniem (treść okna przygotowuje wykonawca, a przy kasowaniu domyślnym
  przyciskiem jest „Nie”); ścieżka spoza dozwolonych folderów jest odrzucana bez pytania. `RoutingExecutor` przekazuje
  decyzję do wykonawcy, do którego trafi polecenie.

## Polecenia na komputerze (profile `pliki` i `komputer`)

```csharp
var executor = new FileSystemExecutor(new FileSystemOptions
{
    AllowedRoots = { @"C:\ODS", Environment.ExpandEnvironmentVariables(@"%USERPROFILE%\Documents") },
    WorkingFolder = @"C:\ODS",                       // folder bieżący na start; „cd” go zmienia
    CommandTimeout = TimeSpan.FromSeconds(120),      // PowerShell: po tym czasie proces (z potomnymi) jest zabijany
});
var session = new HarnessSession(llm, HarnessProfile.Load(@"profile\komputer"), executor,
    new SessionOptions { ConfirmMutation = panel.ConfirmMutationAsync });
```

- Profil decyduje, które polecenia model widzi: `pliki` ma tylko foldery i kopiowanie, a `komputer` także
  `powershell`, `uruchom`, `czytaj_plik`, `zapisz_plik` i `szukaj_plikow`.
- `zapisz_plik` zapisuje skrypt albo inny plik tekstowy (zawsze z pytaniem o zgodę, istniejący plik tylko z `"nadpisz": true`).
  Małe modele piszą w JSON-ie `\\n` zamiast nowej linii: treść bez ani jednej nowej linii, za to z `\n` poza ścieżkami,
  jest naprawiana. Pliki `.ps1` dostają BOM (Windows PowerShell 5.1 bez niego psuje polskie litery), skrypty Windows
  (`.ps1`, `.bat`, `.cmd`…) — końce linii CRLF. Polecenie PowerShella, które zapisuje plik z `\n` w jednej linii,
  jest odrzucane z podpowiedzią „użyj zapisz_plik”, bo w PowerShellu `\n` nie jest nową linią.
- PowerShell to osobny proces `powershell.exe -NoProfile -NonInteractive`, polecenie idzie przez `-EncodedCommand`.
  Wynik (z błędami jako linie `[błąd] …`) jest obcinany do `MaxOutputChars` (4000 znaków).
- Ocena „tylko czyta” (`PowerShellPolicy`) chroni przed pomyłkami modelu, ale nie jest piaskownicą: PowerShell potrafi
  zbudować ścieżkę w locie. Granicą jest zgoda użytkownika na każdą zmianę i uprawnienia jego konta Windows.
- Zablokowane zawsze: polecenia dyskowe i rozruchowe (`Format-Volume`, `diskpart`, `bcdedit`…), wyłączanie komputera,
  `-EncodedCommand`, kasowanie całego dozwolonego folderu, dysku albo `*` w folderze głównym.
- `uruchom`: dokument z dozwolonego folderu otwiera się bez pytania. Program (`notepad`, `excel`, plik `.exe`
  z dozwolonego folderu albo z Program Files) uruchamia się po zgodzie.

## Dokumenty Office (profil `office`)

```csharp
using LokalneAI.Harness.Office;                      // projekt LokalneAI.Harness.Office (bez paczek; COM zainstalowanego Microsoft 365)

var executor = new OfficeExecutor(new OfficeOptions
{
    AllowedRoots = { @"C:\ODS", Environment.ExpandEnvironmentVariables(@"%USERPROFILE%\Documents") },
    WorkingFolder = @"C:\ODS\Projekty\1095_WSP",     // ścieżki względne („raport.docx”) liczą się od niego
    ShowAfterEdit = true,                            // po edycji dokument zostaje otwarty na wierzchu; false = zapisz, zamknij (testy, praca w tle)
});
var session = new HarnessSession(llm, HarnessProfile.Load(@"profile\office"), executor,
    new SessionOptions { ConfirmMutation = panel.ConfirmMutationAsync });
```

- Narzędzia: `word_czytaj`, `word_dopisz`, `word_zamien`, `excel_czytaj`, `excel_zapisz`, `excel_wstaw_wiersze`,
  `powerpoint_czytaj`, `powerpoint_dodaj_slajd`, `outlook_szkic`, `outlook_odebrane`, `outlook_czytaj`, `office_otworz`,
  `office_zapisz_jako` (PDF i formaty Office’a) oraz `szukaj_plikow` i `pokaz_zawartosc` (te dwa wykonuje `FileSystemExecutor`
  w środku, z tymi samymi dozwolonymi folderami).
- Czytanie idzie bez pytania; każda zmiana (dopisanie, zamiana, wpis do komórki, nowy slajd, zapis kopii) z pytaniem
  i treścią okna od wykonawcy; nadpisanie istniejącej kopii ma domyślny przycisk „Nie”. Szkic w Outlooku nie pyta,
  bo niczego nie zmienia i nie wysyła — wysyła użytkownik.
- Dokument otwarty u użytkownika w Wordzie albo Excelu jest edytowany na miejscu (ta sama instancja, `Documents`/`Workbooks`
  po `FullName`) i zapisywany. Gdy program nie działa, wykonawca uruchamia go niewidocznie do czytania, a po edycji
  pokazuje. Każde polecenie to osobny wątek STA (`Com.RunSta`), z limitem `OfficeOptions.Timeout` na okna dialogowe Office’a.
- Formuły: `excel_zapisz` próbuje `FormulaLocal` (polskie nazwy, średniki), a gdy Excel zwróci `#NAZWA?`, `Formula`
  (angielskie). Liczby z przecinkiem są liczbami; wartości czytane są formatowane po polsku, daty jako `RRRR-MM-DD`.
- Bez pola `chmura` i z `internet: false` w `profil.json`: treść dokumentów i maili zostaje na komputerze.

## OCR i PDF (umiejętność `ocr`, polecenia `pdf_*` w `office`)

```csharp
using LokalneAI.Harness.Ocr;                         // projekt LokalneAI.Harness.Ocr: Tesseract 5, PDFium (Docnet.Core), PDFsharp 6.1

var options = new OcrOptions
{
    AllowedRoots = { @"C:\ODS" },
    WorkingFolder = @"C:\ODS\Projekty\1095_WSP\skany",
    Languages = "pol+eng",                           // dane *.traineddata pobierają się same do %LOCALAPPDATA%\LokalneAI\tessdata
    Rotation = OcrRotation.Auto,                     // wykrywanie orientacji (osd); albo None / Clockwise90 / Rotate180 / Clockwise270
    Layout = OcrLayout.Auto,                         // PreserveLayout = tabele (odstępy kolumn zostają)
    MinConfidence = 0,                               // słowa poniżej progu są pomijane
    IgnorePatterns = { @"^Strona \d+ z \d+$" },      // linie do wyrzucenia (nagłówki, stopki)
    Dpi = 300,
};
var executor = new OcrExecutor(options);           // ocr_obraz, ocr_pdf, ocr_folder, pdf_info, pdf_czytaj, pdf_polacz, pdf_wytnij, pdf_obroc
var text = executor.Engine.RecognizeFile(@"C:\ODS\Projekty\1095_WSP\skany\pismo.jpg").Text;   // OCR bez modelu, np. dla obrazka ze schowka
```

- OCR to skrypt (Tesseract LSTM), nie model językowy: model dostaje gotowy tekst z pewnością. Tabele, wzory i rysunki
  Tesseract oddaje słabo — do nich model z obsługą obrazów.
- `pdf_czytaj` bierze warstwę tekstową z PDFium, a strony bez tekstu (skany) renderuje i rozpoznaje (`OcrWhenNoText`).
  `pdf_polacz`, `pdf_wytnij`, `pdf_obroc` zawsze zapisują nowy plik (istniejących nie nadpisują) i pytają o zgodę.
- Natywne biblioteki: `x64\tesseract50.dll`, `leptonica-1.82.0.dll`, `runtimes\win-x64\native\pdfium.dll` — paczki kopiują je
  obok programu gospodarza (x64). PDFium nie jest wielowątkowy, `PdfTools` ma własną blokadę.
- W oknie: Ustawienia → OCR (`OcrViewModel.ToOptions`), obrazek wklejony do pola rozmowy (`HarnessPanel.ImagePasted`) jest
  zapisywany w folderze rozmowy i rozpoznawany od razu — tekst z nagłówkiem trafia do pola.

## Modele RFEM 5 bez Dlubala (umiejętność `rfem`)

```csharp
using LokalneAI.Harness.Rfem;                        // projekt LokalneAI.Harness.Rfem, bez paczek

var executor = new RfemExecutor(new RfemOptions { AllowedRoots = { @"C:\ODS" }, WorkingFolder = @"C:\ODS\Projekty\1095_WSP\RFEM" });
// rfem_info, rfem_pret, rfem_wezel, rfem_przekroje, rfem_przypadki, rfem_sily, rfem_szukaj_pretow + szukaj_plikow, pokaz_zawartosc

using var document = Rf5Document.Open(@"C:\ODS\Projekty\1095_WSP\RFEM\kanal.rf5");   // cały plik do pamięci, bez uchwytu
var member = document.Model.Members[28];
var length = document.MemberLength(28);                                 // m, z węzłów linii
var section = document.Model.SectionOf(member);                         // Description „HE A 160 | DIN 1025-3:1994”, Area [m²], Iy [m⁴]
if (document.HasResults)
{
    var set = document.Results.Find("KO5");                            // z protokołu solvera (.PRON<nr>: NCOMB=5)
    foreach (var point in document.Results.MemberForces(set, 28))
    {
        Console.WriteLine($"x={point.X:0.000} m  N={point.N / 1e3:0.00} kN  My={point.My / 1e3:0.00} kNm");
    }
}
```

- Tylko odczyt: żadne polecenie nie pyta o zgodę; zła ścieżka albo plik spoza dozwolonych folderów to odrzucenie z komunikatem.
- Układ pliku jest z inżynierii wstecznej (RFEM 5.27, kontener ZIP i OLE): nieznane rekordy są pomijane i liczone w `Model.Warnings`.
  Nie czyta powierzchni, podpór, obciążeń, wyników modułów ani plików RFEM 6 (.rf6).
- Jednostki w obiektach są w SI (m, N, Nm, Pa); wykonawca formatuje dla ludzi (m, kN, kNm, cm², cm⁴, mm, przecinek dziesiętny).

## Licznik kontekstu

`session.Context` (`ContextUsage`) podaje zajęte tokeny, limit i procent, np. „8 063 / 16 384 tokenów (49%)”.
Liczbę bierzemy z odpowiedzi serwera (`prompt_eval_count + eval_count`). Ollama liczy tu cały prompt,
także część z pamięci podręcznej (sprawdzone na 0.40.14). Przed pierwszą odpowiedzią liczba jest szacunkiem
(`IsEstimate`). Gdy sesja przekroczy 85% kontekstu, biblioteka skraca starszą część rozmowy i zgłasza ostrzeżenie.
Panel WPF pokazuje licznik jako pasek nad rozmową.

## Profil `cde` — pytania do bazy

```csharp
var executor = new PostgresReadOnlyExecutor(new PostgresReadOnlyOptions { ConnectionString = "Host=…;Database=cde;Username=…;Password=…" });
var options = new SessionOptions
{
    Variables = { ["ja"] = personIdPytajacego },   // model pisze '{{ja}}', biblioteka podstawia uuid
    ContextNote = "Pytający ma w bazie identyfikator osoby {{ja}} — w SQL wpisuj dosłownie '{{ja}}'.",
};
```

Zapytanie od modelu przechodzi przez kilka zabezpieczeń:

1. Strażnik SQL: jedno zapytanie, tylko `SELECT`/`WITH`, bez słów i funkcji zmieniających dane.
2. Reguły profilu:
   - `v_my_hours` wymaga filtra `{{ja}}`;
   - tabele z danymi osobowymi są zablokowane.
3. Wykonanie w transakcji `READ ONLY` z limitem czasu i zawsze z `ROLLBACK`.
4. Sprawdzenie, czy liczby w odpowiedzi pochodzą z wyników. Jeśli nie, model dostaje jedną szansę na poprawkę,
   a potem odpowiedź jest oznaczona `NumbersVerified = false`.

**Rola w bazie musi być tylko do odczytu.** Powyższe warstwy to zabezpieczenie dodatkowe, nie zamiennik uprawnień.

## Inny serwer modelu

Zamiast Ollamy może to być dowolny serwer zgodny z API OpenAI (llama.cpp `llama-server`, LM Studio, vLLM):

```csharp
var llm = new OpenAiCompatibleLlmClient(new OpenAiCompatibleOptions
{
    Url = "http://serwer-ai:8080", Model = "bielik", ApiKey = "…", ContextLength = 16384,
});
```

Konfiguracja serwera w biurze: [serwer-modelu.md](serwer-modelu.md). Format profilu: [profile.md](profile.md).

## Przez okno LokalneAI (kolejka i wybór komputera)

```csharp
var llm = new OllamaLlmClient(new OllamaOptions
{
    Url = LocalGateway.ChooseUrl(),   // okno LokalneAI działa → jego brama; nie działa → Ollama na tym PC
    ClientName = "Tekla",             // nazwa w kolejce okna
});
```

Gdy na komputerze działa okno LokalneAI, program czeka w tej samej kolejce co czat, a liczy komputer wybrany
w oknie (ten albo mocniejszy w biurze). Szczegóły: [siec.md](siec.md).
