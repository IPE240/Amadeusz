# Format profilu

Profil to folder z wiedzą i poleceniami jednego zastosowania. Program wczytuje go przez `HarnessProfile.Load(folder)`.
Gotowe profile:
- [profile/tekla](../profile/tekla): polecenia w Tekli;
- [profile/cde](../profile/cde): pytania do bazy CDE;
- [profile/pliki](../profile/pliki): foldery, kopiowanie i przenoszenie, bez kasowania i bez PowerShella;
- [profile/komputer](../profile/komputer): PowerShell, programy, czytanie i wyszukiwanie plików, z pytaniem o każdą zmianę;
- [profile/office](../profile/office): dokumenty Microsoft 365 — Word (czytanie, dopisywanie, zamiana), Excel (czytanie,
  wpisywanie wartości i formuł), Outlook (szkic maila, lista odebranych), PowerPoint (czytanie, nowy slajd), zapis kopii jako
  PDF; wykonawca `OfficeExecutor` z projektu `LokalneAI.Harness.Office` (COM zainstalowanego Office’a);
- [profile/ocr](../profile/ocr): tekst ze skanów, zdjęć i PDF-ów bez warstwy tekstowej (`ocr_obraz`, `ocr_pdf`, `ocr_folder`,
  `pdf_info`, `pdf_czytaj`) — Tesseract skryptem z ustawieniami z okna (Ustawienia → OCR: języki, obrót, układ, próg pewności,
  reguły ignorowania); obrazek wklejony do pola rozmowy jest rozpoznawany od razu. Wykonawca `OcrExecutor` z `LokalneAI.Harness.Ocr`;
  ten sam wykonawca obsługuje polecenia `pdf_*` umiejętności `office` (scalanie, wycinanie, obrót stron);
- [profile/rfem](../profile/rfem): modele RFEM 5 (.rf5) czytane z pliku bez programu Dlubal (`rfem_info`, `rfem_pret`, `rfem_wezel`,
  `rfem_przekroje`, `rfem_przypadki`, `rfem_sily`, `rfem_szukaj_pretow`): węzły, pręty z długością i przekrojem, materiały, przypadki
  i kombinacje, siły w prętach z wyników solvera, oraz model otwarty w uruchomionym RFEM 5 przez RF-COM (`rfem_otwarty`,
  `rfem_otwarty_pret`, `rfem_otwarty_przekroje`, `rfem_otwarty_przypadki`, `rfem_otwarty_sily`, `rfem_otwarty_reakcje`, `rfem_wskaz` —
  RFEM prosi użytkownika o kliknięcie obiektu; licencja RF-COM zajmowana na czas jednego polecenia). Wykonawca `RfemExecutor`
  z `LokalneAI.Harness.Rfem`, tylko do odczytu;
- [profile/tablice](../profile/tablice): tablice do projektowania konstrukcji stalowych — profil samej wiedzy (`narzedzia.json` z pustą
  tablicą, `szukaj_wiedzy` i `kalkulator`): charakterystyki przekrojów (Bogucki, Żyburtowicz), połączenia doczołowe DAST, tablice SZS C5
  do połączeń; pliki `.md` w `wiedza\` są generowane z `wiedza\dane\*.json` (jedna sekcja `#` na przekrój albo połączenie);
- [profile/tlumaczenie](../profile/tlumaczenie): tłumaczenie tekstów biura na angielski (opisy techniczne, maile, pisma, notatki,
  tytuły rysunków). Profil samej wiedzy: `narzedzia.json` z pustą tablicą, `zakonczenie: model`, słownik konstrukcyjny
  i zwroty w `wiedza\` przez `szukaj_wiedzy`; odpowiedź modelu to samo tłumaczenie;
- [profile/mkk](../profile/mkk): pytania o programy Marka Kowolika (Hala, Modelarz węzłów) i o Robota, RFEM, IDEA StatiCa,
  IFC, SAP2000. Profil samej wiedzy: `narzedzia.json` ma pustą tablicę, model szuka w dokumentacji pobranej z repozytorium
  MKK (`zrodla.json`) i odpowiada; okno nie pokazuje wtedy opcji Tekli (`HarnessProfile.HasHostTools`);
- zestaw **Check Structures** z repozytorium `CS-cheak_structures` (siedem profili samej wiedzy, każdy plik repozytorium
  w dokładnie jednym, żeby łączenie w profil nie dublowało fragmentów; podział i decyzje w
  [profile/cs_program/README.md](../profile/cs_program/README.md)):
  [profile/cs_program](../profile/cs_program) (obsługa programu: instalacja, viewer, toolkit, teczki, wersje, serwisy,
  eksporty, `.csx`, koszty, agent), [profile/cs_ifc](../profile/cs_ifc) (import IFC, bliźniak analityczny, jednostki,
  psety, nazwy profili), [profile/cs_konstrukcja](../profile/cs_konstrukcja) (klasyfikacja `klasa_p1..p4`, role, typy
  zespołów, osie i poziomy, płaszczyzny, obiekty i dylatacje), [profile/cs_polaczenia](../profile/cs_polaczenia) (śruby,
  strefy i typy połączeń, krawędzie i spoiny, spoiny ze źródeł, sztywność, IDEA StatiCa),
  [profile/cs_statyka](../profile/cs_statyka) (model MES z IFC, `static`, obciążenia, porównanie modeli, wyniki 3D),
  [profile/cs_rysunki](../profile/cs_rysunki) (Układy, arkusze zespołów, części, NC DSTV, nesting),
  [profile/cs_robot](../profile/cs_robot) (Robot przez COM, wytężenie na IFC, most MES → Robot); dokumentacja RFEM
  z tego repozytorium doszła do `rfem` (`zrodla.json`, `wiedza/rfem_dlubal.md`);
- [profile/amadeusz](../profile/amadeusz): pytania o samego Amadeusza — okno i ustawienia, porty i zapora, brama HTTP,
  podpinanie programów, kontrola `--autotest`, pliki, instalacja. Profil samej wiedzy (`szukaj_wiedzy`), wiedza przepisana
  z [sterowanie.md](sterowanie.md), [porty.md](porty.md), [siec.md](siec.md) i README na krótkie sekcje; test
  `AmadeuszWiedzaTests` pilnuje, żeby opisywała każdy przełącznik, trasę bramy i port z kodu;
- [profile/formatowanie](../profile/formatowanie): same zasady tabel i wykresów (bez narzędzi), domyślnie w każdym profilu.

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
| Glosariusz (pary terminów) | `wiedza\*.txt` (`.tsv`, `.csv`) — jedna para w wierszu: `expansion joint – kompensator` | model nie musi szukać: sesja sama znajduje w poleceniu (i w tekście z narzędzi, np. dokumencie Word) terminy z glosariusza, także odmienione, i dokleja ich odpowiedniki (`Glossary`) |
| Wspólny plik biura na dysku (np. glosariusz dla wszystkich) | **Źródła wiedzy… → Pliki i foldery na dysku** | ścieżka tylko w ustawieniach komputera (`AppSettings.DiskKnowledge`); plik czytany przy każdej nowej sesji, kopia w `%LOCALAPPDATA%\LokalneAI\wiedza_z_dysku\` na czas, gdy dysk jest niedostępny |

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

## Kilka profili w jednej rozmowie

Zakładka Code pozwala zaznaczyć kilka profili naraz, a gospodarz w kodzie łączy je przez `HarnessProfile.Combine(...)`.

| Co | Jak się łączy |
|---|---|
| `system.md` | wstęp „pracujesz w kilku rolach” i każda część pod nagłówkiem `# Rola: <profil>` |
| `karta.md`, przykłady | wszystkie, po kolei |
| narzędzia | suma; przy tej samej nazwie wygrywa pierwszy profil (`pliki` przy `komputer` okno pomija) |
| `wiedza\` | jeden indeks `szukaj_wiedzy`, źródło z przedrostkiem profilu (`cde/stan_danych.md`) |
| `chmura` | część wspólna — dostawca musi być dozwolony we **wszystkich** |
| `internet` | zgoda tylko, gdy pozwalają **wszystkie**; w oknie przełącznik jest nadrzędny (patrz niżej) |
| `sql_reguly`, `sql_zabronione`, `sprawdzaj_liczby` | suma (obowiązują przy narzędziach SQL) |
| `zakonczenie` | wspólne, gdy wszystkie mają takie samo; inaczej `mieszane` |
| `max_krokow`, `max_wywolan_w_kroku` | największe |

Każde wywołanie modelu wysyła całe polecenie systemowe i całą rozmowę, ale **nie** całą dokumentację: z `wiedza\` model
dostaje tylko fragmenty, o które poprosi przez `szukaj_wiedzy` (3 fragmenty po ok. 900 znaków). Ollama trzyma w pamięci
przeliczony początek rozmowy, więc ten sam początek liczy się szybko. Koszt łączenia to suma poleceń systemowych —
`LokalneAI.Eval rozmiar --profil profile\<nazwa>` pokazuje rozmiar każdego profilu.

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
  "max_krokow": 12,                              // kroki modelu na jedno polecenie (domyślnie 12; 2026-11-19 podwojone we wszystkich profilach do prób)
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
| `mkk` | bez pola | dokumentacja MKK jest wewnętrzna i poufna (LICENCE w repozytorium); dopisanie `["anthropic"]` to decyzja użytkownika |
| `office` | bez pola | model czyta treść dokumentów i maili biura (umowy, raporty, korespondencja); dopisanie dostawcy to decyzja użytkownika |
| `ocr` | bez pola | skany to dokumenty biura (protokoły, pisma, umowy) |
| `rfem` | bez pola | modele obliczeniowe projektów klientów |
| `tablice` | bez pola | wiedza z książek objętych prawem autorskim zostaje na komputerze |
| `cs_program`, `cs_ifc`, `cs_konstrukcja`, `cs_polaczenia`, `cs_statyka`, `cs_rysunki`, `cs_robot` | bez pola | repozytorium CS jest prywatne, dokumentacja zawiera ustalenia biura i nazwy modeli; dopisanie `["anthropic"]` to decyzja użytkownika |
| `amadeusz` | bez pola | wiedza o programie i sieci biura (porty, nazwy komputerów); dopisanie dostawców to decyzja użytkownika |
| nowy profil bez pola | — | tylko modele lokalne, dopóki ktoś świadomie nie dopisze dostawców |

Dozwolone wartości: `anthropic`, `openai`, `google`. Innych (np. `deepseek`) biblioteka nie przyjmie. Model w chmurze
dostaje to samo co lokalny: `system.md`, `karta.md`, przykłady, narzędzia i wyniki `szukaj_wiedzy`.

**`poswiadczenie`** — nazwa wpisu w Ustawieniach → Menedżer poświadczeń (PostgreSQL: nazwa bazy), z którego profil
z narzędziem SQL bierze połączenie i `{{ja}}` (person_id). Profil z tym polem jest w zakładce Code ukryty, dopóki
takiego wpisu nie ma (np. `cde` widzą tylko osoby z dostępem do bazy). Adresów i haseł nie wpisuj do profilu.

**`internet`** — zgoda umiejętności na internet: czy po włączeniu przełącznika „Odblokuj dostęp do internetu” model dostaje
narzędzia `szukaj_w_internecie` (DuckDuckGo) i `czytaj_strone` (`HarnessProfile.WithInternet()` + `SessionOptions.Web`).
W oknie Amadeusza przełącznik jest **nadrzędny** (decyzja użytkownika z 2026-11-19): wybór umiejętności bez zgody sam go wyłącza
(z informacją w rozmowie), a świadome włączenie z powrotem działa także z nią (`WithInternet(evenIfSkillsForbid: true)`; nagłówek:
„internet: tak — włączony mimo braku zgody umiejętności …”). Inne programy z biblioteką domyślnie trzymają się zgody.

| Profil | `internet` | Dlaczego |
|---|---|---|
| `cde` | `false` | model widzi dane z bazy i mógłby je wysłać w zapytaniu do wyszukiwarki |
| `tekla`, `pliki`, `komputer`, `mkk` | `true` | dokumentacja, normy, producenci (`mkk`: publiczna dokumentacja Dlubal, Autodesk, IDEA StatiCa; `system.md` zakazuje wysyłania fragmentów wiedzy) |
| `office` | `false` | jak `cde`: model widzi treść dokumentów i maili i mógłby ją wysłać w zapytaniu do wyszukiwarki |
| `ocr` | `false` | jak `office`: tekst ze skanów zostaje na komputerze |
| `rfem` | `true` | decyzja użytkownika z 2026-11-19 (bez zgody wyłączał internet przy `komputer` + `rfem`); model widzi geometrię i siły z projektów klientów, więc wyciek ogranicza strażnik (fraza do 200 znaków, strony tylko z adresów od użytkownika albo z wyników) |
| `tablice` | `false` | odpowiedzi tylko z tablic, bez szukania w internecie |
| `cs_ifc`, `cs_robot` | `true` | publiczna dokumentacja buildingSMART, IfcOpenShell i Autodesk; `system.md` zakazuje wysyłania fragmentów wiedzy |
| `cs_program`, `cs_konstrukcja`, `cs_polaczenia`, `cs_statyka`, `cs_rysunki` | `false` | treść wewnętrzna CS (decyzje biura, modele, reguły) |
| `amadeusz` | `false` | odpowiedzi tylko z wiedzy o programie |
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
