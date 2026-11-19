# Umiejętność `cs_statyka`: model obliczeniowy w CS-check

Część zestawu umiejętności z repozytorium CS (podział i decyzje: `profile\cs_program\README.md`). Zakres: model prętowy MES
z IFC, neutralny model `static` i magazyn wyników, obciążenie śniegiem i łuk obudowy, porównanie modelu fizycznego
z analitycznym, wyniki statyki 3D. Solverem jest RFEM (umiejętność `rfem`) albo Robot (`cs_robot`).

## Pliki
| Plik | Co to |
|---|---|
| `profil.json` | profil wiedzy, bez `chmura`, bez internetu |
| `system.md` | rola i 7 zasad, w tym granica „CS nie liczy” i jednostki (static w SI, core/mes w mm, N „+” = rozciąganie) |
| `karta.md` | ściąga: granica, model MES (role, węzły, flagi), static i magazyn, śnieg i obudowa, porównanie modeli, wyniki 3D, mapa „pytanie → plik” |
| `przyklady.jsonl` | 10 przykładów (9 wyszukiwań, 1 odmowa liczenia) |
| `testy.jsonl` | 6 testów CS01–CS06 |
| `zrodla.json` | 18 wzorców: glossary/obliczenia, obciazenia, obudowa, pitfalls/mes_model_obliczeniowy, 3 wykłady, 5 planów i raportów, help obciążeń, README mes, static, loads, model_compare, results3d |
| `wiedza/model_obliczeniowy_cs.md` | 10 sekcji streszczenia w gicie |

## Uwagi
- `src/features/mes/README.md` ma 144 KB, `results3d/README.md` 226 KB: wyszukiwarka dzieli sekcje powyżej 1500 znaków, model widzi 900 znaków fragmentu, dlatego karta i notatka mówią, w którym paragrafie doczytać.
- Plany łuków (MES, obudowa, solwer) są wyjątkiem od zasady „bez docs/plans”: zawierają reguły R- i odpowiedzi użytkownika, o które padają pytania.
