# Wiedza profilu „mkk”

Ten plik jest dla ludzi: model go nie widzi (wyszukiwarka pomija `CZYTAJ.md`).

- `hala.md`, `modelarz_wezlow.md`, `programy_zewnetrzne.md`: krótkie notatki w gicie (streszczenie i mapa), żeby profil
  działał także na komputerze, który jeszcze nie pobrał repozytorium MKK.
- `zrodla\MKK\`: pełna dokumentacja pobrana z repozytorium `Optimal-Design-of-Structures/MKK` (prywatne; `git` musi mieć
  dostęp). Pobiera ją przycisk **Aktualizuj wiedzę** w oknie asystenta albo `LokalneAI.Eval wiedza --profil profile\mkk`.
  Lista plików jest w `..\zrodla.json`. Folder `zrodla\` jest poza gitem i nie edytuje się go ręcznie: poprawki robi się
  w repozytorium MKK.

Co jeszcze warto dopisać (w repozytorium MKK, najlepiej w `lokalne-ai\wiedza\`, bo ten wzorzec już jest w `zrodla.json`):
typowe pytania użytkowników Hali i Modelarza z odpowiedziami, słownik pojęć (okładziny, ptaszki, podciąg, tężnik),
komunikaty błędów programów i co z nimi zrobić.

Dokumentacja MKK jest wewnętrzna i poufna (LICENCE w repozytorium), dlatego profil domyślnie pracuje tylko z modelami
lokalnymi (bez pola `chmura` w `profil.json`).
