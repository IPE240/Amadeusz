# Wiedza profilu „tekla”

Ten plik jest dla ludzi: model go nie widzi.

Tu leżą notatki, które asystent Tekli przeszukuje narzędziem `szukaj_wiedzy`: konstrukcje (podstawy słupów, kotwy,
głowice, stropy), zapis profili, poziomy i jednostki, ograniczenia mostu TeklaBridge.

- Nowa notatka: plik `.md`, jedna sekcja (pod nagłówkiem) to jedno zagadnienie, do ok. 900 znaków.
- Najważniejsze pojęcia, bez których model się myli, idą krótko do `karta.md`, a nie tutaj.
- Dokumentacja z repozytoriów (np. mostu TeklaBridge): przycisk „Źródła wiedzy…” w oknie asystenta. Trafia do
  `zrodla\`, którego nie edytuje się ręcznie.

Sprawdzenie, co znajdzie model: `LokalneAI.Eval szukaj --profil profile\tekla --fraza "kotwy M24"`.
Zasady pisania: `docs\wiedza-dla-modeli.md`.
