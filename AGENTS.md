# AGENTS.md: Amadeusz (LokalneAI)

Instrukcja dla agentów AI (Claude Fable, Claude Opus, Cursor i inni), którzy pracują w tym repozytorium.
Claude Code czyta ją przez `CLAUDE.md`.

## Co to jest

Pół-harness dla lokalnych modeli językowych (Bielik w Ollamie) i okno testowe. Biblioteka jest osadzana w programach
biura ODS: użytkownik pisze po polsku, model wybiera polecenie z profilu, biblioteka je sprawdza, a program-gospodarz
wykonuje. Nic nie wychodzi do internetu. Nazwa repozytorium to Amadeusz, a projekty w kodzie nazywają się `LokalneAI.*`.

**Jeśli osadzasz bibliotekę w programie albo ją zmieniasz, najpierw przeczytaj
[docs/biblioteki-dla-agentow.md](docs/biblioteki-dla-agentow.md).**

## Mapa

| Ścieżka | Co to jest |
|---|---|
| `LokalneAI.Harness` | rdzeń (.NET Standard 2.0): sesja, profile, wykonawcy, strażnicy, klienci modelu |
| `LokalneAI.Harness.Wpf` | panel rozmowy `HarnessPanel` (net48 i net10.0-windows) |
| `LokalneAI.Harness.Postgres` | SQL tylko do odczytu dla profilu `cde` (Npgsql 8) |
| `LokalneAI.Harness.Tests` | testy xUnit, uruchamiane na net10.0 i net48 |
| `LokalneAI.App` | okno testowe `LokalneAI.exe`: czat z modelem i przycisk „Pół-harness…” |
| `LokalneAI.Core`, `LokalneAI.Core.Tests` | klient Ollamy okna testowego i jego testy |
| `LokalneAI.Eval` | testy profili z prawdziwym modelem, rozmiar promptu, aktualizacja i przeszukiwanie wiedzy |
| `LokalneAI.Przyklad.WinForms` | najprostsze osadzenie (WinForms, .NET Framework 4.8) |
| `profile\<nazwa>` | profile asystenta: `tekla`, `cde`, `pliki`, `komputer` ([docs/profile.md](docs/profile.md)) |
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

1. **Nie testuj i nie skanuj dysku `A:`** (`\\10.0.0.30\budowa`, dane budowy). Pracuj na kopiach lokalnych.
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

## Pułapki

- **Ollama i kontekst:** obcina po cichu prompt dłuższy niż `ContextLength`. Profil `tekla` potrzebuje 16384.
- **Licznik tokenów:** `prompt_eval_count` w Ollamie liczy cały prompt, także część z pamięci podręcznej.
- **Słabe rachunki modelu:** Bielik źle liczy w pamięci. Sumy liczy baza (SQL) albo narzędzie `kalkulator`,
  a `NumberGuard` sprawdza liczby w odpowiedzi.
- **Ukośniki od modelu:** małe modele psują ukośniki w ścieżkach (`\t` zamiast `\` i podwójne `\\`).
  `HarnessSession.RepairEscapes` to naprawia; nie usuwaj tego.
- **Sprzęt:** RTX 4060 8 GB mieści Bielika 11B tylko w 60–73% przy kontekście 16k. Odpowiedź trwa kilka do
  kilkudziesięciu sekund.

## Dokumentacja

| Plik | Dla kogo | O czym |
|---|---|---|
| [docs/biblioteki-dla-agentow.md](docs/biblioteki-dla-agentow.md) | agenci AI | jak użyć bibliotek: przepisy, kontrakt, mapa API |
| [docs/osadzanie.md](docs/osadzanie.md) | programiści | osadzanie z tłem i uzasadnieniem |
| [docs/profile.md](docs/profile.md) | autorzy profili | format plików profilu |
| [docs/wiedza-dla-modeli.md](docs/wiedza-dla-modeli.md) | wszyscy | gdzie wkładać dokumentację dla lokalnych modeli i jak ją pisać |
| [docs/zasady-opisow-dla-ai.md](docs/zasady-opisow-dla-ai.md) | programiści | kiedy i co opisywać dla AI |
| [docs/serwer-modelu.md](docs/serwer-modelu.md) | administrator | Ollama na serwerze w biurze |
| [docs/wybor-modelu.md](docs/wybor-modelu.md) | wszyscy | porównanie modeli |
| [README.md](README.md) | wszyscy | obsługa okna, instalator, testy |
