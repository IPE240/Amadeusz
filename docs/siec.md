# Kolejka i liczenie na innym komputerze (brama LokalneAI)

Okno LokalneAI ma w sobie **bramę**, czyli mały serwer HTTP przed Ollamą. Brama mówi tym samym językiem co Ollama,
więc programy nie muszą nic o niej wiedzieć. Robi trzy rzeczy:

1. **Kolejka.** Zapytania z czatu, zakładki Code, innych programów na tym PC i innych komputerów czekają w jednej
   kolejce przed Ollamą tego komputera. W Ustawieniach widać, kto liczy, kto czeka i od ilu sekund.
2. **Liczenie na innym komputerze.** W *Ustawienia → Gdzie liczyć* wybierasz ten komputer albo inny komputer w biurze
   (nazwa, np. `PC-ODS12`, albo IP). Rozmowa zostaje na Twoim PC. Brama za każdym razem wysyła tamtemu komputerowi
   całe zapytanie razem z kontekstem, a on tylko liczy odpowiedź i niczego nie zapisuje.
3. **Udostępnianie.** Przełącznik *Pozwalaj na przeliczanie zapytań z innych komputerów* otwiera bramę dla komputerów
   z tej samej podsieci. Wyłączony oznacza, że port słucha tylko na `127.0.0.1` i z sieci nie da się go nawet zobaczyć.

```
 laptop (słaba karta)                         PC-ODS12 (mocna karta)
 ┌──────────────────────────────┐            ┌──────────────────────────────┐
 │ Czat / Code / Tekla / CDE    │            │ okno LokalneAI               │
 │      │ (API Ollamy)          │            │  udostępnia: TAK             │
 │      ▼                       │   HTTP,    │                              │
 │ brama :11435 (127.0.0.1)     │  podsieć   │  brama :11435                │
 │  „Gdzie liczyć: PC-ODS12”  ──┼───────────►│   kolejka ──► Ollama :11434  │
 │  (rozmowa zostaje tutaj)     │            │   (127.0.0.1)                │
 └──────────────────────────────┘            └──────────────────────────────┘
```

## Jak tego użyć

**U siebie (liczy inny komputer):** *Ustawienia → Gdzie liczyć → Na innym komputerze w sieci*. Wpisz nazwę albo IP
i kliknij **Połącz**, albo kliknij **Szukaj w sieci** i potem **Użyj**. Po połączeniu lista modeli w nagłówku
pokazuje modele tamtego komputera.

**Na mocnym komputerze (udostępnia):**

1. Zaznacz *Pozwalaj na przeliczanie zapytań z innych komputerów*.
2. Kliknij **Otwórz port w zaporze…** i zatwierdź okno administratora (UAC). Wystarczy raz.
3. Okno LokalneAI musi być uruchomione, wystarczy zminimalizowane. Zamknięcie okna zamyka bramę.

Port to **11435** (TCP), a wyszukiwanie używa **11436** (UDP). Port jest ten sam na wszystkich komputerach.
Zmienia się go w Ustawieniach tylko wtedy, gdy 11435 jest zajęty, i wtedy przy łączeniu trzeba podać `IP:port`.
Wszystkie porty, reguły zapory i połączenia wychodzące: [porty.md](porty.md).

## Komputery dla podagentów

Zakładka Code może dzielić pracę na **podagentów**: osobne sesje z własnym modelem, które liczą równolegle, a wynik oddają
agentowi głównemu (narzędzie `zlec_podagentowi`; podagent nie widzi rozmowy — dostaje tylko zadanie). Komputery dla nich
ustawia się w *Ustawienia → Gdzie liczyć → Komputery dla podagentów*: przełącznik **Ten komputer** (karta tego PC,
niezależnie od tego, gdzie liczy rozmowa główna) i lista innych komputerów jak w Menedżerze poświadczeń („+” dodaje wiersz,
× usuwa, **Sprawdź** pyta bramę tamtego komputera o kartę, limit kontekstu i modele; adres zapisuje się po wyjściu z pola).
Tamte komputery muszą udostępniać model (ich Ustawienia → Udostępnianie), tak samo jak przy „Gdzie liczyć”.

Na każde zlecenie program sam wybiera komputer (najmniej obciążony: mniej własnych podagentów, krótsza kolejka bramy,
większa karta) i model do jego karty: najpierw model główny rozmowy i preferowane modele profilu (jeśli są na tamtym
komputerze i mieszczą się z zapasem), potem model już załadowany do pamięci, potem największy, który mieści się w karcie
z kontekstem (`ModelChooser`). „Ten komputer” liczy przez bramę okna z nagłówkiem `X-LokalneAI-Przekazane: 1`, więc jego
własna karta, choćby „Gdzie liczyć” wskazywało inny komputer. Każdy podagent widać w trakcie na kafelku nad polem polecenia
(z przyciskiem **Przerwij**), a po skończeniu jako rozwijany wpis w rozmowie; Stop przerywa wszystkich.

## Kolejność w kolejce

- Ollama liczy jedno zapytanie naraz (`OLLAMA_NUM_PARALLEL` = 1). Kolejne czekają w bramie, gdzie je widać,
  a nie w Ollamie, gdzie byłyby niewidoczne.
- **Zapytania z tego komputera mają pierwszeństwo**, bo właściciel karty nie powinien czekać na cudze zapytania.
  Zapytanie innego komputera, które czeka już ponad 60 s, wraca do zwykłej kolejności przyjścia, więc nie utknie na zawsze.
- Z innych komputerów może czekać najwyżej 8 zapytań naraz. Dziewiąte dostaje „kolejka pełna”.
- Właściciel może **Odrzucić** zapytanie innego komputera. Zapytań z własnego PC się nie odrzuca, bo wystarczy Stop.

Czat pokazuje pod pytaniem „Czeka w kolejce — przed nim: …”, gdy liczy coś innego.

## Kto ile policzył (tokeny)

Pod kolejką są **liczniki tokenów policzonych na tym komputerze**: wiersz na każdy komputer, który pytał (i „ten komputer”),
z liczbą zapytań, tokenami przeczytanymi przez model (prompt) i napisanymi, sumą i czasem ostatniego zapytania; przy kilku
wierszach także „Razem”. Liczby są dokładne: brama i tak przepuszcza odpowiedź Ollamy, więc czyta z jej końca
`prompt_eval_count` i `eval_count` (API OpenAI: `usage.prompt_tokens` i `completion_tokens`). Prompt liczy się cały, także
część z pamięci podręcznej Ollamy. Zapytanie przekazane dalej („Gdzie liczyć”) liczy komputer, który je policzył, nie ten,
który je wysłał. Sumy zapisują się w `%APPDATA%\LokalneAI\tokeny.json` (przetrwają zamknięcie okna); **Resetuj** zeruje je
i zaczyna liczyć od teraz.

Komputer przedstawia się nazwą Windows w nagłówku `X-LokalneAI-Komputer` — ustawia go brama przy „Gdzie liczyć” i zakładka
Code przy podagentach na innych komputerach. Bez nagłówka (inny program, stara wersja) w kolejce i licznikach stoi adres IP.
Nazwa to podpis, nie uwierzytelnienie — dlatego obok jest zawsze adres.

## Bezpieczeństwo: co jest, czego nie ma

| Ochrona | Jak |
|---|---|
| tylko sieć biura | brama przyjmuje adresy z podsieci kart sieciowych tego PC, np. `10.0.0.0/24`; reguła zapory ma `RemoteAddress LocalSubnet` i działa tylko w sieciach prywatnych i domenowych |
| tylko liczenie | przechodzą `POST /api/chat`, `/api/generate`, `/api/embed`, `/v1/chat/completions` (i embeddingi) oraz odczyt `GET /api/tags`, `/api/ps`, `/api/version`, `POST /api/show`; **pobieranie, usuwanie, kopiowanie i tworzenie modeli są zablokowane** (404) |
| Ollama nie wychodzi do sieci | Ollama nadal słucha tylko na `127.0.0.1:11434`; z sieci widać jedynie bramę |
| strony internetowe | zapytanie z nagłówkiem `Origin` (każda przeglądarka go wysyła) dostaje 403, więc strona otwarta w przeglądarce nie użyje modelu |
| pamięć karty właściciela | kontekst z innych komputerów jest ograniczony (domyślnie 16 384); za długi dostaje czytelny błąd, nie ciche obcięcie; `keep_alive` innych zastępuje wartość właściciela (5 min) |
| bez pętli | zapytanie przekazane przez inną bramę (`X-LokalneAI-Przekazane: 1`) nie idzie dalej |
| treści | brama nie zapisuje treści zapytań; w kolejce i w zdarzeniach jest tylko komputer, program, model i czas |

**Czego nie ma (świadomie, w pierwszej wersji):**

- **Hasła.** Każdy komputer z podsieci może liczyć na udostępniającym PC. Przy kilkunastu osobach w jednym biurze
  wystarczy przełącznik i podgląd kolejki. Gdy będzie potrzebny, dodamy „kod biura” w nagłówku `Authorization`
  (brama już go przekazuje).
- **Szyfrowania.** Treść idzie po sieci biura otwartym HTTP, jak pliki na dysku sieciowym. Pytań o dane, których
  właściciel tamtego PC nie powinien widzieć (np. godziny z CDE), nie wysyłaj na cudzy komputer.
- **Nazwa komputera jest tylko opisem.** Komputer sam podaje swoją nazwę (`X-LokalneAI-Komputer`) i nie da się jej
  sprawdzić. O dostępie decyduje adres IP z podsieci.

## Programy z biblioteką

```csharp
var llm = new OllamaLlmClient(new OllamaOptions
{
    Url = LocalGateway.ChooseUrl(),   // brama okna LokalneAI, gdy działa; inaczej Ollama na tym PC
    ClientName = "Tekla",             // nazwa w kolejce
    Model = "SpeakLeash/bielik-11b-v3.0-instruct:Q4_K_M",
    ContextLength = 16384,
});
```

Program, który łączy się przez bramę, liczy tam, gdzie użytkownik wybrał w oknie, i czeka w tej samej kolejce
co czat. Gdy okno nie działa, `ChooseUrl()` zwraca `http://127.0.0.1:11434` i program łączy się wprost z Ollamą,
tak jak dotąd. Inne języki (Python i inne) wysyłają zwykłe zapytania API Ollamy na `http://127.0.0.1:11435`
albo API OpenAI na `http://127.0.0.1:11435/v1`, z nagłówkiem `X-LokalneAI-Program`. Trasy, nagłówki, kody błędów,
sprawdzone przykłady (PowerShell, curl, Python, OpenAI SDK) i ustawienia gotowych narzędzi (Open WebUI, Continue,
LangChain): [sterowanie.md](sterowanie.md).

Opis bramy: `GET http://<komputer>:11435/lokalneai/info` zwraca nazwę, kartę, długość kolejki i limit kontekstu.

## Dlaczego tak, a nie inaczej

- **Dlaczego nie samo `OLLAMA_HOST=0.0.0.0`** ([serwer-modelu.md](serwer-modelu.md)). Ollama nie ma haseł ani
  ograniczeń: każdy w sieci może pobierać i usuwać modele, a błędy w samej Ollamie (np. CVE-2026-7482, wyciek pamięci
  bez logowania) stają się widoczne dla całej sieci. Do tego nie da się tego wyłączyć jednym przełącznikiem bez
  restartu Ollamy, a właściciel nie widzi, kto liczy, i nie ma pierwszeństwa. Brama zostawia Ollamę na `127.0.0.1`.
- **Dlaczego nie SSH.** Wymaga instalacji serwera OpenSSH (administrator) oraz kont i kluczy na każdym PC. Daje
  pełną powłokę zamiast samego liczenia. Kolejki i pierwszeństwa też nie rozwiązuje, bo tylko tuneluje port Ollamy.
- **Dlaczego jeden program, a nie osobny serwer i klient.** Serwerem i tak jest Ollama, osobny proces na każdym PC.
  Brama to cienka warstwa przed nią, a okno i tak działa u tych, którzy mają mocne karty. Gdy w biurze stanie
  komputer tylko do liczenia, wystarczy na nim uruchomić okno z zaznaczonym udostępnianiem; tryb bez okna
  (`--serwer`, autostart) to następny krok.
- **Dlaczego Kestrel (ASP.NET Core).** Nie wymaga praw administratora ani rezerwacji adresu (`netsh http urlacl`),
  jak `HttpListener`, a strumieniowanie odpowiedzi działa od razu. Administrator jest potrzebny tylko raz, do zapory.

Podobnie działają inne programy: LM Studio ma „Serve on Local Network” (otwarty serwer w sieci, opcjonalnie z tokenem)
i LM Link (połączenie przez VPN Tailscale, modele innego komputera widoczne jak lokalne). Szczegóły
w [badania/funkcje-aplikacji.md](badania/funkcje-aplikacji.md).

## Kontrola bez klikania

```powershell
# czat liczony na „innym komputerze” (tu: druga brama na tym samym PC)
LokalneAI.exe --autotest "Stolica Polski?" --cel 127.0.0.1:11445 --zakladka ustawienia --wynik C:\wynik
# zrzut Ustawień z włączonym udostępnianiem (port tylko na 127.0.0.1, bez okna zapory)
LokalneAI.exe --autotest - --udostepnij --zakladka ustawienia --wynik C:\wynik
```

W kontroli brama bierze dowolny wolny port (albo `--port`), więc nie zajmuje portu otwartego okna. `--udostepnij`
traktuje `127.0.0.1` jak inny komputer i nie otwiera portu w sieci. Testy bramy (`GatewayTests`) robią to samo na
dwóch bramach w jednym procesie.

## Gdy coś nie działa

| Objaw | Co sprawdzić |
|---|---|
| „Nie ma połączenia z PC-…” | czy tam działa okno LokalneAI z zaznaczonym udostępnianiem; czy na tamtym PC kliknięto „Otwórz port w zaporze”; czy sieć jest prywatna, a nie publiczna (Ustawienia Windows → Sieć) |
| „Szukaj w sieci” nic nie znajduje, a adres działa | brak reguły UDP 11436 albo komputery są w różnych podsieciach; wpisz adres ręcznie |
| „… przyjmuje tylko z podsieci …” | pytający komputer jest w innej podsieci (np. VPN); brama go nie wpuści |
| „… nie udostępnia teraz modelu …” | właściciel wyłączył przełącznik |
| „Na komputerze … nie ma modelu …” | wybierz z listy model, który tamten komputer ma; pobiera go właściciel u siebie |
| „… przyjmuje kontekst do 16384 tokenów …” | zmniejsz kontekst albo poproś właściciela o większy limit; zakładka Code sama schodzi do limitu komputera, który liczy (`MainViewModel.HarnessContextLength`, limit z sondy bramy) |
| „Brama nie wystartowała na porcie 11435” | port zajęty przez inny program; zmień port w Ustawieniach (na wszystkich komputerach) |
