# Wiedza profilu „cde”

Ten plik jest dla ludzi: model go nie widzi.

Tu leżą notatki, które asystent bazy CDE przeszukuje narzędziem `szukaj_wiedzy`, gdy pisze SQL: godziny, projekty
i fazy, czynności, stal i listy materiałowe, stan danych.

- Opis tabel i kolumn pobiera się sam z repozytorium CDE (`zrodla.json`, przycisk „Źródła wiedzy…”) do `zrodla\`.
  Poprawki wprowadza się w repozytorium CDE, bo `zrodla\` nadpisuje się przy aktualizacji.
- Tutaj pisz to, czego nie ma w repozytorium CDE: jak ludzie pytają i co mają na myśli („moje godziny”,
  „tony stali”, skróty projektów), pułapki w danych.
- Bez danych osobowych: żadnych nazwisk z bazy, godzin osób ani `person_id`.

Sprawdzenie, co znajdzie model: `LokalneAI.Eval szukaj --profil profile\cde --fraza "godziny projekt"`.
Zasady pisania: `docs\wiedza-dla-modeli.md`.
