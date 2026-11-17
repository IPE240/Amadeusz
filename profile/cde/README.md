# Profil B: pytania o projekty i godziny z bazy CDE

Profil dla lokalnego modelu (np. Bielik 11B), który odpowiada po polsku na pytania z zarządzania projektami biura ODS, pisząc SQL (tylko SELECT) do bazy CDE (PostgreSQL 15). Rachunki robi baza.

Połączenie: Ustawienia → Menedżer poświadczeń, wiersz PostgreSQL z nazwą bazy `cde` (adres, user, hasło, person_id; zapisane w Menedżerze poświadczeń Windows). Profil wskazuje go polem `"poswiadczenie": "cde"` w `profil.json`; bez takiego wpisu profil cde jest w zakładce Code ukryty. Serwer bazy opisuje repozytorium `CDE_serwer_bazy` (README pobierane do `wiedza\zrodla\`), strukturę danych — `cdeReposForAI/rozmowa_z_baza_cde/OPIS_KOLUMN.md` w repozytorium CDE.

Aktualizacja 2026-11-08: treść dokumentów (`library.section`, wyszukiwanie słów), rewizje (`v_document_history`), notatka `wiedza/dokumenty.md`, przykłady i testy B44–B46; zablokowane `fin.` (finanse — dane poufne, w bazie jeszcze ich nie ma) oraz kolumny z nazwiskami (`author_raw`, `checker_raw`) i całe treści (`text_md`). Wszystkie 61 zapytań z przykładów i testów przechodzi na bazie (EXPLAIN, READ ONLY).

## Pliki
- `narzedzia.json` - jedno narzędzie `zapytanie_sql(sql, cel)`.
- `system.md` - polecenie systemowe (ok. 390 słów).
- `karta.md` - karta bazy do promptu (ok. 900 słów): relacje, reguły, pułapki, stan danych.
- `przyklady.jsonl` - 15 par few-shot (13 z zapytaniem, 2 dopytania).
- `testy.jsonl` - 43 testy (B01-B43), w tym sesje wielokrotne (B02, B28) i 2 oczekiwane dopytania (B37, B38).
- `wiedza/*.md` - 5 notatek do wyszukiwania: godziny, projekty i fazy, stal i listy materiałowe, czynności, stan danych.

## Co sprawdzono
Wszystkie 56 zapytań (13 z przykładów i 43 sql_wzorcowych z testów) wykonano w transakcji READ ONLY (`statement_timeout` 15 s, `rollback`), najpierw `EXPLAIN`, potem wykonanie; wszystkie przeszły (56/56). Znacznik `{{ja}}` podmieniano na uuid testowy. Do plików nie trafiły żadne wyniki (tylko SQL). Jedyny test z oczekiwanym pustym wynikiem to B43 (świadomie: brak godzin w danym roku).

## Reguły przyjęte w SQL
- Godziny: `hours_counted`; `hours_pending` osobno. Bez filtra po `may_be_incomplete`.
- "Ile przepracowałem" bez projektu = work + internal z rozbiciem; ranking projektów tylko `entry_kind = 'work'`.
- Stal: tylko `library.v_steel_by_project`, gatunki osobno (`ILIKE 'S355%'`), suma ton = `SUM(mass_kg)/1000`.
- Statystyki projektów pomijają `code = '9000'` (projekt testowy).
- "W realizacji" = faza R; "aktywne" = O, R, B, W (przykład pokazuje rozbicie).

## Wątpliwości
- Stal jest wczytana tylko dla projektu 0546 (3 gatunki); pytania o stal innych projektów dadzą pusty wynik i model musi to powiedzieć jako brak danych.
- Znacznik `{{ja}}` w SQL generowanym przez model: zakładam, że harness podmienia go także w zapytaniach modelu (system.md każe wpisać go dosłownie). Jeśli harness podaje person_id inaczej, trzeba poprawić `system.md` i `karta.md`.
- Znaczenie "projekt w realizacji" (R czy wszystkie aktywne) i czy "ile przepracowałem" ma obejmować zadania wewnętrzne - decyzja robocza, opisana wyżej.
- Test B41 używa `CURRENT_DATE` (wynik zależy od daty uruchomienia); B33 oczekuje, że projekt bez wykazów (1095) po prostu nie pojawi się w wyniku.
- `v_my_hours` zawiera wszystkie osoby; bezpieczeństwo opiera się na filtrze w SQL modelu, więc harness powinien dodatkowo wymusić filtr albo odrzucać zapytania bez `person_id`.
- Przykłady dopytań zawierają nazwy projektów (nie dane osobowe). Wiedza o 0546 (ok. 91,6 t) pochodzi z odczytu bazy z 2026-11-06 i się zdezaktualizuje po kolejnym ładowaniu dokumentów.
- Pliki wzorcowe są tylko SQL; porównanie `liczby` vs `wiersze` zostawia harnessowi interpretację (kolejność wierszy bez znaczenia dla `liczby`).
