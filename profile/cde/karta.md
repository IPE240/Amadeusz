# Karta bazy CDE (PostgreSQL 15, tylko SELECT)

Biuro ODS projektuje konstrukcje stalowe. Baza CDE trzyma projekty, godziny pracy (import z systemu vaibro), słowniki oraz bibliotekę dokumentów ze stalą. Używaj WYŁĄCZNIE relacji poniżej i zawsze z aliasem tabeli.

## Pułapki
- Nazwa schematu to nie alias. Pisz `FROM timesheet.v_my_hours h`, potem `h.month`, nie `timesheet.month`.
- `project_code` i `code` to TEKST z zerem wiodącym: `'1095'`, `'0546'` (nie liczba).
- `month` to DATA = 1. dzień miesiąca (sierpień 2026 = `DATE '2026-08-01'`). Rok filtruj zakresem: `h.month >= DATE '2025-01-01' AND h.month < DATE '2026-01-01'`.
- Symbol projektu (`symbol`) NIE jest unikalny (np. ABS, CWS, MRO ma po 2 projekty). Numer (`code`) jest unikalny.
- `v_my_hours` zawiera WSZYSTKIE osoby: zawsze `h.person_id = '{{ja}}'`. Nigdy nie czytaj ani nie podawaj danych innych osób.
- Po `JOIN` z `project.v_project` gubisz wiersze bez projektu (kod wewnętrzny); do sum ogółem używaj `v_my_hours` bez joinu.
- Sumy: `SUM(...)` na `numeric`; przy braku wierszy SUM daje NULL, więc `COALESCE(SUM(...),0)`, ale pusty wynik (0 wierszy) = brak danych.

## project.v_project p (jeden wiersz = jeden projekt)
- `p.code` numer 4 cyfry (tekst, unikalny); `p.symbol` krótki symbol (nieunikalny, wielkie litery); `p.name` nazwa
- `p.phase_code` O/R/B/W/Z/X/N; `p.phase_name`; `p.phase_is_active` (true dla O, R, B, W); `p.phase_sort` kolejność faz
- `p.city`, `p.country_name`, `p.object_category_name`, `p.client_name` (nazwa klienta; można podać), `p.archived_at`
- NIE czytaj `manager_*` (dane osób).
Fazy: O ofertowy, R realizacja (projektowanie), B w trakcie budowy, W wstrzymany, Z zakończony, X oferta bez realizacji, N niebiznesowy (pseudo-projekty wewnętrzne). „W realizacji" = zwykle R; „aktywne" = O, R, B, W. Projekt 9000 to projekt testowy: pomijaj w statystykach (`p.code <> '9000'`). Razem ok. 722 projekty (większość Z i X).

## Wyszukiwanie projektu
Po numerze: `p.code = '1095'`. Po symbolu: `p.symbol ILIKE 'WSP'`. Po nazwie: `p.name ILIKE '%spalarnia%'`. Gdy użytkownik podaje słowo bez rodzaju, sprawdź wszystkie trzy: `p.code = 'x' OR p.symbol ILIKE 'x' OR p.name ILIKE '%x%'`. Jeśli pasuje więcej niż jeden projekt, zapytaj, o który chodzi (albo podaj wynik osobno dla każdego). Przykłady: WSP = 1095 (Włocławek spalarnia), CPK = 1063, PMZ = 0546.

## timesheet.v_my_hours h (godziny pytającego; wiersz = osoba x projekt x miesiąc x czynność)
`h.person_id` (zawsze filtruj `= '{{ja}}'`), `h.project_code` (NULL = zadania wewnętrzne biura bez projektu), `h.entry_kind` ('work' = projekt, 'internal' = wewnętrzne; nieobecności są wyłączone), `h.month`, `h.activity_code` (NULL = czynność nieznana), `h.hours_counted` (godziny liczone), `h.hours_pending` (czekające), `h.may_be_incomplete`.
Pytanie „ile przepracowałem" bez wskazania projektu = wszystkie wpisy (work + internal); podaj rozbicie przez `SUM(...) FILTER (WHERE h.entry_kind = 'work')`.

## timesheet.v_hours_project_month hp (godziny projektów, wszystkie osoby razem, bez nazwisk; tylko praca na projektach)
`hp.project_code`, `hp.month`, `hp.activity_code`, `hp.activity_name`, `hp.activity_group`, `hp.hours_counted`, `hp.hours_pending`, `hp.entries_counted`, `hp.may_be_incomplete`. Brak zadań wewnętrznych (te są tylko w `v_my_hours`). Dane od 2021-11 do 2026-09.
Nie rozbijaj tego widoku na osoby (nie ma takiej możliwości i nie wolno szukać obejść).

## Reguły godzin
- Godziny przepracowane = `hours_counted`. `hours_pending` to godziny czekające na rozstrzygnięcie nakładki z kartami pracy KP (okres 2021-11 do 2023-07); podawaj je OSOBNO, nie doliczaj do sumy.
- `may_be_incomplete` = miesiące do 2023-07 mogą być niepełne (część pracy tylko w kartach KP). NIE filtruj po tym, najwyżej ostrzeż.
- Jednostka: godziny (h). Importowane są wpisy z vaibro; ostatni import 2026-10-02, ostatni miesiąc z danymi 2026-09. Bieżący miesiąc (od października 2026) nie ma jeszcze danych.

## ref.activity a (słownik czynności)
`a.code`, `a.name_pl`, `a.activity_group`, `a.retired_on`, `a.sort_order`. Kody: mod model (Tekla), obl obliczenia, rys rysunki, rew rewizje, spr sprawdzanie, org organizacja, ops opisy techniczne, ofe oferta, druk drukowanie i wysyłka, del delegacje i spotkania, eks ekspertyza, adm administracja, prg programowanie, utk ustawienia Tekli, kap katalog połączeń, sof testowanie programów, szk szkolenia, wdr wdrożenia, prom promocja, ark partner zewnętrzny, hwc prace nieokreślone. Dołączaj `LEFT JOIN ref.activity a ON a.code = h.activity_code` (w `v_hours_project_month` jest już `activity_name`).
Grupy czynności (`activity_group`): modelowanie, obliczenia, rysunki, rewizje, sprawdzanie, koordynacja, dokumentacja, ofertowanie, wydanie, teren, ekspertyza, rozwoj (prg, utk, szk, kap, sof, wdr), administracja, promocja, nieokreslone. „Praca" w pytaniach = wpisy `work` i `internal` z godzin liczonych; jeśli użytkownik pyta o „pracę na projektach", użyj `entry_kind = 'work'`.

## ref.project_phase f
`f.code`, `f.name_pl`, `f.is_active_work`, `f.sort_order` (opis faz wyżej). Stadia projektowania `ref.design_stage`: PB (10) < PW (20) < PV (30) < EP (40); większy numer = dokładniejsze stadium.

## Stal (biblioteka dokumentów, schemat library)
`library.v_steel_by_project s`: `s.project_code`, `s.grade` (gatunek, np. S235JR, S355J2, 16Mo3), `s.mass_kg` (kg), `s.mass_t` (tony), `s.stage_used` (stadium źródła), `s.documents` (liczba wykazów), `s.scopes`, `s.several_lists_in_one_scope` (true = w jednym zakresie kilka wykazów, do przejrzenia).
REGUŁA: tony stali liczy się z `v_steel_by_project` (tylko najdokładniejsze stadium z danymi, wykazy bez dubli, obowiązujące rewizje). NIE sumuj stadiów ani wierszy `v_steel_stage_coverage` (wtedy ta sama stal liczy się wielokrotnie) i nie sumuj `v_bom_lists.mass_t` (zawiera duble). Gatunki liczysz OSOBNO; gatunek szukaj przez `s.grade ILIKE 'S355%'` (S355J2, S355JR...). Razem wszystkie gatunki: `ROUND(SUM(s.mass_kg)/1000, 3)`; osobno i razem: `GROUP BY ROLLUP (s.grade)`.
Stan: wykazy stali są wczytane tylko dla projektu 0546 (PMZ, reaktor SCR Pomorzany): trzy gatunki. Dla innych projektów brak wyniku = „brak wykazów stali w bazie", nie „zero ton".
`library.v_bom_lists b`: wykazy (`b.project_code`, `b.list_kind` materialowa/elementow/zespolow, `b.mass_t`, `b.counted` = liczony do sumy, `b.duplicate_reason`, `b.title`, `b.stage_code`). Dubel (counted = false) nie wchodzi do sum.

## Dokumenty (podstawowo)
`library.v_document_current d` (jeden wiersz = dokument w obowiązującej rewizji): `d.project_code`, `d.title`, `d.stage_code` (PB/PW/PV/EP lub NULL = nieznane), `d.scope_path`, `d.current_revision`, `d.revision_count`, `d.content_category`. Razem ok. 3199 dokumentów w ok. 203 projektach. Nie czytaj `author_raw` ani `checker_raw` (nazwiska).

## Stan danych (listopad 2026)
Godziny: import vaibro, ostatni 2026-10-02, miesiące 2021-11 do 2026-09; miesiące do 2023-07 mogą być niepełne. Stal: tylko projekt 0546. Dokumenty: wczytane 2026-10-26.
