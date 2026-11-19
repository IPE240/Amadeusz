# Umiejętność `amadeusz`: obsługa samego Amadeusza

Model szuka w `wiedza\` przez `szukaj_wiedzy` i odpowiada na pytania o Amadeusza — obsługę okna, porty i zaporę, bramę HTTP
(API Ollamy i OpenAI), podpinanie innych programów i skryptów, liczenie na innym komputerze, kontrolę bez klikania
(`--autotest`), pliki ustawień, umiejętności i instalację. Od 2026-11-19 ma też polecenia (wykonawca `SelfExecutor`
w `LokalneAI.App\HarnessView.Amadeusz.cs`):

| Polecenie | Co robi |
|---|---|
| `stan_amadeusza` | model i gdzie liczy, brama, internet, zaznaczone umiejętności i profil, uprawnienia, foldery, komputery podagentów, modele |
| `ustawienia_amadeusza` | pola `ustawienia.json` bez danych logowania (`SelfView.SettingsForModel`: bez `cde…`, `person…`, haseł i kluczy) |
| `umiejetnosci_amadeusza` | lista umiejętności i profili; z nazwą — opis, polecenia, zasady jednej umiejętności |
| `dziennik_amadeusza` | końcówka `bledy.log` albo `bezpieczenstwo.log` |
| `wybierz_umiejetnosci` | przełącza umiejętności (`ustaw`, `dodaj`, `usun`) albo profil; zmiana — pyta o zgodę; działa po turze, od następnego polecenia; tylko rozmowa zakładki Code |

Ustawień, plików umiejętności ani programu umiejętność nie zmienia (strażnik, [docs/bezpieczenstwo.md](../../docs/bezpieczenstwo.md)).
`"chmura": ["anthropic"]`: tylko modele lokalne i Claude — dane Amadeusza nie idą do Gemini ani GPT (decyzja użytkownika z 2026-11-19).

Przykłady: „na czym teraz liczę?”, „jaki port bramy mam ustawiony?”, „przełącz na teklę”, „pokaż ostatnie błędy”, „na jakim
porcie działa brama?”, „jak podpiąć skrypt w Pythonie?”, „jaki base_url dla biblioteki openai?”, „czemu `/api/pull` daje 404?”.

## Pliki

| Plik | Co to |
|---|---|
| `profil.json` | `zakonczenie: model`, `szukaj_wiedzy`, bez internetu, `"chmura": ["anthropic"]` (modele lokalne i Claude) |
| `system.md` | rola, narzędzia i 9 zasad: wiedza albo stan, przełączanie, niczego nie zmienia, nazwy z okna z drogą, gotowe polecenia, brama zamiast Ollamy, ostrzeżenia, przyczyna błędu, bez obchodzenia zabezpieczeń |
| `karta.md` | ściąga: części i adresy, porty, najważniejsze miejsca w oknie, pojęcia, zakazy |
| `narzedzia.json` | 5 poleceń: cztery do odczytu, `wybierz_umiejetnosci` zmienia |
| `przyklady.jsonl` | przykłady (porty, zapora, Python, OpenAI, Open WebUI, autotest, ustawienia, tokeny, stan, przełączanie, dzienniki, dopytanie) |
| `testy.jsonl` | 10 testów dla `LokalneAI.Eval test` |
| `wiedza\*.md` | 10 plików w krótkich sekcjach; opis w `wiedza\CZYTAJ.md` |

## Skąd wiedza i jak ją utrzymać

Źródło prawdy to dokumentacja repozytorium: [docs/sterowanie.md](../../docs/sterowanie.md),
[docs/porty.md](../../docs/porty.md), [docs/siec.md](../../docs/siec.md), [README.md](../../README.md),
[docs/profile.md](../../docs/profile.md). Wiedza umiejętności to te same fakty przepisane na sekcje do ok. 900 znaków
(tyle `szukaj_wiedzy` oddaje modelowi), z nagłówkami ze słów, których ludzie używają w pytaniach.

Zmiana portu, przełącznika `LokalneAI.exe`, trasy bramy albo nazwy w oknie wymaga zmiany w `docs\` i w `wiedza\`.
Test `AmadeuszWiedzaTests` sprawdza, że każdy przełącznik z `StartupOptions` i każda trasa z `GatewayServer.Routes`
są opisane w `docs\sterowanie.md` i w `wiedza\`, a porty z `GatewayOptions` — w `docs\porty.md` i w `wiedza\porty.md`.

Brak `zrodla.json` jest celowy: repozytorium jest prywatne, a długie sekcje dokumentacji model dostawałby ucięte do
900 znaków.

Sprawdzenie wyszukiwania bez modelu:

```powershell
.\LokalneAI.Eval\bin\Debug\net10.0\LokalneAI.Eval.exe szukaj --profil profile\amadeusz --fraza "OpenAI base_url"
```
