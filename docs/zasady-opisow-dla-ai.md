# Opisy dla AI: zasady dla programistów ODS

Z naszego kodu korzystają dwa rodzaje AI i oba potrzebują opisów:

| Kto | Co robi | Co czyta |
|---|---|---|
| **agenci kodujący** (Claude Fable i Opus, Cursor i inni) | piszą i zmieniają kod w naszych repozytoriach | `AGENTS.md` i `CLAUDE.md` w repozytorium, komentarze `///`, dokumentację w `docs\` |
| **lokalne modele** (Bielik w Ollamie) | odpowiadają użytkownikom w naszych programach i wykonują ich polecenia | profil asystenta i notatki w `lokalne-ai\wiedza\` ([wiedza-dla-modeli.md](wiedza-dla-modeli.md)) |

Agent bez opisu zgaduje i psuje rzeczy, których nie widać w kodzie, np. pisze do folderu, z którego coś się
automatycznie wykonuje. Lokalny model bez opisu odpowiada ogólnikami albo zmyśla.

## Zasada

**Zmiana nie jest skończona, dopóki opisy dla AI nie mówią prawdy.** Opis piszesz razem ze zmianą kodu, w tym samym
commicie. Agent kodujący pomoże: poproś go „uzupełnij opisy dla AI według docs/zasady-opisow-dla-ai.md w repozytorium
Amadeusz”.

## Kiedy co uzupełnić

| Zmieniasz | Uzupełnij |
|---|---|
| zakładasz repozytorium albo program | `AGENTS.md` i `CLAUDE.md` z [szablony/repozytorium](../szablony/repozytorium) (pliki `*.szablon.md`); folder `lokalne-ai\`, jeśli użytkownicy będą pytać asystenta o ten program |
| budowanie, testy, układ projektu, pułapki („nie pisz do folderu X”) | `AGENTS.md` repozytorium |
| publiczną klasę albo metodę biblioteki | komentarz `///` (w bibliotekach Amadeusza brak opisu to ostrzeżenie CS1591); w Amadeuszu także [biblioteki-dla-agentow.md](biblioteki-dla-agentow.md) |
| funkcję programu, o którą użytkownicy mogą zapytać | notatkę w `lokalne-ai\wiedza\` |
| dane: tabele, kolumny, jednostki, znaczenie pól | notatkę w `lokalne-ai\wiedza\` repozytorium danych |
| polecenie asystenta (narzędzie) | opis i parametry w `narzedzia.json`, 2–3 przykłady w `przyklady.jsonl` (w tym dopytanie), test w `testy.jsonl`, kod w wykonawcy |
| działanie istniejącego polecenia | opis w `narzedzia.json`, przykłady i testy tego polecenia |
| pojęcie, które model myli | `karta.md` profilu (jedno zdanie) i przykład |

## AGENTS.md: co ma w nim być

Jeden plik w katalogu głównym repozytorium, czytany przez agentów przy każdej pracy.
Do tego `CLAUDE.md` z jedną linią `@AGENTS.md`, żeby Claude Code czytał to samo.

1. **Co to jest:** dwa, trzy zdania o programie, dla kogo i na czym działa (.NET Framework 4.8 / .NET 10, Tekla 2019…).
2. **Mapa:** projekty i foldery z jednym zdaniem o każdym.
3. **Budowanie i testy:** dokładne polecenia, które działają z katalogu repozytorium.
4. **Zasady, których nie wolno łamać,** z powodem. Przykłady: „nie pisz do `exchange`, bo skrypt wykona się w Tekli”,
   „baza tylko do odczytu”, „nie testuj na A:”.
5. **Konwencje:** język komentarzy i komunikatów, styl, gdzie dodawać nowe rzeczy.
6. **Pułapki** znane z doświadczenia, jedno zdanie każda.
7. **Gdzie dalej:** linki do `docs\`.

Pisz krótko, w punktach, tylko sprawdzone fakty. Nieaktualny opis szkodzi bardziej niż brak opisu, bo agent mu wierzy.

## Komentarze `///` w bibliotekach

- **Każdy publiczny element.** Jedno zdanie: co robi i czego nie widać z sygnatury. Przykłady: jednostki,
  `null` = brak, wołane z wątku w tle, rzuca wyjątek, gdy…
- **Nie powtarzaj nazwy.** Zamiast „Pobiera nazwę” napisz „Nazwa, której używa model, np. `wstaw_slup`”.
- **Opisz pułapki użycia,** np. „`null` = zgoda bez pytania”.

## Notatki dla lokalnych modeli

Szczegóły: [wiedza-dla-modeli.md](wiedza-dla-modeli.md). W skrócie:

- Notatki pisz w `lokalne-ai\wiedza\` w repozytorium programu, po polsku.
- Jedna sekcja to jedno zagadnienie, do ok. 900 znaków, pod nagłówkiem ze słowami, których ludzie używają w pytaniach.
- Wpisuj konkrety: liczby z jednostkami, nazwy jak w programie, przykładowe pytania.
- Bez danych osobowych, haseł i godzin osób.
- Sprawdzenie: `LokalneAI.Eval szukaj --profil <profil> --fraza "…"` pokazuje, co dostanie model.

## Lista przed commitem

- [ ] `AGENTS.md` mówi prawdę o budowaniu, testach i zasadach (jeśli coś z tego się zmieniło).
- [ ] Nowe i zmienione elementy publiczne mają `///`, a budowanie nie daje ostrzeżeń.
- [ ] Nowa albo zmieniona funkcja, o którą ktoś zapyta asystenta, ma notatkę w `lokalne-ai\wiedza\`.
- [ ] Nowe polecenie asystenta ma opis, przykłady i test.
- [ ] W opisach nie ma haseł, nazwisk z bazy, godzin osób ani `person_id`.
