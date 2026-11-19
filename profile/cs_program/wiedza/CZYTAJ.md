# Wiedza umiejętności „cs_program”

Ten plik jest dla ludzi: model go nie widzi.

- `obsluga_cs.md`: streszczenie w gicie (start, teczki, serwisy, eksporty, koszty), żeby umiejętność działała też bez dostępu
  do repozytorium CS.
- `zrodla\CS-cheak_structures\`: dokumentacja pobrana z repozytorium `Optimal-Design-of-Structures/CS-cheak_structures`
  według `..\zrodla.json` (README, ODS.md, how-to, help, handbook, README modułów). Pobiera ją Ustawienia → Umiejętności →
  „Aktualizuj” albo `LokalneAI.Eval wiedza --profil profile\cs_program`. Folder `zrodla\` jest poza gitem; poprawki robi się
  w repozytorium CS.

Repozytorium CS jest prywatne i wewnętrzne, dlatego umiejętność pracuje tylko z modelami lokalnymi (bez pola `chmura`).
Siostrzane umiejętności z tego samego repozytorium: `cs_ifc`, `cs_konstrukcja`, `cs_polaczenia`, `cs_statyka`, `cs_rysunki`,
`cs_robot`; wiedza o RFEM jest w `rfem`. Każdy plik repozytorium jest w jednej z nich, żeby łączenie umiejętności w profil
nie dublowało fragmentów.
