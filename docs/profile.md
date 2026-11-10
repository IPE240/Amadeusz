# Format profilu

Profil to folder z wiedzą i poleceniami jednego zastosowania. Program wczytuje go przez `HarnessProfile.Load(folder)`.
Gotowe profile:
- [profile/tekla](../profile/tekla): polecenia w Tekli;
- [profile/cde](../profile/cde): pytania do bazy CDE;
- [profile/pliki](../profile/pliki): foldery, kopiowanie i przenoszenie, bez kasowania i bez PowerShella;
- [profile/komputer](../profile/komputer): PowerShell, programy, czytanie i wyszukiwanie plików, z pytaniem o każdą zmianę.

| Plik | Obowiązkowy | Co zawiera |
|---|---|---|
| `profil.json` | nie | ustawienia (niżej) |
| `system.md` | tak | rola modelu i zasady, do ok. 400 słów |
| `karta.md` | nie | wiedza wklejana do każdego polecenia: pojęcia, jednostki, pułapki. Krótko, bo zjada kontekst |
| `narzedzia.json` | tak | polecenia gospodarza z parametrami (JSON Schema) |
| `przyklady.jsonl` | nie | przykłady „polecenie → wywołania”, po jednym JSON w linii |
| `wiedza\*.md` | nie | dłuższe notatki; model sięga po nie narzędziem `szukaj_wiedzy` (także z podfolderów, `.md` i `.txt`) |
| `zrodla.json` | nie | repozytoria i adresy, z których dokumentacja aktualizuje się sama do `wiedza\zrodla\` |
| `testy.jsonl` | nie | zestaw testów dla `LokalneAI.Eval` |
| `szablony\*.cs` | nie | szablony skryptów (profil `tekla`: skrypty C# 5 dla mostu TeklaBridge) |

## Gdzie wkleić dokumentację

| Co | Gdzie | Uwagi |
|---|---|---|
| Zasady zachowania („nie zgaduj profilu”, „pytaj o id”) | `system.md` | krótko; trafia do każdego pytania |
| Najważniejsze pojęcia i jednostki | `karta.md` | krótko (każde słowo zajmuje kontekst w każdym pytaniu) |
| Dłuższa dokumentacja, instrukcje, opisy | `wiedza\*.md` | dowolnie dużo; model szuka w niej narzędziem `szukaj_wiedzy`, gdy czegoś nie wie |
| Dokumentacja z repozytorium | przycisk **Źródła wiedzy…** w oknie asystenta (zapisuje `zrodla.json`) | wklej adres repozytorium i wzorce plików; aktualizuje się samo (niżej) |

Gdzie trzymać wiedzę o programach biura (folder `lokalne-ai\` w ich repozytoriach) i jak pisać notatki, żeby model je
znalazł: [wiedza-dla-modeli.md](wiedza-dla-modeli.md). `CZYTAJ.md` w `wiedza\` to opis folderu dla ludzi; model go
nie widzi. Nowy profil zaczyna się od kopii [szablony/profil](../szablony/profil).

## zrodla.json — automatyczna aktualizacja dokumentacji

```json
{"zrodla": [
  {"typ": "git", "repo": "https://github.com/Optimal-Design-of-Structures/CDE.git", "galaz": "repo_do_rozmowy_z_CDE_mw1",
   "pliki": ["cdeReposForAI/rozmowa_z_baza_cde/OPIS_KOLUMN.md", "handbook/glossary.md", "docs/**/*.md"]},
  {"typ": "url", "url": "https://raw.githubusercontent.com/…/instrukcja.md", "token_env": "GITHUB_TOKEN"}
]}
```

- Pliki trafiają do `wiedza\zrodla\<repozytorium>\…`. Twoje notatki w `wiedza\` zostają nietknięte i nic nie jest kasowane.
- Repozytoria prywatne działają, jeśli `git` na danym komputerze ma dostęp (to samo logowanie co przy `git clone`).
- Kiedy: okno pół-harnessu samo aktualizuje wiedzę, gdy od ostatniego razu minęło ponad 6 godzin. Jest też przycisk
  „Aktualizuj wiedzę”, a z wiersza poleceń (np. w Harmonogramie zadań): `LokalneAI.Eval wiedza --profil profile\cde`.
- Wzorce: `docs/*.md` (jeden folder), `docs/**/*.md` (z podfolderami), konkretne pliki.

## profil.json

```json
{
  "nazwa": "tekla",
  "zakonczenie": "auto",                         // "auto" = po udanych poleceniach koniec bez dodatkowego wywołania modelu; "model" = model pisze odpowiedź;
                                                 // "mieszane" = koniec po zmianach (i poleceniach z "konczy_ture"), a po samym czytaniu model widzi wynik i decyduje dalej
  "narzedzia_wbudowane": ["szukaj_wiedzy", "kalkulator"],
  "narzedzia_sql": ["zapytanie_sql"],            // argument "sql" przechodzi przez strażnika SQL
  "sprawdzaj_liczby": true,                      // liczby w odpowiedzi muszą być w wynikach narzędzi
  "id_zero_to_poprzedni": true,                  // "id": 0 = element z poprzedniego wywołania w tym kroku
  "max_krokow": 6,
  "max_wywolan_w_kroku": 4,
  "plan": false,                                 // krótkie zdanie planu przed wywołaniami
  "opis_narzedzi": "zwiezly",                    // albo "pelny" (opisy parametrów — dłuższy prompt)
  "sql_reguly": [{ "jesli": "v_my_hours", "wymaga": "{{ja}}", "komunikat": "…" }],
  "sql_zabronione": ["timesheet.entry", "people."],
  "chmura": ["anthropic", "openai", "google"]     // dostawcy w chmurze dozwoleni w profilu; bez pola = tylko modele lokalne
}
```

**`chmura`** — zasada biura dla modeli w chmurze (Ustawienia → Modele w chmurze w oknie LokalneAI):

| Profil | `chmura` | Dlaczego |
|---|---|---|
| `cde` | `["anthropic"]` | dane osobowe (godziny pracy): tylko modele lokalne i Claude |
| `tekla`, `pliki`, `komputer` | `["anthropic", "openai", "google"]` | testy obsługi programów przez AI |
| nowy profil bez pola | — | tylko modele lokalne, dopóki ktoś świadomie nie dopisze dostawców |

Dozwolone wartości: `anthropic`, `openai`, `google`. Innych (np. `deepseek`) biblioteka nie przyjmie. Model w chmurze
dostaje to samo co lokalny: `system.md`, `karta.md`, przykłady, narzędzia i wyniki `szukaj_wiedzy`.

**`internet`** — czy po włączeniu przełącznika „Odblokuj dostęp do internetu” model dostaje narzędzia
`szukaj_w_internecie` (DuckDuckGo) i `czytaj_strone` (`HarnessProfile.WithInternet()` + `SessionOptions.Web`).

| Profil | `internet` | Dlaczego |
|---|---|---|
| `cde` | `false` | model widzi dane z bazy i mógłby je wysłać w zapytaniu do wyszukiwarki |
| `tekla`, `pliki`, `komputer` | `true` | dokumentacja, normy, producenci |
| nowy profil bez pola | — | bez internetu |

Adresy w sieci biura i na tym komputerze (prywatne IP, nazwy bez kropki, `.local`, nazwy wskazujące adres prywatny,
także po przekierowaniu) są zablokowane — model nie sięgnie „przez internet” do bazy, dysku budowy ani bramy.

## narzedzia.json

```json
{"narzedzia":[{
  "nazwa": "wstaw_slup",
  "opis": "Wstawia słup od poziomu dołu do poziomu góry w punkcie X, Y.",
  "zmienia_model": true,
  "parametry": {"type":"object","properties":{
      "x_mm": {"type":"number","description":"X w mm"},
      "poziom_gora_m": {"type":"number","description":"poziom góry w metrach, np. 3.53"},
      "profil": {"type":"string"}},
    "required":["x_mm","poziom_gora_m","profil"], "additionalProperties": false},
  "szablon": "szablony/wstaw_slup.cs"
}]}
```

`"konczy_ture": true` przy narzędziu (np. `przejdz_do_folderu`) w trybie `mieszane` kończy turę bez kolejnego
wywołania modelu, bo wynik sam wszystko mówi. Przy samym `cd` model przepisywał ścieżkę i ją psuł.

Zasady, które wyszły z testów na żywym Bieliku:

- **Jednostka w nazwie parametru**: `_mm`, `_m`, `_stopni`. Model nie przelicza wtedy jednostek. Bez tego przy
  „poziomie 3,53” podał 3.53 w miejsce 3530 mm.
- **Poziomy w metrach**, bo tak mówią ludzie. Szablon mnoży je przez 1000.
- **Krótkie opisy.** Model nie dostaje pliku `narzedzia.json`. Surowy plik 23 poleceń Tekli to ok. 10 tys. tokenów
  (zmierzone). Do polecenia systemowego trafia opis zwięzły, w szacunku ok. 1,3 tys. tokenów zamiast ok. 4,3 tys.
  dla wersji z opisami parametrów. Schemat JSON idzie do serwera osobno jako gramatyka i nie zajmuje kontekstu.
- **Parametry wymagane tylko te, bez których nie da się wykonać polecenia.** Gdy ich brak, model ma dopytać.

## Przykłady i testy

`przyklady.jsonl`:

```json
{"stan":"ostatnio wstawiony słup id=1001","uzytkownik":"zmień go na HEB320","wywolania":[{"narzedzie":"zmien_profil","argumenty":{"id":1001,"profil":"HEB320"}}]}
{"uzytkownik":"wstaw słup","wywolania":[],"pytanie":"Podaj profil, punkt i poziomy słupa."}
```

`testy.jsonl` (profil z poleceniami):

```json
{"id":"A01","kroki":[{"uzytkownik":"…","oczekiwane":[{"narzedzie":"…","argumenty":{…}}],"wyniki":["OK id=1001"]}]}
```

`testy.jsonl` (profil z bazą):

```json
{"id":"B01","kroki":[{"uzytkownik":"…","sql_wzorcowy":"SELECT …'{{ja}}'…","porownanie":"liczby"}]}
```

Pusta lista `oczekiwane` albo `"pytanie": true` oznacza, że model powinien dopytać.
Testy uruchamia `LokalneAI.Eval test --profil profile\<nazwa> --model <tag>` (szczegóły w README).
