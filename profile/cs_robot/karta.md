# Karta: Autodesk Robot w CS-check

## Połączenie przez COM (Python, pywin32)
- `wc.GetActiveObject("Robot.Application")` NIE działa (MK_E_UNAVAILABLE); działa `wc.Dispatch("Robot.Application")` — przy działającym Robocie zwraca istniejącą instancję (klasa MULTIPLEUSE). Bezpieczeństwo „nie uruchom drugiego Robota” z warunku na PROCES przed COM (`tools/robot_com.py`, `procesy_robota`). `Dispatch` w trakcie startu Robota uruchamia drugą, ukrytą kopię; Robot jako dziecko serwera CS ginie z nim (start przez `explorer.exe`).
- `Interactive = 0` przed operacjami hurtowymi, `1` na końcu; `Visible = 1`. `Nodes.Count` / `Bars.Count` nie istnieją (użyj `GetAll()`); `Project.SaveToFile` nie istnieje (`SaveAs`). Konsola: `sys.stdout.reconfigure(encoding="utf-8")` (nazwy z `—`, `→`, `³`).
- API addytywne 2017 ↔ 2025: DISPID-y wspólnych metod identyczne; pułapka = cache pywin32 (ta sama GUID i wersja tlb).

## Jednostki: API w SI, nie inżynierskie
E stali `2.1e11` Pa (nie 210000), gęstość `RO ≈ 77010` N/m³ (7850 × 9,81), długość m, siła N, moment Nm; `IRobotBar.Gamma` w STOPNIACH (RFEM `rotation_angle` w radianach: ×180/π). `robot_pull.py` mnoży ×1000 do mm.

## Przekroje, osie, podpory, kombinacje
- `LoadFromDBase(name)` nie zgłasza braku: sprawdź `sd.ShapeType != 0`. Katalog europejski ma POLSKIE nazwy rur (RK, RO, RP) — nazwy RFEM/IFC trzeba przekształcić (§3.1); przekroje NS parametryczne; złożone `2 C 300` bez wymiarów, ale do wyprowadzenia.
- Osie lokalne: CZYTAJ `IRobotBar.GetLocalMtx()` (9 liczb, wiersze e_x, e_y, e_z w globalnym), nigdy nie wyprowadzaj z Gamma (468 z 1855 prętów KOT błędnie); osie należą do biegu, migawka w `static_run`. Siły i przemieszczenia Robota są w tych osiach.
- DOF odwrócone: `IRobotNodeSupportData.UX…` niezerowe = ZABLOKOWANE, `IRobotBarReleaseData…` niezerowe = ZWOLNIONE; flagi wracają jako `-1` (VARIANT_TRUE) — testuj `bool(flag)`, nigdy `== 1`. Sprężyny: `KX/KY/KZ` (N/m), `HX/HY/HZ` (Nm/rad).
- `Cases.CreateCombination` 3. argument to `I_CBT_*` (`IRobotCombinationType`), nie `I_CT_*`; wartości kolidują na 1. Grupy: `Structure.Groups` (3. argument `Create` to STRING). Panele: pełna sekwencja aktywacji (§7). Ciężar własny: wymagana flaga (§11). Obciążenia ruchome przez COM nie działają (MKK).

## Narzędzia CS
| Narzędzie | Co robi |
|---|---|
| `python tools/robot_pull.py --projekt 1095_WSP --do-core` | czyta otwarty model (tylko odczyt) do `static.*`, z `--do-core` do `core.*` i klasyfikuje; wypisuje `core id`; `--sprawdz-tylko` |
| `python tools/robot_wytezenie.py --wyjscie wytezenie.json` | wytężenie z modułu wymiarowania stali (~3 min / 2000 prętów); raz ręcznie w Robocie: Weryfikacja prętów → Pręty `all` → Obliczenia (lista prętów modułu nie da się ustawić z zewnątrz); przy rozjeździe przerywa bez pliku |
| `python tools/dopasuj_model_obliczeniowy.py --obliczeniowy 72 --fizyczny 65 --zapisz --powiaz-prety --wytezenie wytezenie.json` | przesunięcie układów, pręt↔pręt, symbol splotu, wytężenie na oba modele; `--eksport-para` = dwa IFC do oglądu |
| `python tools/robot_sily.py --core-id 114` | siły przywęzłowe w stacjach (2/3/7/11 wg długości), przemieszczenia, reakcje → `static.static_run`; wykresy na Układach (źródło Robot) |
| zakładka MES → Robot (toolkit) | Model → Most (lista kontrolna) → Wyślij (kontrola bez Robota, bramka mechanizmu; odcisk → kopia `SaveAs` → `New` → buduj → licz → zapisz → powrót) → Wyniki (ΣF reakcji, przemieszczenia, obwiednie N/Vz/My/Mz); katalog kopii `CS_MES_ROBOT_KOPIE` |
| `tools/rfem_to_robot/convert.py` | RFEM 6 → Robot 2025 (węzły, pręty, przekroje, materiały, podpory, przeguby, obciążenia, kombinacje); STARY klient SOAP, osobna instalacja, nierozwijane; mapowanie encji w MAPOWANIE.md |

## Ograniczenia
Wytężenie to zdjęcie stanu (pobrać po każdej zmianie); jedno sztywne przesunięcie nie nałoży modeli z osią na górze belki (`40 + h/2`); model powłokowy nie przechodzi (`static` bez powierzchni); Robot bez licencji = nie sprawdzono na żywo budowy, obliczeń i powrotu. Kopiowanie fragmentu żywego modelu zrobiono raz ad hoc (nie w `tools/`).

## Gdzie szukać (repo CS)
| Pytanie o… | Plik |
|---|---|
| fakty i pułapki COM (wersje, jednostki, katalog, osie, DOF, kombinacje, panele, grupy, obciążenia) | docs/reference/ROBOT_COM_API.md |
| wytężenie z Robota na IFC krok po kroku | docs/how-to/ROBOT_DO_IFC_WYTEZENIE.md |
| most MES → Robot, okna błędów, test na żywo | src/features/mes_robot/README.md |
| RFEM → Robot | tools/rfem_to_robot/README.md, MAPOWANIE.md |
| Robot 2017 w programach Marka (COM, wymiarowanie EC3, format .rtd) | umiejętność `mkk` |
