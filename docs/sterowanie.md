# Jak sterować Amadeuszem i podpinać go do innych narzędzi

Instrukcja dla agentów AI (Claude Code, Cursor, Codex, lokalny model z umiejętnością `amadeusz`) i dla ludzi, którzy
podpinają Amadeusza do swoich programów. Porty i zapora: [porty.md](porty.md). Brama i kolejka: [siec.md](siec.md).
Osadzanie biblioteki w programie .NET: [biblioteki-dla-agentow.md](biblioteki-dla-agentow.md).

## Najpierw pięć faktów

1. **Amadeusz to cztery części:** okno `LokalneAI.exe` (skrót „Amadeusz”), **brama HTTP** w oknie
   (`http://127.0.0.1:11435`), **Ollama** z modelami (`http://127.0.0.1:11434`) i **umiejętności** w folderze `profile\`.
2. **Brama mówi API Ollamy i API zgodnym z OpenAI.** Każdy program, który umie rozmawiać z Ollamą, zadziała z Amadeuszem
   po zmianie portu z 11434 na 11435. Zyskuje kolejkę, liczenie na innym komputerze („Gdzie liczyć”) i liczniki tokenów.
3. **Okna nie da się klikać przez API.** Zakładki Czat i Code obsługuje człowiek. Agent steruje Amadeuszem na cztery
   sposoby: zapytaniami HTTP do bramy, uruchomieniem kontrolnym `--autotest`, biblioteką .NET i plikami umiejętności.
4. **Jedna kopia programu na użytkownika.** Drugie uruchomienie pokazuje okno działającej kopii i się kończy.
   Wyjątek: uruchomienie kontrolne (`--autotest`) działa obok, z własną bramą na wolnym porcie.
5. **Zamknięcie okna (×) chowa je do zasobnika.** Brama działa, dopóki program działa. Wyłącza go dopiero
   „Zakończ Amadeusza” w menu ikony.

## Zasady dla agenta (obowiązują zawsze)

To zasady dla agenta, który steruje Amadeuszem z zewnątrz. Model wewnątrz Amadeusza (zakładka Code, Czat, podagenci) ma własne,
twarde granice w kodzie — strażnik bezpieczeństwa blokuje m.in. zmianę samego Amadeusza, uruchamianie go, furtki sieciowe
i sekrety, także za zgodą użytkownika: [bezpieczenstwo.md](bezpieczenstwo.md).

| Nie rób | Dlaczego / zamiast |
|---|---|
| `POST /api/pull`, `/api/delete`, `/api/create`, `/api/copy`, `/api/push` (także wprost w Ollamie) | pobranie na istniejącym modelu go aktualizuje i zmienia szablon; brama i tak zwraca 404. Stan sprawdzaj przez `GET /api/tags` i `/api/ps` |
| edycja `%APPDATA%\LokalneAI\ustawienia.json`, gdy program działa | okno zapisuje cały plik przy każdej zmianie i nadpisze Twoją. Zakończ program (zasobnik → Zakończ Amadeusza), zmień, uruchom |
| czytanie `klucze.json` i wpisów `Amadeusz:*` w Menedżerze poświadczeń | klucze API i hasła; program sam je odczytuje |
| włączanie udostępniania, „Otwórz port w zaporze…”, `FirewallSetup` | zmienia zaporę i otwiera port w sieci — tylko na prośbę użytkownika; do prób `--udostepnij` |
| płatne zapytania do modeli w chmurze | tylko za zgodą użytkownika |
| skanowanie dysku `A:` (dane budowy) i zapis do folderu `exchange` mostu Tekli | dane budowy i skrypt na otwartym modelu Tekli |
| wysyłanie na cudzy komputer pytań o dane osobowe (godziny z CDE) | treść idzie po sieci biura otwartym HTTP |

## Czy Amadeusz działa

```powershell
Invoke-RestMethod http://127.0.0.1:11435/lokalneai/info
```

Odpowiedź (prawdziwa, z jednego komputera):

```json
{"program":"LokalneAI","komputer":"PC-ODS12","wersja":"0.1.0","port":11435,"udostepnia":true,
 "karta":"NVIDIA GeForce RTX 4060 (8 GB)","temperatura":37,"liczy":0,"czeka":0,"maks_kontekst":16384}
```

| Pole | Znaczenie |
|---|---|
| `komputer` | nazwa Windows tego PC |
| `udostepnia` | czy przyjmuje zapytania innych komputerów |
| `karta`, `temperatura` | karta graficzna i jej temperatura w °C (brak = nie da się odczytać) |
| `liczy`, `czeka` | ile zapytań teraz liczy, ile czeka w kolejce |
| `maks_kontekst` | największy `num_ctx`, jaki przyjmie od innych komputerów |

Brak odpowiedzi znaczy, że program nie działa, port jest inny (Ustawienia → Udostępnianie) albo bramę ma inne okno.
Sama Ollama: `Invoke-RestMethod http://127.0.0.1:11434/api/version`.

Uruchomienie: skrót „Amadeusz”, po instalacji `%LOCALAPPDATA%\Programs\LokalneAI\LokalneAI.exe`, w repozytorium
`Amadeusz.exe` albo `LokalneAI.App\bin\Debug\net10.0-windows\LokalneAI.exe`.

## Zapytania przez bramę (HTTP)

### Trasy

| Trasa | Rodzaj | Uwagi |
|---|---|---|
| `GET /lokalneai/info` | opis bramy | pola wyżej |
| `GET /api/tags`, `GET /api/ps`, `GET /api/version`, `POST /api/show`, `GET /v1/models` | odczyt | lista modeli komputera, który liczy (przy „Gdzie liczyć” — tamtego) |
| `POST /api/chat`, `POST /api/generate` | liczenie, API Ollamy | strumień NDJSON albo `"stream": false`; `options.num_ctx`, `keep_alive`, `think`, `format` działają |
| `POST /api/embed`, `POST /api/embeddings` | liczenie | wektory do wyszukiwania (RAG) |
| `POST /v1/chat/completions`, `/v1/completions`, `/v1/embeddings` | liczenie, API OpenAI | SSE przy `"stream": true`; klucz API dowolny, niepusty |
| każda inna | — | **404**: brama przepuszcza tylko liczenie i odczyt |

Wszystkie zapytania liczące stają w jednej kolejce. Zapytania z tego PC mają pierwszeństwo przed zapytaniami innych
komputerów. Brama nie zapisuje treści.

### Nagłówki

| Nagłówek | Kto ustawia | Po co |
|---|---|---|
| `X-LokalneAI-Program: <nazwa>` | Twój program (zalecane) | nazwa w kolejce i w zdarzeniach, np. `Tekla`, `Skrypt raportu`; polskie litery kodowane jak w adresie (`%C5%82`) |
| `X-LokalneAI-Komputer: <nazwa PC>` | brama przy „Gdzie liczyć”, zakładka Code przy podagentach | nazwa pytającego komputera w kolejce i licznikach tokenów innego PC (bez niego — adres IP) |
| `X-LokalneAI-Przekazane: 1` | brama przekazująca zapytanie | „licz tutaj, nie przekazuj dalej” — liczy karta tego PC mimo „Gdzie liczyć” |
| `Origin` | przeglądarka | **zawsze 403**: strona internetowa nie użyje modelu; brama jest dla programów, nie dla JavaScriptu w przeglądarce |
| `Authorization` | klient OpenAI | przechodzi dalej bez sprawdzania (brama nie ma haseł) |

### PowerShell (sprawdzone na żywo)

```powershell
$body = @{
    model = "qwen3.5:4b-q4_K_M"; stream = $false; think = $false; keep_alive = "5m"
    options = @{ num_ctx = 8192 }
    messages = @(@{ role = "user"; content = "Stolica Polski? Jedno słowo." })
} | ConvertTo-Json -Depth 5
$r = Invoke-RestMethod http://127.0.0.1:11435/api/chat -Method Post -ContentType "application/json; charset=utf-8" `
    -Headers @{ "X-LokalneAI-Program" = "PowerShell" } -Body ([Text.Encoding]::UTF8.GetBytes($body))
$r.message.content          # Warszawa
$r.prompt_eval_count        # tokeny przeczytane (cały prompt)
$r.eval_count               # tokeny napisane
```

`-Body` jako bajty UTF-8, bo Windows PowerShell 5.1 inaczej psuje polskie litery.

### curl i Python (bez dodatkowych paczek)

```powershell
curl.exe -s http://127.0.0.1:11435/api/chat -H "X-LokalneAI-Program: curl" -d '{\"model\":\"qwen3.5:4b-q4_K_M\",\"stream\":false,\"think\":false,\"messages\":[{\"role\":\"user\",\"content\":\"2+2?\"}]}'
```

```python
import json, urllib.request
req = urllib.request.Request("http://127.0.0.1:11435/api/chat",
    data=json.dumps({"model": "qwen3.5:4b-q4_K_M", "stream": False, "think": False,
                     "options": {"num_ctx": 8192},
                     "messages": [{"role": "user", "content": "Stolica Polski?"}]}).encode("utf-8"),
    headers={"Content-Type": "application/json", "X-LokalneAI-Program": "Python"})
print(json.load(urllib.request.urlopen(req))["message"]["content"])
```

### API zgodne z OpenAI

```python
from openai import OpenAI          # pip install openai
client = OpenAI(base_url="http://127.0.0.1:11435/v1", api_key="ollama",
                default_headers={"X-LokalneAI-Program": "Mój skrypt"})
r = client.chat.completions.create(model="qwen3.5:4b-q4_K_M", reasoning_effort="none",
                                   messages=[{"role": "user", "content": "Stolica Niemiec?"}])
print(r.choices[0].message.content, r.usage)
```

Dwie pułapki API OpenAI, obie sprawdzone na żywo:

- **Myślenie.** Qwen3.5 (i inne modele z myśleniem) bez `reasoning_effort: "none"` wydaje tokeny na „myślenie”.
  Przy małym `max_tokens` treść wraca **pusta**. W API Ollamy to samo robi `"think": false`.
- **Kontekst.** API OpenAI nie ma `num_ctx`, więc Ollama bierze swój domyślny kontekst (na tym PC 4096 tokenów,
  zmienna `OLLAMA_CONTEXT_LENGTH`). **Dłuższy prompt Ollama obcina po cichu.** Do długich promptów używaj
  `/api/chat` z `options.num_ctx`.

### Kody błędów bramy

Treść błędu to zawsze JSON `{"error":"…"}` po polsku.

| Kod | Kiedy |
|---|---|
| 400 | zapytanie nie jest obiektem JSON; inny komputer chce kontekstu większego niż `maks_kontekst` |
| 403 | nagłówek `Origin` (przeglądarka); komputer nie udostępnia; pytający spoza podsieci; model `…-cloud` (chmura Ollamy jest wyłączona) |
| 404 | trasa spoza listy (pobieranie, usuwanie, tworzenie modeli) |
| 413 | zapytanie większe niż 16 MB |
| 502 | brak połączenia z Ollamą albo z bramą innego komputera (treść mówi, co sprawdzić) |
| 503 | kolejka innych komputerów pełna (8 czekających); właściciel odrzucił albo przerwał zapytanie |

## Podpinanie innych narzędzi

| Narzędzie | Co ustawić |
|---|---|
| program z polem „adres Ollamy” / „Ollama base URL” (np. Open WebUI: `OLLAMA_BASE_URL`, Continue w VS Code: `apiBase` dostawcy `ollama`) | `http://127.0.0.1:11435` |
| biblioteka `ollama` w Pythonie | `ollama.Client(host="http://127.0.0.1:11435", headers={"X-LokalneAI-Program": "…"})` |
| LangChain | `ChatOllama(base_url="http://127.0.0.1:11435", model="…")` |
| klient OpenAI (SDK, aplikacje z polem „OpenAI-compatible base URL”) | `http://127.0.0.1:11435/v1`, klucz dowolny niepusty (np. `ollama`) |
| program .NET | `OllamaLlmClient` z `LocalGateway.ChooseUrl()` ([biblioteki-dla-agentow.md](biblioteki-dla-agentow.md)) |
| inny komputer w biurze | `http://NAZWA-PC:11435`, gdy tamten PC udostępnia (Ustawienia → Udostępnianie) i jest w tej samej podsieci |

Czego się nie da:

- **Pobieranie modelu z narzędzia** (przycisk „Pull” w Open WebUI itp.) dostaje 404. Model pobiera się w Ollamie na
  komputerze, który liczy (`ollama pull <tag>` albo instalator Amadeusza).
- **Aplikacja w przeglądarce, która woła model z JavaScriptu**, dostaje 403. Działają tylko programy i serwery, które wołają
  bramę po swojej stronie.
- **Klienci API Anthropic** (Claude Code, `/v1/messages`): brama nie ma tej trasy (404). Nowa trasa to zmiana
  w `GatewayServer.Routes` (zasada 9 w [AGENTS.md](../AGENTS.md)).
- **Wywołania narzędzi przez API OpenAI** przechodzą do Ollamy bez zmian. Czy działają, zależy od modelu, nie od bramy.

Nazwy modeli bierz dokładnie z `GET /api/tags` (np. `qwen3.5:9b-q4_K_M`,
`SpeakLeash/bielik-11b-v3.0-instruct:Q4_K_M`). Gdy „Gdzie liczyć” wskazuje inny komputer, lista i liczenie są tamtego
komputera, a nazwa w zapytaniu musi być z jego listy.

## Kontrola bez klikania (`--autotest`)

Uruchomienie kontrolne otwiera okno poza ekranem, bez fokusu, robi zadanie, zapisuje zrzut i wynik, po czym się zamyka.
Nie zapisuje ustawień, historii, kluczy ani liczników tokenów użytkownika. Działa obok otwartego okna, z własną bramą
na wolnym porcie. Tak agent może **zadać polecenie umiejętności** (zakładka Code) i przeczytać wynik.

```powershell
$exe = "$env:LOCALAPPDATA\Programs\LokalneAI\LokalneAI.exe"     # albo LokalneAI.App\bin\Debug\net10.0-windows\LokalneAI.exe
& $exe --autotest "jaki Wy ma IPE 200?" --harness tablice --model qwen3.5:9b-q4_K_M --wynik C:\Temp\amadeusz | Out-Null
Get-Content C:\Temp\amadeusz\harness.json -Encoding UTF8
```

`| Out-Null` w PowerShellu czeka na koniec programu okienkowego. Kod wyjścia jest w `$LASTEXITCODE`.

| Przełącznik | Znaczenie |
|---|---|
| `--autotest "<pytanie>"` | pytanie do Czatu; kilka pytań rozdziela `\|\|`; `-` = sam zrzut okna po starcie |
| `--wynik <folder>` | gdzie zapisać wyniki (domyślnie `%TEMP%\LokalneAI-autotest`) |
| `--harness <umiejętność>[+<umiejętność>…]` | zamiast Czatu: zakładka Code z tymi umiejętnościami, np. `tekla+pliki`; polecenia z `--autotest` po kolei (rozdziela `\|\|`) |
| `--model <tag>` | model (nazwa z `/api/tags`) |
| `--url <adres>` | inny adres Ollamy |
| `--foldery "C:\a;C:\b"` | dozwolone foldery umiejętności `pliki`, `komputer`, `office`, `ocr`, `rfem`; bez tego pusty `%TEMP%\LokalneAI_kontrola` (kontrola sama zatwierdza zwykłe zmiany! „zawsze pytaj” strażnika — instalacje, rejestr, zamykanie programów — odrzuca) |
| `--rozmowy <folder>` | zapisuj rozmowy Code jak w oknie, w `<folder>\.amadeusz\` |
| `--polaczenie <plik.json>`, `--ja <person_id>` | baza dla umiejętności `cde` (tylko odczyt) |
| `--zakladka czat\|code\|whiteboard\|ustawienia\|kolejka` | zakładka na zrzucie; `kolejka` = Ustawienia z rozwiniętą kolejką i licznikami tokenów; `--rozwin` = `ustawienia`; `whiteboard` = kontrola tablicy (niżej) |
| `--cel <komputer>` | licz na innym komputerze (tylko to uruchomienie) |
| `--udostepnij` | brama przyjmuje „inne komputery”, ale tylko z `127.0.0.1` (próba bez otwierania portu w sieci) |
| `--port <n>` | port bramy kontroli |
| `--ustawienia <plik>`, `--klucze <plik>` | ustawienia i klucze API z innych plików |

Polecenia specjalne w `--autotest` przy `--harness` (wpisywane jak polecenia, rozdzielane `||`):

| Polecenie | Co robi |
|---|---|
| `#tokeny` | włącza „Pokaż tokeny i czasy” (liczby w polu `metryki` wpisów) |
| `#profil:<Nazwa>` | wybiera profil (zestaw umiejętności) z listy, np. `#profil:Biuro` |
| `#wznow` | otwiera najnowszą rozmowę z `--rozmowy` i pisze dalej |
| `#zgoda` | nie zatwierdza sam: robi zrzut pytania o zgodę (`zgoda.png`) i klika „Tak” |
| `#lista` | zrzut listy umiejętności (`umiejetnosci.png`) |
| `#zrodla` | zrzut okna „Źródła wiedzy” (`zrodla.png`) |

Przy Czacie (bez `--harness`) `#ikona` zapisuje podgląd ikony w zasobniku (`ikona.png`).

Przy `--zakladka whiteboard` (tablica) polecenia z `--autotest` idą po kolei przez „Zatwierdź”, a specjalne to:

| Polecenie | Co robi |
|---|---|
| `#demo` | kładzie przykładowy szkic użytkownika: belka z podporami, strzałka z siłą, odręczna pętla piórem, dwa teksty |
| `#obraz:<plik.png>` | szkic z `#demo` i obraz z pliku obok (jak wklejony; OCR w tle) |
| `#capture` | sprawdza Capture: ramka 360×240 w lewym górnym rogu ekranu na chwilę, zrzut jej wnętrza na tablicę |

```powershell
& $exe --autotest "#demo||zaznacz, gdzie jest największy moment" --zakladka whiteboard --model gemma4:12b --wynik C:\Temp\tablica | Out-Null
```

| Plik wyniku | Zawartość |
|---|---|
| `okno.png`, `lista.png` | zrzut okna i rozwiniętej listy modeli (Czat) |
| `wynik.json` | Czat: `pytanie`, `odpowiedz`, `statystyki`, `blad`, `tokenyNaSekunde`, `udzialGpu`, `ollama`, `karta`, `liczy`, `model`, `modele`, `kolejka`, `brama`, `gdzie`, `baner`, `status` |
| `harness.png` | zrzut zakładki Code |
| `harness.json` | Code: `profil`, `liczy`, `kontekst`, `rozmowa`, `wpisy[]` z polami `rodzaj` (User, Call, Answer, Question, Warning, Error, Info, Confirm, Step, Agent), `tekst`, `szczegoly`, `ok`, `metryki` |
| `whiteboard.png`, `tablica.png` | zrzut zakładki Whiteboard i sama tablica (obie warstwy), jak dla modelu |
| `whiteboard.json` | tablica: `opis` (to, co model dostaje z poleceniem), `rysunkiAmadeusza`, `wpisy[]` jak w `harness.json` |
| `wyjatek.txt` | gdy program się wysypał |

Kody wyjścia: **0** — odpowiedź, **1** — błąd w odpowiedzi (Czat) albo wpis `Error` (Code), **2** — wyjątek kontroli
(opis w `wyjatek.txt`), **3** — nieoczekiwany błąd okna (opis w `%LOCALAPPDATA%\LokalneAI\bledy.log`).

## Pliki i ustawienia

| Plik | Zawartość | Agent |
|---|---|---|
| `%APPDATA%\LokalneAI\ustawienia.json` | ustawienia okna (JSON, nazwy pól camelCase) | czytaj; zmieniaj tylko przy wyłączonym programie |
| `%APPDATA%\LokalneAI\tokeny.json` | liczniki tokenów bramy (Ustawienia → Kolejka) | czytaj |
| `%APPDATA%\LokalneAI\klucze.json` | klucze API (zaszyfrowane kontem Windows) | nie ruszaj |
| `<folder grupy>\.amadeusz\*.json` | rozmowy zakładki Code (grupa „Niezgrupowane”: `Dokumenty\Amadeusz`) | czytaj |
| `Dokumenty\Amadeusz\Czat\` | rozmowy Czatu | czytaj |
| `%LOCALAPPDATA%\LokalneAI\historia.jsonl` | pytania i odpowiedzi Czatu ze statystykami, JSON w linii | czytaj |
| `%LOCALAPPDATA%\LokalneAI\bledy.log` | nieoczekiwane błędy programu | czytaj przy kłopotach |
| `%LOCALAPPDATA%\LokalneAI\bezpieczenstwo.log` | polecenia modelu zablokowane przez strażnika (JSON w linii) — [bezpieczenstwo.md](bezpieczenstwo.md) | czytaj |
| `<folder programu>\profile\<umiejętność>\` | umiejętności | dodawaj obok, nie kasuj |

Najczęściej potrzebne pola `ustawienia.json`:

| Pole | Znaczenie |
|---|---|
| `ollamaUrl` | adres Ollamy (domyślnie `http://127.0.0.1:11434`) |
| `model`, `modelProvider` | wybrany model; `modelProvider` pusty = Ollama, inaczej `anthropic` / `openai` / `google` |
| `contextLength` | kontekst czatu |
| `codeContextLength` | kontekst zakładki Code (lista „Kontekst” nad rozmową): 8192, 16384, 32768 albo 0 = jak w Czacie, ale co najmniej 16384 |
| `computeOn`, `computeOnName` | „Gdzie liczyć”: adres bramy innego komputera; `null` = ten komputer |
| `shareOnLan`, `gatewayPort`, `maxContextForOthers` | udostępnianie, port bramy, limit kontekstu dla innych |
| `agentMachines`, `agentsUseThisComputer` | komputery dla podagentów |
| `favoriteModels`, `cloudModels` | ulubione modele, modele w chmurze na liście |
| `harnessProfiles`, `workProfiles` | zaznaczone umiejętności zakładki Code, profile (zestawy umiejętności) |
| `showTokens`, `showSteps`, `internetAccess` | przełączniki z okna |

## Umiejętności: dodawanie i sprawdzanie

Umiejętność to folder `profile\<nazwa>\` z `profil.json`, `system.md` i `narzedzia.json`. Format:
[profile.md](profile.md), jak pisać wiedzę: [wiedza-dla-modeli.md](wiedza-dla-modeli.md), szablon:
[szablony/profil](../szablony/profil). Nowa umiejętność pojawia się na liście po ponownym uruchomieniu programu.
Wiedza z repozytoriów: Ustawienia → Umiejętności → Źródła wiedzy… (`zrodla.json`).

Sprawdzanie bez okna (`LokalneAI.Eval`, w repozytorium `dotnet build`, potem `LokalneAI.Eval\bin\Debug\net10.0\LokalneAI.Eval.exe`):

| Polecenie | Co robi |
|---|---|
| `szukaj --profil profile\<nazwa> --fraza "…"` | co dostałby model z `szukaj_wiedzy` (bez modelu) |
| `rozmiar --profil profile\<nazwa> --model <tag>` | ile tokenów zajmuje polecenie systemowe |
| `test --profil profile\<nazwa> --model <tag> --ctx 16384` | zestaw `testy.jsonl` z prawdziwym modelem (Ollama `127.0.0.1:11434` albo `--url`) |
| `wiedza --profil profile\<nazwa>` | aktualizacja wiedzy z `zrodla.json` |

## Gdy coś nie działa

| Objaw | Co sprawdzić |
|---|---|
| `/lokalneai/info` nie odpowiada | czy program działa (ikona w zasobniku); port w Ustawieniach → Udostępnianie; `Get-NetTCPConnection -LocalPort 11435 -State Listen` |
| odpowiada Ollama (11434), a brama nie | brama nie wystartowała (port zajęty — opis w Ustawieniach → Udostępnianie) albo działa w innym oknie |
| 404 przy `/api/pull` | celowo; pobieraj w Ollamie na komputerze, który liczy |
| 403 „ze stron internetowych” | klient wysyła `Origin` (przeglądarka) — wołaj z programu albo serwera |
| pusta odpowiedź przez `/v1/chat/completions` | myślenie modelu: `reasoning_effort: "none"` albo większe `max_tokens` |
| model „zapomina” początek długiego promptu | Ollama obcięła prompt dłuższy niż kontekst; `/api/chat` z `options.num_ctx` |
| zapytanie długo czeka | Ustawienia → Kolejka: liczy coś innego; zapytania tego PC mają pierwszeństwo |
| błędy z innym komputerem | tabela w [siec.md](siec.md#gdy-coś-nie-działa) |
