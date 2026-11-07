# Amadeusz
ODS Harnes for local AI agents. Works without intenet connection on your own RTX 4060 8GB or better.

Roboczo program i biblioteki nazywają się **LokalneAI** (projekty `LokalneAI.*`, program `LokalneAI.exe`).

Lokalne modele AI (Bielik i inne, w [Ollamie](https://ollama.com)) w programach biura ODS. Nic nie wychodzi
do internetu: model liczy na karcie graficznej tego komputera albo serwera w biurze.

Są dwie części:

1. **Pół-harness** (`LokalneAI.Harness*`). Biblioteka do osadzenia w innych programach. Użytkownik pisze po polsku,
   model wybiera polecenia z katalogu (profil), biblioteka je sprawdza, a program-gospodarz wykonuje. Gotowe profile:
   - **tekla**: „wstaw słup HEB300 w 6000, 0 od zera do 3,53”, „zmień go na HEB320”, „wyselekcjonuj strop na poziomie 3,53”.
     Polecenia idą przez most TeklaBridge do Tekli 2019 albo do symulatora.
   - **cde**: „Ile przepracowałem godzin na projekcie WSP?”, „Ile ton stali S355 ma projekt 0546?”. Model pisze SQL
     tylko do odczytu, a sumy liczy baza.
   - **pliki**: „cd C:\ODS\Projekty, a potem utwórz folder Raporty”, „co jest w tym folderze?”. Umie tworzyć foldery,
     pokazywać zawartość, kopiować, przenosić i otwierać w Eksploratorze, tylko w dozwolonych folderach i bez kasowania.
   - **komputer**: to co `pliki`, a do tego polecenia PowerShella, uruchamianie programów, czytanie i wyszukiwanie
     plików: „ile jest PDF-ów z podfolderami?”, „w folderze R01 zmień R01 na R02 w nazwach”, „spakuj R02 do zipa”,
     „otwórz Notatnik”. Działa tylko w dozwolonych folderach. To, co tylko czyta, idzie od razu; każdą zmianę
     i uruchomienie programu użytkownik zatwierdza w okienku.

   Dokumentację dla modelu wkleja się do `profile\<nazwa>\wiedza\`. Repozytoria z dokumentacją dodaje się przyciskiem
   **Źródła wiedzy…** w oknie asystenta; pliki aktualizują się same ([docs/profile.md](docs/profile.md)).

   Jak osadzić: [docs/osadzanie.md](docs/osadzanie.md). Serwer modelu w biurze: [docs/serwer-modelu.md](docs/serwer-modelu.md).
   Kolejka i liczenie na innym komputerze: [docs/siec.md](docs/siec.md).
   Format profilu: [docs/profile.md](docs/profile.md). Wybór modelu: [docs/wybor-modelu.md](docs/wybor-modelu.md).

   **Opisy dla AI:**
   - agenci kodujący (Claude, Cursor) czytają [AGENTS.md](AGENTS.md) i [docs/biblioteki-dla-agentow.md](docs/biblioteki-dla-agentow.md);
   - gdzie wkładać dokumentację dla lokalnych modeli i jak ją pisać: [docs/wiedza-dla-modeli.md](docs/wiedza-dla-modeli.md);
   - zasady dla programistów: [docs/zasady-opisow-dla-ai.md](docs/zasady-opisow-dla-ai.md);
   - szablony profilu i plików dla repozytoriów programów: [szablony](szablony).
2. **Okno** (`LokalneAI.App`) wygląda jak TransmittalMaster (kolory i style w `LokalneAI.App\Theme.xaml`).
   Karty są w granatowym pasku tytułu:
   - **Czat**: rozmowa z modelem i statystyki „gdzie liczył”.
   - **Code**: asystent wykonujący polecenia (profile `tekla`, `cde`, `pliki`, `komputer`). Ma trzy poziomy uprawnień:
     *Tylko czyta*, *Pyta o każdą zmianę* i *Bez pytania (poza kasowaniem)*.
   - **Ustawienia**: gdzie liczyć (ten komputer albo mocniejszy komputer w biurze), udostępnianie tego komputera
     innym z tej samej podsieci, podgląd kolejki i parametry czatu.

   W oknie działa **brama** (`LokalneAI.Gateway`, port 11435). Zapytania czatu, zakładki Code, innych programów
   na tym PC i innych komputerów czekają przez nią w jednej kolejce, a zapytania z tego komputera mają pierwszeństwo.
   Opis: [docs/siec.md](docs/siec.md). Co mają inne aplikacje i co dalej:
   [docs/badania/funkcje-aplikacji.md](docs/badania/funkcje-aplikacji.md).

## Wymagania

- Windows 10 albo 11.
- Ollama. Instalacja w PowerShellu, bez praw administratora:
  ```powershell
  irm https://ollama.com/install.ps1 | iex
  ```
- Model (ok. 6,7 GB). Jeśli go brakuje, okno samo zaproponuje pobranie:
  ```powershell
  ollama pull SpeakLeash/bielik-11b-v3.0-instruct:Q4_K_M
  ```
- Do budowania .NET SDK 10. Paczka z instalatora (niżej) ma program self-contained, więc na innych PC .NET
  nie jest potrzebny.

## Budowanie i uruchomienie

```powershell
dotnet build LokalneAI.sln
.\LokalneAI.App\bin\Debug\net10.0-windows\LokalneAI.exe
```

## Obsługa

- **Enter** wysyła pytanie, **Shift+Enter** wstawia nową linię, **Esc** albo **Stop** przerywa.
- Pod każdą odpowiedzią widać, gdzie liczył model (ile modelu jest w pamięci karty, a ile na procesorze),
  liczbę tokenów, szybkość i czasy.
- **Zwolnij pamięć GPU** wyrzuca model z karty tego komputera od razu, np. przed pracą w Tekli. Bez tego Ollama
  zwalnia pamięć sama po 5 minutach bezczynności.
- **Pamiętaj rozmowę** (pod czatem) jest domyślnie wyłączone, więc każde pytanie liczy się od zera.
- **Ustawienia** zawierają:
  - gdzie liczyć: ten komputer albo inny, z przyciskiem „Szukaj w sieci”;
  - udostępnianie: przełącznik, port, limit kontekstu dla innych, otwarcie portu w zaporze;
  - kolejkę z przyciskiem „Odrzuć” dla zapytań innych komputerów;
  - polecenie systemowe czatu, temperaturę (Bielik: 0,1), długość kontekstu, zapis historii i adres Ollamy.
- Pasek na dole pokazuje, który komputer liczy, i stan kolejki.
- Gdy Ollama nie działa albo brakuje modelu, baner nad rozmową podpowiada, co zrobić
  (Uruchom Ollamę, Pobierz model, polecenie instalacji).

Odpowiedź jest pokazywana dokładnie tak, jak zwrócił ją model, łącznie ze znakami Markdowna (`**`).

## Gdzie są dane

| Plik | Zawartość |
|---|---|
| `%APPDATA%\LokalneAI\ustawienia.json` | ustawienia okna |
| `%LOCALAPPDATA%\LokalneAI\historia.jsonl` | pytania i odpowiedzi ze statystykami, jeden JSON w linii; tylko na tym PC, zapis można wyłączyć |
| `%LOCALAPPDATA%\LokalneAI\bledy.log` | nieoczekiwane błędy programu |

## Testy

```powershell
dotnet test LokalneAI.sln
```

Testy rdzenia nie potrzebują Ollamy: zamiast niej odpowiada udawany serwer.

Kontrola całego okna bez klikania (okno otwiera się poza ekranem i nie zabiera fokusu):

```powershell
.\LokalneAI.App\bin\Debug\net10.0-windows\LokalneAI.exe --autotest "Ile to 7,5 + 8 + 6,5?" --wynik C:\folder
```

Program zada pytanie, zapisze w podanym folderze `okno.png` i `wynik.json`, po czym się zamknie.
Nie zmienia przy tym ustawień ani historii. Dodatkowe przełączniki:

- `--autotest -`: sam zrzut okna po starcie, bez pytania;
- `--url <adres>`: inny adres Ollamy;
- `--model <nazwa>`: inny model;
- `--zakladka czat|code|ustawienia`: zakładka na zrzucie (`--rozwin` = Ustawienia);
- `--harness <profil>`: kontrola zakładki Code, polecenia w `--autotest` rozdziela `||`; zapisuje `harness.png`
  i `harness.json`. Zmiany zatwierdza sama, dlatego profile `pliki` i `komputer` pracują w folderze z `--foldery`
  albo, bez niego, w pustym folderze tymczasowym (`%TEMP%\LokalneAI_kontrola`);
- `--cel <komputer>`: licz na innym komputerze (tylko w tym uruchomieniu);
- `--udostepnij`: brama przyjmuje zapytania „innych komputerów” tylko z 127.0.0.1 (próba bez otwierania portu w sieci);
- `--port <n>`: port bramy w kontroli (domyślnie dowolny wolny, żeby nie zająć portu otwartego okna).

Kod wyjścia: 0 to odpowiedź, 1 to błąd w odpowiedzi, 2 i 3 to wyjątek.

## Układ

| Projekt | Co to jest | Platforma |
|---|---|---|
| `LokalneAI.Harness` | rdzeń pół-harnessu: sesja, schemat kroku, strażnicy, narzędzia wbudowane, klienci Ollamy i serwerów zgodnych z OpenAI, wykonawcy (symulator, TeklaBridge) | .NET Standard 2.0 |
| `LokalneAI.Harness.Postgres` | wykonawca SQL tylko do odczytu (profil `cde`) | .NET Standard 2.0, Npgsql 8 |
| `LokalneAI.Harness.Wpf` | panel rozmowy z licznikiem kontekstu do osadzenia | .NET Framework 4.8 i .NET 10 |
| `LokalneAI.Harness.Tests` | testy xUnit, uruchamiane na .NET 10 i .NET Framework 4.8 | |
| `LokalneAI.Eval` | porównanie modeli na testach profili | .NET 10 |
| `LokalneAI.Przyklad.WinForms` | najprostsze osadzenie w WinForms (.NET Framework 4.8) | |
| `LokalneAI.Gateway` | brama: kolejka, liczenie na innym komputerze, udostępnianie w podsieci | .NET 10, ASP.NET Core (Kestrel) |
| `LokalneAI.Core`, `LokalneAI.App`, `LokalneAI.Core.Tests` | okno, jego klient Ollamy i testy (także bramy) | .NET 10 |
| `profile\tekla`, `cde`, `pliki`, `komputer` | wiedza, polecenia, przykłady i testy profili | |
| `szablony\` | szablon profilu i plików `AGENTS`, `CLAUDE`, `lokalne-ai\` dla repozytoriów programów | |
| `narzedzia\sprawdz_przyklady` | kompiluje i uruchamia przykłady z `docs\biblioteki-dla-agentow.md` | .NET 10 i .NET Framework 4.8, C# 7.3 |
| `docs\` | osadzanie, opisy dla AI, wiedza dla modeli, serwer modelu, format profilu, wybór modelu, raporty z badań | |

## Instalator

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File narzedzia\zbuduj_instalator.ps1 -Wersja 0.3.0
```

Skrypt najpierw uruchamia testy, potem buduje `dist\LokalneAI-<wersja>.zip` (ok. 60 MB, program self-contained, więc
.NET nie jest potrzebny). Na komputerze użytkownika: rozpakować i uruchomić `instaluj.bat`. Nie są potrzebne prawa
administratora. Szczegóły w `instalator\CZYTAJ.txt`:

- `instaluj.bat`: Ollama i model na tym komputerze;
- `instaluj.bat -SerwerModelu http://serwer-ai:11434`: model na serwerze w biurze;
- `instaluj.bat -Model <tag>`: inny model.

## Porównanie modeli

```powershell
dotnet build LokalneAI.sln
.\LokalneAI.Eval\bin\Debug\net10.0\LokalneAI.Eval.exe rozmiar --profil profile\tekla --model <tag>
.\LokalneAI.Eval\bin\Debug\net10.0\LokalneAI.Eval.exe test --profil profile\tekla --model <tag> --ctx 16384
.\LokalneAI.Eval\bin\Debug\net10.0\LokalneAI.Eval.exe test --profil profile\cde --model <tag> --ctx 16384 `
    --polaczenie <plik.json z host, port, dbname, user, password> --ja <person_id pytającego>
powershell -NoProfile -ExecutionPolicy Bypass -File narzedzia\porownaj_modele.ps1   # wszystkie modele i profile
```

`rozmiar` mierzy, ile tokenów zajmuje polecenie systemowe profilu. `test` przechodzi zestaw `testy.jsonl`.
`szukaj --profil <folder> --fraza "kotwy M24"` pokazuje, co model dostałby z narzędzia `szukaj_wiedzy` (bez modelu),
a `wiedza --profil <folder>` aktualizuje dokumentację z `zrodla.json`.
Dla profilu `cde` każde zapytanie wzorcowe liczy się na żywej bazie (tylko odczyt) i porównuje z wynikiem SQL modelu.
Pełne odpowiedzi lądują w `wyniki\`, który jest poza gitem (są tam godziny ludzi).

## Dalej

- Dokończenie porównania modeli (Gemma 4, Qwen3.5 4B, Qwen bez „myślenia”) — [docs/wybor-modelu.md](docs/wybor-modelu.md).
- Rola bazy tylko do odczytu dla asystenta (na każdą osobę) i sposób łączenia od użytkownika.
- Test profilu `tekla` na kopii prawdziwego modelu przez most TeklaBridge (szablony są tylko skompilowane).
- Próba bramy między dwoma komputerami w biurze (na jednym PC sprawdzona przez `127.0.0.1`).
- Ikona w zasobniku i start z Windows dla komputerów, które udostępniają model; dalsze pomysły
  w [docs/badania/funkcje-aplikacji.md](docs/badania/funkcje-aplikacji.md).
