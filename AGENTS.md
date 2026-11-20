# AGENTS.md: Amadeusz (LokalneAI)

Instrukcja dla agentów AI (Claude Fable, Claude Opus, Cursor i inni), którzy pracują w tym repozytorium.
Claude Code czyta ją przez `CLAUDE.md`.

## Co to jest

Pół-harness dla lokalnych modeli językowych (Bielik w Ollamie) i okno testowe. Biblioteka jest osadzana w programach
biura ODS: użytkownik pisze po polsku, model wybiera polecenie z profilu, biblioteka je sprawdza, a program-gospodarz
wykonuje. Nic nie wychodzi do internetu. Nazwa repozytorium to Amadeusz, a projekty w kodzie nazywają się `LokalneAI.*`.

**Jeśli osadzasz bibliotekę w programie albo ją zmieniasz, najpierw przeczytaj
[docs/biblioteki-dla-agentow.md](docs/biblioteki-dla-agentow.md).** Jeśli masz **użyć** działającego Amadeusza (zapytanie
do modelu przez bramę, podpięcie programu, umiejętność bez klikania, odczyt ustawień), czytaj
[docs/sterowanie.md](docs/sterowanie.md); porty: [docs/porty.md](docs/porty.md).

## Mapa

| Ścieżka | Co to jest |
|---|---|
| `LokalneAI.Harness` | rdzeń (.NET Standard 2.0): sesja, profile, wykonawcy, strażnicy, klienci modelu; podagenci (`Session\SubAgents.cs`: `ISubAgentHost`, `SubAgentRunner`, narzędzie `zlec_podagentowi` — zlecenia z jednego kroku równolegle); obrazy dla modelu (`LlmMessage.Images`, `HarnessSession.SendAsync(tekst, obrazy)`); tablica (`Whiteboard\Board.cs`: `BoardSnapshot` — opis tablicy dla modelu, `WhiteboardExecutor` — rysuj, wyczysc_rysunki) |
| `LokalneAI.Harness.Wpf` | panel rozmowy `HarnessPanel` (net48 i net10.0-windows): wpisy „Krok N” (`ShowModelSteps`), kafelki podagentów nad polem polecenia (`AgentTile`, „Przerwij”) i wpis `Agent` z przebiegiem jako drzewo (`PanelEntry.Children`); wklejony obrazek do modelu (`ImagesForModel`, miniatury nad polem, `ClipboardImages`) |
| `LokalneAI.Harness.Postgres` | SQL tylko do odczytu dla profilu `cde` (Npgsql 8) |
| `LokalneAI.Harness.Office` | Word, Excel, Outlook i PowerPoint dla profilu `office` przez COM zainstalowanego Microsoft 365 (późne wiązanie, bez paczek Interop; `OfficeExecutor`, `OfficeOptions`) |
| `LokalneAI.Harness.Ocr` | OCR skryptem (Tesseract 5, dane tessdata_fast pobierane do `%LOCALAPPDATA%\LokalneAI\tessdata`) i PDF (PDFium przez Docnet.Core: tekst i render stron; PDFsharp 6.1 MIT: scalanie, wycinanie, obrót) dla umiejętności `ocr` i poleceń `pdf_*` w `office`; `OcrExecutor`, `OcrEngine`, `OcrOptions`, `PdfTools`, `OcrLanguages` |
| `LokalneAI.Harness.Rfem` | czytnik plików RFEM 5 (.rf5) bez programu Dlubal dla umiejętności `rfem`: kontener ZIP/OLE (`Rf5Archive`, własny `CompoundFile`), tabele modelu z inżynierii wstecznej (`Rf5Parser`: węzły, linie, pręty, przekroje, materiały, PO/KO/KW), siatka (`Rf5Mesh`), siły w prętach z plików solvera (`Rf5Results`), `RfemExecutor`; otwarty RFEM 5 przez RF-COM (`Com\RfemCom.cs` — interfejsy vtable wygenerowane z biblioteki typów v5.7, `RfemComSession`, `RfemLive`: polecenia `rfem_otwarty*`, `rfem_wskaz`) |
| `LokalneAI.Harness.Tests` | testy xUnit, uruchamiane na net10.0 i net48 |
| `LokalneAI.App` | okno `LokalneAI.exe`: zakładki Czat, Code (asystent z uprawnieniami), Whiteboard (`WhiteboardView`: tablica, `CaptureWindow`, sesja przez `HarnessView.CreateBoardSession`) i Ustawienia (gdzie liczyć z listą komputerów dla podagentów, modele w chmurze, udostępnianie, kolejka); lista modeli z ulubionymi; wygląd jak TransmittalMaster (`Theme.xaml`: te same klucze kolorów i stylów, własny pasek tytułu z kartami); gospodarz podagentów `SubAgentHost` (komputer z listy, model do karty przez `ModelChooser` z `LokalneAI.Core`), lista komputerów `AgentMachinesViewModel` |
| `LokalneAI.Gateway` | brama okna (Kestrel, net10.0): kolejka zapytań, liczenie na innym komputerze, udostępnianie w podsieci ([docs/siec.md](docs/siec.md)) |
| `LokalneAI.Core`, `LokalneAI.Core.Tests` | klient Ollamy okna i testy (także bramy: `GatewayTests`) |
| `LokalneAI.Eval` | testy profili z prawdziwym modelem, rozmiar promptu, aktualizacja i przeszukiwanie wiedzy |
| `LokalneAI.Przyklad.WinForms` | najprostsze osadzenie (WinForms, .NET Framework 4.8) |
| `profile\<nazwa>` | profile asystenta: `tekla`, `cde`, `pliki`, `komputer`, `mkk` (sama wiedza: programy Marka Kowolika i Robot, RFEM, IDEA, IFC z repozytorium MKK), `office` (dokumenty Word, Excel, Outlook, PowerPoint i PDF), `tlumaczenie` (sama wiedza: tłumaczenie tekstów biura na angielski ze słownikiem konstrukcyjnym), `ocr` (tekst ze skanów, zdjęć i PDF-ów bez warstwy tekstowej), `rfem` (modele RFEM 5 z pliku .rf5 bez Dlubala albo z otwartego RFEM 5 przez RF-COM: pręty, przekroje, długości, kombinacje, siły, reakcje, wskazanie kliknięciem), `tablice` (sama wiedza: tablice Żyburtowicza, połączenia DAST, SZS C5 — dane z OCR w `wiedza\dane\*.json`, `.md` generowane), zestaw `cs_program`, `cs_ifc`, `cs_konstrukcja`, `cs_polaczenia`, `cs_statyka`, `cs_rysunki`, `cs_robot` (sama wiedza z repozytorium CS-cheak_structures, podział w `profile\cs_program\README.md`; dokumentacja RFEM z CS w `rfem`), `whiteboard` (tylko zakładka Whiteboard, ukryta w Code przez `ProfilesFolder.Hidden`: rysowanie na warstwie Amadeusza), `amadeusz` (sam Amadeusz: wiedza o oknie, portach, bramie, podpinaniu, `--autotest`, ustawieniach — przepisana z `docs\`; polecenia do odczytu stanu, ustawień, umiejętności i dzienników oraz `wybierz_umiejetnosci` — wykonawca `SelfExecutor` w `LokalneAI.App\HarnessView.Amadeusz.cs`; tylko modele lokalne i Claude) ([docs/profile.md](docs/profile.md)) |
| `szablony\` | szablon profilu i plików dla repozytoriów programów biura |
| `narzedzia\` | budowanie instalatora, porównanie modeli, `sprawdz_przyklady` (kompilacja przykładów z dokumentacji) |
| `instalator\` | skrypty instalacji u użytkownika |
| `docs\` | dokumentacja; spis niżej |

## Budowanie i testy

```powershell
dotnet build LokalneAI.sln          # gdy WPF się sypie przy budowaniu równoległym: dodaj -m:1
dotnet test LokalneAI.sln           # testy nie potrzebują Ollamy ani bazy
dotnet run --project narzedzia\sprawdz_przyklady -f net10.0-windows   # przykłady z docs\biblioteki-dla-agentow.md
.\LokalneAI.App\bin\Debug\net10.0-windows\LokalneAI.exe --autotest "pytanie" --wynik <folder>   # okno bez klikania
```

- Budowanie ma dawać **0 ostrzeżeń**, także CS1591 (brak `///` przy publicznym elemencie bibliotek).
- Testy z prawdziwym modelem (`LokalneAI.Eval test`) i zrzuty okna wymagają działającej Ollamy na `127.0.0.1:11434`.

## Zasady, których nie wolno łamać

1. **Nie testuj i nie skanuj dysku `A:`** (udział sieciowy z danymi budowy). Pracuj na kopiach lokalnych.
2. **Nie kasuj ani nie zastępuj istniejących plików** bez pytania użytkownika. Nowe rzeczy dodawaj obok.
3. **Commit i push tylko na wyraźne polecenie użytkownika.** Przed commitem przeszukaj zmiany pod kątem:
   - haseł,
   - `person_id`,
   - nazwisk i godzin osób,
   - plików `*.local.json` i folderu `wyniki\`.
4. **Baza CDE tylko w transakcji READ ONLY.** Rola, na którą łączy się program, może pisać. Nigdy nie używaj
   `polaczenie_admin.local.json`.
5. **Nie zapisuj do folderu `exchange` mostu TeklaBridge**, bo skrypt wykona się na otwartym modelu Tekli. Do prób służy
   `SimulatedModelExecutor`.
6. **Nie wołaj `POST /api/pull` w Ollamie**, żeby coś sprawdzić: na pobranym modelu to go aktualizuje (zmienia szablon).
   Do sprawdzania służą `/api/tags` i `/api/ps`.
7. **Biblioteki muszą działać na .NET Framework 4.8.** `LokalneAI.Harness` i `.Postgres` to netstandard2.0, bez API
   i zależności spoza niego.
8. **Bezpieczeństwo poleceń.** Model niczego nie wykonuje sam. Nowe możliwości dodaje się jako narzędzia z opisem,
   sprawdzaniem argumentów i pytaniem o zgodę przy zmianach (`IConfirmingExecutor`, `ConfirmMutation`).
9. **Brama nie otwiera niczego więcej niż liczenie.** Lista dozwolonych ścieżek jest w `GatewayServer.Routes`.
   Pobieranie, usuwanie i tworzenie modeli zostają zablokowane. Ollama słucha tylko na `127.0.0.1`. Zapytania spoza
   podsieci i z przeglądarek (nagłówek `Origin`) są odrzucane.
10. **Nie otwieraj portów w sieci podczas prób.** Testy i kontrola bez klikania używają `LoopbackCountsAsLan` /
    `--udostepnij`, bo port tylko na `127.0.0.1` nie wywołuje okna zapory Windows. Nie uruchamiaj
    `FirewallSetup.SetupAsync` (zmienia zaporę, wymaga administratora) bez prośby użytkownika.
11. **Chmura: tylko Anthropic, OpenAI i Google, tylko gdzie profil pozwala.** Decyzja użytkownika (2026-11-07):
    profil `cde` — modele lokalne i Claude; DeepSeek wyłącznie lokalnie. Pilnują tego `CloudProvider`
    i `HarnessSession` (pole `chmura` w `profil.json`). Nie dopisuj dostawców do profili bez prośby użytkownika.
    **Nie wysyłaj płatnych zapytań do chmury bez zgody użytkownika** — testy na podstawionym `HttpMessageHandler`.
    Kluczy API nie czytaj, nie wypisuj i nie zapisuj poza `CloudKeyStore` (`%APPDATA%\LokalneAI\klucze.json`, DPAPI).
12. **Hasła, loginy i adresy serwerów nigdy w repozytorium.** Dane baz, FTP i SMB trzyma `CredentialVault` w Menedżerze
    poświadczeń Windows (okno: Ustawienia → Menedżer poświadczeń); w kodzie i dokumentacji tylko przykłady (`serwer`, `baza`). `narzedzia\sprawdz_sekrety.ps1` sprawdza commit (hak `.git\hooks\pre-commit`) i całe repozytorium
    (test `SekretyTests`). Nie omijaj go (`--no-verify`); fałszywy alarm popraw we wzorcu, nie w danych.
13. **Strażnik bezpieczeństwa (`SafetyGuard`) zostaje włączony i nie słabnie bez zgody użytkownika.** Każde wywołanie narzędzia
    przechodzi przez niego przed zgodą i wykonaniem: model nie zmienia samego Amadeusza, nie uruchamia go, nie otwiera furtek
    sieciowych, nie czyta sekretów, nie dodaje autostartu, nie wyłącza zabezpieczeń, nie wynosi danych, nie wykonuje kodu
    w ukryciu i nie sięga do innych komputerów ([docs/bezpieczenstwo.md](docs/bezpieczenstwo.md), decyzja z 2026-11-19).
    Nowe narzędzie, które zapisuje pliki albo uruchamia programy, musi być objęte strażnikiem (parametry ścieżek, `IWorkingFolderProvider`)
    i dostać przypadki w `SafetyTests` albo w korpusie `LokalneAI.Harness.Tests\Dane\straznik_korpus.jsonl`.

## Konwencje

- **Identyfikatory w kodzie po angielsku.** Komentarze, dokumentacja, komunikaty, opisy narzędzi i pliki profili
  po polsku.
- **C#:** `LangVersion latest`, `Nullable disable`, w bibliotekach bez `ImplicitUsings`.
- **Skrypty `.ps1` dla Windows PowerShell 5.1:** zapisuj w UTF-8 z BOM, inaczej polskie znaki się psują.
- **Python:** uruchamiaj z `-X utf8`.
- **Tekst z ukośnikami** (ścieżki Windows, wyrażenia regularne) zapisuj narzędziem do edycji plików, nie przez heredoc
  w Bashu, bo heredoc gubi `\\`.
- **Wyniki testów z modelem** idą do `wyniki\`, który jest poza gitem, bo są tam godziny ludzi.

## Opisy dla AI to część zmiany

Zasady: [docs/zasady-opisow-dla-ai.md](docs/zasady-opisow-dla-ai.md). W tym repozytorium oznacza to:

- **Publiczne API:** `///` przy każdym elemencie i aktualna [docs/biblioteki-dla-agentow.md](docs/biblioteki-dla-agentow.md).
  Test `DokumentacjaTests` sprawdza mapę API i linki w dokumentacji.
- **Sposób użycia:** zmiana w przepisach w `biblioteki-dla-agentow.md` wymaga tej samej zmiany
  w `narzedzia\sprawdz_przyklady`.
- **Format profilu:** [docs/profile.md](docs/profile.md) i `szablony\profil`.
- **Nowe polecenie w profilu:** opis w `narzedzia.json`, przykłady w `przyklady.jsonl` (w tym dopytanie) i test
  w `testy.jsonl`.
- **Nowy przełącznik `LokalneAI.exe`, polecenie kontroli (`#…`), trasa bramy, port albo przycisk w Ustawieniach:**
  opis w [docs/sterowanie.md](docs/sterowanie.md) (porty także w [docs/porty.md](docs/porty.md)) i ten sam fakt
  w `profile\amadeusz\wiedza\` (sekcja do 900 znaków). Test `AmadeuszWiedzaTests` porównuje przełączniki, polecenia,
  trasy i porty z kodem.
- **Zmiana zasad bezpieczeństwa:** kod w `SafetyRules.cs`, opis w [docs/bezpieczenstwo.md](docs/bezpieczenstwo.md) i w
  `profile\amadeusz\wiedza\bezpieczenstwo.md`, przypadki w `SafetyTests` i w korpusie; `LokalneAI.Eval straznik` pokazuje tabelę
  zgodności korpusu, `LokalneAI.Eval atak` — zachowanie prawdziwego modelu na scenariuszach wstrzyknięć (`LokalneAI.Eval\Dane`).

## Pułapki

- **Ollama i kontekst:** obcina po cichu prompt dłuższy niż `ContextLength`. Profil `tekla` potrzebuje 16384.
- **Licznik tokenów:** `prompt_eval_count` w Ollamie liczy cały prompt, także część z pamięci podręcznej (sprawdzone na Ollamie 0.40:
  ten sam prompt drugi raz — ta sama liczba, czas czytania z 3,8 s na 0,06 s). Dlatego przyrost tokenów między wywołaniami
  (`ModelCallMetrics.AddedTokens`) to różnica pełnych promptów. Ollama nie ma `/api/tokenize` (404); tokeny samych zasad liczy
  zapytanie z `num_predict: 1` — `num_predict: 0` znaczy „bez limitu”, nie „nic nie pisz”.
- **Strażnik bezpieczeństwa to nie piaskownica:** ocenia tekst polecenia (wzorce po ujednoliceniu, wywołania z `PowerShellPolicy.Inspect`,
  ścieżki, adresy) i blokuje wszystko, co wygląda na ukrywanie — dlatego model dostaje zasadę „pisz polecenia wprost”. Fałszywy alarm
  popraw zasadą z przypadkiem w korpusie (`dozwolone`), nie wyłączaniem strażnika. Natywne polecenia, które tylko czytają (`reg query`,
  `netsh … show`, `git status`), rozpoznaje `SafetyRules.ReadOnlyNative`. W próbach `atak` Qwen3.5 9B dawał się namówić notatce na
  regułę zapory — zatrzymał go dopiero strażnik, więc zasady w `system.md` nie zastąpią kodu (z `--wymus-czytanie` mimo zasad:
  Qwen 10, Gemma 12 prób szkody na 32 scenariusze; jedyna, która przeszła — sprawdzenie portu na innym komputerze, W27 — jest teraz
  „zawsze pytaj”; wyniki: [docs/badania/bezpieczenstwo-proby.md](docs/badania/bezpieczenstwo-proby.md)).
- **Myślenie modeli:** qwen3.5 potrafił „myśleć” kilka minut przed krokiem w JSON (limit 300 s w Eval). `OllamaLlmClient`
  wysyła `"think": false` (`OllamaOptions.AllowThinking = false`); Ollama przyjmuje to pole także dla modeli bez myślenia.
- **Słabe rachunki modelu:** Bielik źle liczy w pamięci. Sumy liczy baza (SQL) albo narzędzie `kalkulator`,
  a `NumberGuard` sprawdza liczby w odpowiedzi.
- **Ukośniki od modelu:** małe modele psują ukośniki w ścieżkach (`\t` zamiast `\` i podwójne `\\`).
  `HarnessSession.RepairEscapes` to naprawia; nie usuwaj tego.
- **OCR i PDF (`LokalneAI.Harness.Ocr`):** Tesseract i PDFium to natywne dll (`x64\tesseract50.dll`, `leptonica`, `runtimes\win-x64\native\pdfium.dll`)
  kopiowane przez paczki do folderu programu; dane języków (`*.traineddata`) nie leżą w repozytorium — `OcrLanguages` pobiera je
  z tessdata_fast przy pierwszym użyciu albo przyciskiem w Ustawieniach → OCR. PDFium renderuje z przezroczystym tłem:
  `PngWriter` składa alfę na biel (inaczej czarny tekst znika). `PDFsharp` zostaje na **6.1.1** — 6.5 sprawdza licencję
  (`PdfSharpLicenseException`). PDFium nie jest wielowątkowy (`PdfTools` ma blokadę). W programie jednoplikowym (`Amadeusz.exe`, `PublishSingleFile`)
  Tesseract nie znajduje swoich dll sam (`Assembly.Location` jest puste → `TargetInvocationException`): `NativeLibraries.Prepare`
  ustawia `TesseractEnviornment.CustomSearchPath` na folder rozpakowania z `NATIVE_DLL_SEARCH_DIRECTORIES` — próby OCR rób też
  na `Amadeusz.exe`, nie tylko na `bin\Debug`. Testy na żywo (`OcrLiveTests`) tworzą PDF
  w PDFsharp z czcionką Arial z Windows i przechodzą bez sprawdzania, gdy brak danych języków.
- **Pliki RFEM 5 (`LokalneAI.Harness.Rfem`):** układ strumienia modelu jest z inżynierii wstecznej (pięć modeli biura z RFEM 5.27), nie
  z dokumentacji — rekordy obiektów kończy znacznik `OwnerParamGroup`, a tabele przekrojów, materiałów, przypadków i kombinacji mają
  stałe układy opisane w komentarzach `Rf5Parser`. Zmieniając parser, sprawdź na prawdziwym pliku (`RfemLiveTests` ze zmienną
  `LOKALNEAI_RFEM_PROBKA`), nie zgaduj; pliki klientów (modele z OneDrive) nie trafiają do repozytorium — testy jednostkowe budują
  rekordy syntetycznie (`Rf5Bytes`). Który zestaw wyników to która kombinacja, czyta się z protokołu solvera `.PRON<nr>` (`NCOMB=`),
  nie z kolejności plików. Plik jest czytany cały do pamięci, żeby nie trzymać uchwytu (RFEM może go w tym czasie zapisywać).
  Nagłówek rekordu obiektu to `[i32 numer][tekst 1][tekst 2][00×6]` przed ciągiem jedynek, teksty `[u8 len]` albo `[FF][u16 len]`
  (`Rf5Parser.ReadHeader`): model wyeksportowany z Tekli ma w każdym pręcie 266-znakowe `<TeklaItemMetadata>` w jednym z tych tekstów
  i parser znający tylko krótki komentarz gubił 2/3 prętów albo brał numer z wnętrza tekstu (model F10, 2026-11-19: „pręta 14 nie ma”).
  Wyniki samego przypadku n liczonego razem z kombinacjami solver zapisuje pod `NCOMB = 10000 − n` (`Rf5Results.ReadProtocol`).
- **RF-COM, otwarty RFEM 5 (`LokalneAI.Harness.Rfem\Com`):** interfejsy RF-COM to vtable po IUnknown, nie IDispatch — późne wiązanie
  (`Type.InvokeMember`, `dynamic`, pywin32) daje E_NOINTERFACE. `RfemCom.cs` jest wygenerowany z biblioteki typów v5.7 (zrzut comtypes) z dokładną
  liczbą miejsc w vtable i zaślepkami `_NNN_Nazwa` — nie przestawiać ani nie usuwać; `RfemComLayout.Problem()` sprawdza nazwy metod
  w zarejestrowanej bibliotece przed pierwszym użyciem, a `RfemComUnitTests` rozmiary struktur i liczbę miejsc. Bez `LockLicense()` każde
  wywołanie wraca z CLASS_E_NOTLICENSED, a trzymana blokada blokuje okno RFEM — `RfemComSession` zwalnia ją w `Dispose`, więc połączenie
  żyje tylko przez jedno polecenie. CLR zamienia HRESULT-y RFEM-a na różne wyjątki („obiekt nie został odnaleziony” → FileNotFoundException),
  dlatego łapie się `RfemCom.IsComFailure(e)`, nie samo COMException. Zaznaczenia w oknie RFEM nie da się odczytać (biblioteka v5.7 nie ma
  takiej metody): `rfem_wskaz` woła `IView.PickObjects`, RFEM otwiera okno wyboru i czeka na OK użytkownika. Testy na żywo
  (`RfemComLiveTests`) tylko ze zmienną `LOKALNEAI_RFEM_COM=1` i uruchomionym RFEM 5.
- **COM Office’a (profil `office`):** wywołania idą przez `Type.InvokeMember` z `GetProperty | InvokeMethod` (w bibliotekach
  typów Office’a `Item` bywa metodą), zawsze w wątku STA (`Com.RunSta`). Instancja uruchomiona przez nas startuje niewidoczna
  i jest zamykana po czytaniu; po edycji dokument zostaje otwarty na wierzchu. Próba, która się wysypie, zostawia
  niewidoczny `WINWORD.EXE`/`EXCEL.EXE` — zamknij go przez COM (`Documents`/`Workbooks` → `Close` bez zapisu → `Quit`),
  nie `taskkill`, bo widoczna instancja może być użytkownika. Testy nie ruszają Outlooka (szkic otworzyłby okno w poczcie).
- **Sprzęt:** RTX 4060 8 GB mieści Bielika 11B tylko w 60–73% przy kontekście 16k. Odpowiedź trwa kilka do
  kilkudziesięciu sekund.
- **Kroki i podagenci:** jedno polecenie = pętla w `HarnessSession.RunTurnAsync` (najwyżej `max_krokow`, domyślnie 12;
  2026-11-19 podwojone we wszystkich profilach do prób); każdy krok to osobne `POST /api/chat` z całą rozmową — serwer
  modelu nie ma stanu, dzieli biblioteka. Zlecenia `zlec_podagentowi` z jednego kroku startują równolegle i czekają po pętli
  wywołań (`DelegatedCall`), wyniki wracają w kolejności wywołań; sesja podagenta dostaje `SessionOptions` bez `SubAgents`
  (podagent nie zleca dalej), bez historii i bez pomiaru zasad. Pytanie podagenta do użytkownika wraca agentowi głównemu
  jako nieudany wynik (`SubAgentRunner`), przerwanie kafelkiem — też, a Stop tury rzuca `OperationCanceledException`.
  „Ten komputer” wśród komputerów podagentów liczy przez bramę okna z nagłówkiem `X-LokalneAI-Przekazane: 1` (brama nie
  przekazuje dalej, więc liczy własna karta mimo „Gdzie liczyć”); listę modeli bierze z Ollamy tego PC, nie z bramy.
  Sesję podagenta buduje `HarnessView.BuildSession` przez `Dispatcher.InvokeAsync` (czyta pola okna: foldery, most Tekli).
  Wpisy „Krok N” (`PanelEntryKind.Step`) powstają zawsze, a `ShowModelSteps` tylko je pokazuje; przebieg podagenta to
  `PanelEntry.Children` zapisywane jako `CodeConversationEntry.Children`.
- **Brakujące umiejętności (`ISkillCatalog`):** katalog dostaje tylko sesja główna Code i tylko z polem „Szukaj brakujących
  umiejętności” (`AppSettings.CodeSkillSearch`, domyślnie wyłączone od 2026-11-20 — model prosił o niepotrzebne umiejętności, np. przy
  pogodzie z wykresem, aż zapchał kontekst) (`BuildSession(canSwitchSkills: true)`,
  `HarnessView.Skills.cs`); podagent i tablica — nie. Narzędzia katalogu są wbudowane, więc omijają `SafetyGuard` — dlatego
  `wlacz_umiejetnosci` niczego nie włącza bez kliknięcia „Tak, włącz i ponów” (`HarnessPanel.AskAsync`; tylko kontrola
  `AutoConfirm` zatwierdza sama). Włączenie idzie przez `RequestSkills(…, "dodaj")` i `ApplyPendingSkills` (umiejętność
  `amadeusz`, po turze), a ponowienie — przez `RetryAfterSkillSwitch`, zapisany na `Panel.TurnEnded` PO `ApplyPendingSkills`
  (kolejność subskrypcji ma znaczenie: ponowienie musi iść do nowej sesji). Po zgodzie sesja kończy turę bez kolejnego
  wywołania modelu. „Nie mam narzędzia” (odpowiedź albo pytanie) rozpoznaje `SkillCatalog.LooksLikeMissingTool`; wtedy program sam
  szuka (bez ścieżek z polecenia — folder „…\Rfem” wskazywał umiejętność rfem), raz podsuwa wynik modelowi, a za drugim razem sam pyta
  o najlepiej pasującą (gemma4:12b po zwykłej uwadze tylko odpisał „Zrozumiałem” i nie wywołał narzędzia).
- **Obrazy i tablica:** obraz idzie do modelu tylko, gdy model go widzi (`ModelItem.SeesImages`: `vision` w `/api/tags`
  Ollamy, modele w chmurze — tak); inaczej sam tekst z OCR. Obrazy zostają w historii sesji przy poleceniu (każdy krok wysyła
  je ponownie — duże zrzuty zmniejsza `ClipboardImages.Shrink` do 1568 px). Tablica (`WhiteboardView`): rysunki użytkownika
  to `InkCanvas` — pióro i marker jako pociągnięcia, linie, strzałki i prostokąty jako pociągnięcia z rodzajem w danych
  pociągnięcia (`KindKey`), teksty jako `TextBlock` (edycja w chwilowym `TextBox`), obrazy jako `Image` z PNG i OCR w `Tag`;
  rysunki modelu to osobny `Canvas` z kształtami WPF (`IWhiteboardSurface`). Cofnij = migawki całej tablicy po każdej zmianie
  (`Commit`). Proporcjonalne skalowanie obrazu robi `SelectionResizing` (InkCanvas pokazuje w trakcie zwykłą ramkę, wynik jest
  proporcjonalny). Capture to osobne przezroczyste okno (`CaptureWindow`, `AllowsTransparency`, tło z alfą 1, żeby łapało
  mysz) — okna głównego nie da się zrobić przezroczystym bez przebudowy (`AllowsTransparency` tylko przed pokazaniem).
  `rysuj` jest „wszystko albo nic”: zły element (strzałka bez długości, prostokąt bez wysokości) daje błąd i model rysuje
  jeszcze raz — gemma4:12b potrafił dać strzałkę z początkiem w grocie.
- **Nie poddawaj się za łatwo (2026-11-19):** błąd „poza dozwolonymi folderami” (`FileSystemExecutor.Resolve`, `PowerShellPolicy`) sesja zamienia w pytanie użytkownika (`HarnessSession.WithFolderAccessAsync`, `ToolCall.AccessFolder`, zawsze `ConfirmationDangerous`), po zgodzie dodaje folder wykonawcom (`IFolderAccess` — każdy ma własną listę `AllowedRoots`) i ponawia polecenie przez strażnika; foldery systemowe (`FolderAccess.IsSystem`) bez pytania, odmowa raz na folder. Zmieniając te komunikaty błędów, popraw wzorce w `FolderAccess`. „Nie znalazłem” po mniej niż 3 wyszukiwaniach cofa model (`GiveUpGuard`), a katalog umiejętności nie wtrąca się, gdy problemem był dostęp (`FolderAccess.IsAccessProblem`). „Tylko czyta” pyta o wyjątek dla jednego polecenia (`HarnessView.ConfirmIn`).
- **Blok ```wykres (umiejętność `formatowanie`):** format JSON żyje w trzech miejscach, które muszą się zgadzać: czytanie
  w `ChartSpec.Parse` (`LokalneAI.Harness\Charts`, bez WPF — testowalne na net48 i net10), zasady dla modelu
  w `profile\formatowanie\system.md` (test `ChartSpecTests` sprawdza, że opisuje każde pole i że przykład się czyta) i rysowanie
  w `ChartView` (`LokalneAI.Harness.Wpf`). Nowa opcja = wszystkie trzy. Zakładka Czat bierze `system.md` tej umiejętności
  bezpośrednio (`MainViewModel.FormattingRules`). Sortowanie i odwrócenie kolejności są zrobione już w `Parse` — `Labels` i `Series`
  są w kolejności rysowania, więc „Kopiuj do Excela” też daje posortowane dane.

## Dokumentacja

| Plik | Dla kogo | O czym |
|---|---|---|
| [docs/sterowanie.md](docs/sterowanie.md) | agenci AI, integratorzy | jak sterować działającym Amadeuszem i podpinać programy: brama HTTP (trasy, nagłówki, błędy, przykłady), `--autotest`, pliki ustawień |
| [docs/porty.md](docs/porty.md) | administratorzy, agenci AI | porty 11434/11435/11436, zapora, połączenia wychodzące |
| [docs/bezpieczenstwo.md](docs/bezpieczenstwo.md) | wszyscy | co Amadeusz może robić sam ze sobą, a czego nie: blokady, „zawsze pytaj”, strażnik w kodzie, próby |
| [docs/biblioteki-dla-agentow.md](docs/biblioteki-dla-agentow.md) | agenci AI | jak użyć bibliotek: przepisy, kontrakt, mapa API |
| [docs/osadzanie.md](docs/osadzanie.md) | programiści | osadzanie z tłem i uzasadnieniem |
| [docs/profile.md](docs/profile.md) | autorzy profili | format plików profilu |
| [docs/wiedza-dla-modeli.md](docs/wiedza-dla-modeli.md) | wszyscy | gdzie wkładać dokumentację dla lokalnych modeli i jak ją pisać |
| [docs/zasady-opisow-dla-ai.md](docs/zasady-opisow-dla-ai.md) | programiści | kiedy i co opisywać dla AI |
| [docs/siec.md](docs/siec.md) | wszyscy | kolejka, liczenie na innym komputerze, udostępnianie w podsieci, bezpieczeństwo |
| [docs/serwer-modelu.md](docs/serwer-modelu.md) | administrator | Ollama na serwerze w biurze |
| [docs/badania/funkcje-aplikacji.md](docs/badania/funkcje-aplikacji.md) | wszyscy | co mają inne aplikacje i harnessy, kolejność dalszych prac |
| [docs/wybor-modelu.md](docs/wybor-modelu.md) | wszyscy | porównanie modeli |
| [README.md](README.md) | wszyscy | obsługa okna, instalator, testy |
