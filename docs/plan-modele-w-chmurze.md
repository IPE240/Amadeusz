# Plan: modele w chmurze (Claude, GPT, Gemini) — do testów

Stan (2026-11-07): fazy 0–3 zbudowane i sprawdzone testami bez sieci; czat z modelem w chmurze działa bez strumienia
(cała odpowiedź naraz). Brakuje: pierwszej prawdziwej próby z kluczem użytkownika i strumienia w czacie (faza 4).

Co powstało:

- `CloudProvider`, `CloudLlm`, `CloudModels`, `AnthropicLlmClient` i tryb chmury w `OpenAiCompatibleLlmClient`
  (`LokalneAI.Harness`); `HarnessSession` odmawia modelu, na który profil nie pozwala, serwera w internecie spoza
  listy i modeli „-cloud” z Ollamy; brama też ich nie przepuszcza.
- Pole `chmura` w profilach: `cde` — `["anthropic"]`, pozostałe — trzech dostawców.
- Okno: Ustawienia → „Modele w chmurze (test)” (klucze w `CloudKeyStore`, DPAPI), kolorowa lista modeli z gwiazdką
  ulubionych, pasek w zakładce Code (dokąd idą dane albo czerwone ostrzeżenie), kolor w pasku stanu.
- Lista modeli dostawcy jest pobierana z jego API, więc nowe modele pojawiają się same.

## Po co

LokalneAI ma działać lokalnie. Ma też sprawdzić, czy AI da radę obsługiwać nasze programy przez harness
(zakładka Code). Lokalne modele są na to jeszcze za słabe, dlatego do testów dopuszczamy modele w chmurze.

## Zasady

| Zasada | Jak w programie |
|---|---|
| Dostawcy w chmurze: tylko **Anthropic (Claude), OpenAI (GPT), Google (Gemini)** | zamknięta lista w kodzie; innych adresów w chmurze nie da się wpisać |
| **DeepSeek nigdzie w chmurze**; lokalnie wolno (Ollama), gdy będzie sprzęt | nie ma go na liście dostawców; modele `deepseek-*` w Ollamie działają jak każdy model lokalny |
| **Profil CDE: tylko modele lokalne i Claude** | `profil.json` ma pole `chmura` z dozwolonymi dostawcami; CDE: `["anthropic"]` |
| Profil bez pola `chmura`: tylko modele lokalne | bezpieczny domyślny stan dla nowych profili |
| Kolory na liście modeli: **Claude pomarańczowy, GPT turkusowy, Gemini niebieski**; modele lokalne bez koloru | kolorowa kropka i nazwa na liście, ten sam kolor w pasku stanu („liczy: chmura Anthropic”) |
| Model w chmurze dostaje tę samą dokumentację trybu co model lokalny | model w chmurze zastępuje tylko silnik (`ILlmClient`); profil (system.md, karta.md, przykłady, `wiedza\`, narzędzia) i tryby uprawnień zostają bez zmian |
| Klucze API zostają na komputerze | zaszyfrowane kontem Windows (DPAPI) w `%APPDATA%\LokalneAI`; nigdy w repo, historii, wynikach autotestu ani w bramie LAN |
| Brama LAN nie przekazuje zapytań do chmury | zapytanie do chmury idzie prosto z tego komputera, bez kolejki; inne komputery nie widzą moich modeli w chmurze |

## Kolejność: od najprostszego

| Faza | Co | Dlaczego w tej kolejności |
|---|---|---|
| 0 | części wspólne: dostawcy, klucze, lista z kolorami, reguła profili | potrzebne każdemu modelowi |
| 1 | **GPT (OpenAI) w zakładce Code** | klient `OpenAiCompatibleLlmClient` już jest; wystarczą poprawki |
| 2 | Gemini w zakładce Code | ten sam klient, inny adres (Google ma API zgodne z OpenAI) |
| 3 | Claude w zakładce Code, odblokowanie CDE dla Claude | własne API Anthropic, nowy klient |
| 4 | zakładka Czat dla trzech dostawców | odpowiedź na bieżąco (strumień) ma u każdego inny format |
| 5 | dalej (opcjonalnie) | biblioteka dla innych programów, więcej dokumentacji od razu w kontekście, koszt w zł |

## Faza 0: części wspólne

1. `CloudProvider` (Harness): Anthropic, OpenAI, Google — nazwa, adres API, kolor, nazwa zmiennej środowiskowej klucza.
2. Klucze:
   - Ustawienia → nowa sekcja „Modele w chmurze (test)”: przełącznik „Pozwalaj na modele w chmurze” (domyślnie wyłączony),
     pole klucza dla każdego dostawcy (zakryte) i przycisk „Sprawdź”;
   - „Sprawdź” pobiera listę modeli (`GET /v1/models`); to nic nie kosztuje;
   - zapis: DPAPI (`ProtectedData`, zakres bieżący użytkownik), plik `%APPDATA%\LokalneAI\klucze.dat`;
   - zamiast pola można użyć zmiennej środowiskowej (`ANTHROPIC_API_KEY`, `OPENAI_API_KEY`, `GEMINI_API_KEY`).
3. Lista modeli:
   - `ModelItem` dostaje dostawcę (`null` = lokalny);
   - modele w chmurze pochodzą z listy dostawcy, a nie z kodu, więc nowe modele pojawią się same;
   - grupy na liście: „Ten komputer” albo „PC-…”, potem „Chmura (płatne, dane wychodzą z biura)”;
   - kolory na liście i w pasku stanu.
4. Reguła profili:
   - pole `chmura` w `profil.json`;
   - przy zmianie profilu albo modelu niedozwolony model jest wyszarzony;
   - przed startem sesji kod sprawdza zasadę jeszcze raz i odmawia z komunikatem
     („Profil CDE: tylko modele lokalne i Claude”). Sprawdzenie jest w kodzie, nie tylko w oknie.
5. Statystyki: tokeny wejścia i wyjścia z odpowiedzi API; zamiast „% w VRAM” pokazuje „chmura Anthropic” i tak dalej.
6. Testy (bez sieci):
   - reguła profili;
   - lista z kolorami;
   - klucze nie trafiają do historii ani do `wynik.json`;
   - DeepSeek nie przechodzi.

## Faza 1: GPT w zakładce Code

Poprawki w `OpenAiCompatibleLlmClient`:

1. **Schemat kroku.** Obecny klient wysyła `strict: true`. Tryb ścisły OpenAI nie przyjmuje schematu z `anyOf` na samej górze
   ani parametrów opcjonalnych, a nasz schemat kroku (`StepSchema`) i część narzędzi takie mają. Dla chmury: `strict: false`,
   a poprawność sprawdza harness. Niepoprawny krok już dziś wraca do modelu z prośbą o poprawkę.
2. **Temperatura.** Część modeli OpenAI przyjmuje tylko domyślną temperaturę. Potrzebna opcja „nie wysyłaj temperatury”,
   a po błędzie 400 o temperaturze ponowienie bez niej.
3. **Adres.** `https://api.openai.com`, klucz w nagłówku `Authorization: Bearer`. Klient już to umie.
4. Testy na podstawionym HTTP: treść zapytania, błędy 401, 429 i 400 z czytelnym komunikatem po polsku.
5. Prawdziwa próba: użytkownik wpisuje klucz w Ustawieniach i robi jedno zadanie w trybie Tekla albo pliki.
   Agent nie wysyła płatnych zapytań bez zgody.

## Faza 2: Gemini w zakładce Code

1. Google ma API zgodne z OpenAI pod innym adresem (`…/v1beta/openai/chat/completions`). Klient musi przyjąć pełną ścieżkę
   zamiast doklejać `/v1`.
2. Sprawdzić, jak Gemini przyjmuje `response_format` ze schematem. Jeśli go nie przyjmie: zwykły tryb JSON
   i sprawdzanie po stronie harnessu.
3. Testy jak w fazie 1.

## Faza 3: Claude w zakładce Code

1. Nowy `AnthropicLlmClient : ILlmClient` dla Messages API. Inaczej niż w OpenAI:
   - polecenie systemowe osobno;
   - nagłówki `x-api-key` i `anthropic-version`;
   - wymagane `max_tokens`;
   - odpowiedź w blokach treści.
2. JSON kroku: wymuszone narzędzie ze schematem albo ustrukturyzowana odpowiedź. Sprawdzić w aktualnej dokumentacji Anthropic.
   Schemat narzędzia musi być obiektem, więc `anyOf` kroku trzeba opakować.
3. Pamięć podręczna promptu na polecenie systemowe i dokumentację profilu. Harness wysyła je w każdym kroku, więc to obniża koszt.
4. Odblokowanie CDE dla Claude (`"chmura": ["anthropic"]` w profilu CDE). Rola bazy bez zmian: tylko odczyt.
5. Testy jak w fazie 1, potem prawdziwa próba z kluczem użytkownika.

## Faza 4: zakładka Czat

Odpowiedź na bieżąco (SSE) dla każdego z trzech API. Czat nie używa narzędzi ani CDE, więc wolno w nim wszystkich trzech dostawców.

## Faza 5: dalej (opcjonalnie)

- Biblioteka dla innych programów (np. `HarnessPanel` w TransmittalMaster): ten sam wybór silnika i ta sama reguła profili.
- Duży model w chmurze dostaje od razu więcej dokumentacji z `wiedza\`, a nie tylko przez `szukaj_wiedzy`.
- Szacowany koszt odpowiedzi w zł z cennika w ustawieniach.

## Otwarte decyzje

- Klucz firmowy wpisywany na każdym komputerze czy każdy swój.
- Czy dla modeli w chmurze zostawić tryb uprawnień „bez pytania”, czy wymagać trybu „pytaj”.
- Warunki dostawców: dane z API Anthropic domyślnie nie służą do trenowania; firma powinna to potwierdzić w aktualnych warunkach
  (dla CDE są tam dane osobowe: godziny pracy).
