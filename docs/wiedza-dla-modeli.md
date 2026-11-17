# Dokumentacja dla lokalnych modeli: gdzie ją wkładać i jak pisać

Dla każdego, kto chce, żeby asystent w programie biura coś wiedział: programistów i autorów instrukcji.
Zasady dla programistów, czyli kiedy pisać takie opisy: [zasady-opisow-dla-ai.md](zasady-opisow-dla-ai.md).

## Gdzie

Wiedza leży tam, gdzie pracuje ten, kto ją zna i aktualizuje.

```
Repozytorium programu biura (TransmittalMaster, CDE, most Tekli, …)
└─ lokalne-ai\
   ├─ CZYTAJ.md           opis folderu dla ludzi (model go nie widzi)
   ├─ wiedza\*.md         wiedza o programie i jego danych; pisze zespół programu razem z kodem
   └─ profil\             tylko gdy program ma wbudowanego asystenta: system.md, karta.md, narzedzia.json…

Amadeusz (to repozytorium)
└─ profile\<profil>\      tekla, cde, pliki, komputer, mkk
   ├─ system.md           zasady zachowania asystenta
   ├─ karta.md            pojęcia, jednostki, pułapki; trafia do KAŻDEGO pytania
   ├─ przyklady.jsonl     przykłady „polecenie → wywołanie”
   ├─ zrodla.json         z których repozytoriów pobierać wiedzę (przycisk „Źródła wiedzy…”)
   └─ wiedza\
      ├─ CZYTAJ.md        co tu wkładać (model tego nie widzi)
      ├─ *.md             wiedza ogólna biura dla tego profilu (w gicie)
      └─ zrodla\          pobrane automatycznie z repozytoriów; poza gitem, NIE EDYTOWAĆ
```

| Wiedza | Gdzie | Kto aktualizuje |
|---|---|---|
| o programie: funkcje, pojęcia, dane, typowe pytania użytkowników | `lokalne-ai\wiedza\` w repozytorium programu | zespół programu, razem ze zmianą w kodzie |
| o bazie danych: tabele, kolumny, widoki, jednostki | `lokalne-ai\wiedza\` w repozytorium bazy (CDE ma już `cdeReposForAI\` i `handbook\`) | właściciel bazy |
| ogólna biura: nazwy folderów projektów, numeracja rewizji, normy, skróty | `profile\<profil>\wiedza\` w Amadeuszu | ktokolwiek; zmiana przez commit |
| zasady zachowania asystenta | `system.md` profilu | autor profilu |
| pojęcia i jednostki, bez których model się myli | `karta.md` profilu (krótko) | autor profilu |
| jak wywoływać polecenia | `przyklady.jsonl` i opisy w `narzedzia.json` | autor profilu, razem z kodem polecenia |

Wiedza z repozytoriów programów trafia do profili przez `zrodla.json`. Najprościej przyciskiem **Źródła wiedzy…**
w oknie asystenta. Wpis dla repozytorium z folderem `lokalne-ai`:

```json
{"typ": "git", "repo": "https://github.com/Optimal-Design-of-Structures/<Repozytorium>.git", "galaz": "main",
 "pliki": ["lokalne-ai/wiedza/**/*.md"]}
```

Pliki pobierają się do `wiedza\zrodla\<Repozytorium>\`. Poprawki wprowadza się w źródle, bo zmiany w `wiedza\zrodla\`
zniknęłyby przy następnej aktualizacji.

Nowe repozytorium programu: skopiuj [szablony/repozytorium](../szablony/repozytorium). Są tam `AGENTS.szablon.md`
i `CLAUDE.szablon.md` (po skopiowaniu zmień nazwy na `AGENTS.md` i `CLAUDE.md`) oraz `lokalne-ai\` z pierwszą notatką
do wypełnienia.

## Jak model korzysta z wiedzy

- **`system.md` i `karta.md`:** całe trafiają do każdego pytania. Każde słowo zajmuje kontekst i spowalnia odpowiedź,
  dlatego mają być krótkie. Rozmiar mierzy `LokalneAI.Eval rozmiar --profil <folder>`.
- **`wiedza\`:** model sięga po nią narzędziem `szukaj_wiedzy`, gdy czegoś nie wie (narzędzie trzeba włączyć
  w `profil.json`: `"narzedzia_wbudowane": ["szukaj_wiedzy"]`). Wyszukiwarka działa tak:
  - porównuje słowa, nie znaczenie: bez wielkości liter i polskich znaków, a dłuższe słowa skraca do 6 pierwszych liter.
    Dzięki temu „projektu” i „projektów” to to samo słowo („projek”). Krótsze słowa z inną końcówką już się nie łączą:
    „kotwa”, „kotwy” i „kotwami” to trzy różne słowa. Dlatego w nagłówku albo w treści warto użyć kilku form;
  - fragment to sekcja pod nagłówkiem `#`, `##`, `###`; sekcja dłuższa niż ok. 1500 znaków jest dzielona;
  - model dostaje 3 najlepsze fragmenty, każdy ucięty do 900 znaków, z nazwą pliku i nagłówkiem;
  - pliki `CZYTAJ.md` są pomijane.

## Jak pisać notatki do `wiedza\`

1. **Jedna sekcja to jedno zagadnienie, do ok. 900 znaków** (ok. 120 słów). Dalej model już nie widzi.
2. **Nagłówek ze słowami, których ludzie używają w pytaniach, także w kilku formach.** Lepiej „Kotwy, kotwa: średnica,
   długość, M24, M30” niż „Rozdział 4.2”. Synonimy i skróty dopisz w treści, np. „ostroga (kotwa ścinana, shear key)”.
3. **Sekcja zrozumiała sama.** Bez „jak wyżej” i „patrz rozdział 3”, bo model dostaje wycięty fragment.
4. **Konkret:** liczby z jednostkami, nazwy pól i przycisków takie jak w programie, przykład polecenia albo zapytania.
5. **Po polsku.** Angielski termin można dopisać w nawiasie.
6. **Prosty Markdown:** nagłówki, listy, krótkie tabele. Bez obrazków (model ich nie widzi) i bez HTML.
7. **Bez danych osobowych i haseł.** Nie wpisuj nazwisk z bazy, godzin osób, `person_id` ani danych logowania.
8. **Tylko to, o co ludzie będą pytać asystenta.** Instrukcja budowania programu nie pomoże w rozmowie o projekcie.

Przykład dobrej sekcji:

```markdown
## Rewizje rysunków: R00, R01, numeracja, kiedy podnieść

Rewizję zapisujemy jako R i dwie cyfry: R00 to pierwsze wydanie, potem R01, R02.
Rewizję podnosimy przy każdej zmianie wysłanej do klienta (transmittal), także gdy zmienia się tylko opis.
Folder wydania: `<projekt>\Wydania\R01\`. W tabelce rysunku pole „Rev.” ma tę samą wartość co folder.
Przykładowe pytania: „jaka jest ostatnia rewizja rysunku 1095-K-101?”, „kiedy dajemy R01?”.
```

## Jak pisać `karta.md` i `system.md`

- **`karta.md`** to ściąga, nie podręcznik. Pojęcia i jednostki, pomyłki, które model już zrobił, nazwy, które musi znać.
  Orientacyjnie do 700 słów. Wszystko, co dłuższe, przenieś do `wiedza\`.
- **`system.md`**: rola („jesteś asystentem programu X”), czego nie robić, kiedy pytać, jak odpowiadać.
  Do ok. 400 słów, w punktach. Każdą zasadę dopisaną po błędzie modelu uzupełnij przykładem w `przyklady.jsonl`:
  małe modele uczą się bardziej z przykładów niż z zasad.

## Jak sprawdzić

1. **Co znajdzie wyszukiwarka:** `LokalneAI.Eval szukaj --profil <folder profilu> --fraza "kotwy M24"` pokazuje
   dokładnie te fragmenty, które dostałby model.
2. **Jak odpowie model:** w oknie testowym (przycisk **Pół-harness…**, wybór profilu) zadaj pytanie. Panel pokazuje
   wywołanie `szukaj_wiedzy` i wynik.
3. **Aktualizacja z repozytoriów:** w oknie asystenta przycisk „Aktualizuj wiedzę”, a z wiersza poleceń
   `LokalneAI.Eval wiedza --profil <folder>`.
