# Umiejętność `cs_robot`: Autodesk Robot w CS-check

Część zestawu umiejętności z repozytorium CS (podział i decyzje: `profile\cs_program\README.md`). Zakres: sterowanie Robotem
przez COM z Pythona (fakty zmierzone na Robocie 2017 i 2025), odczyt modelu do CS, wytężenie z wymiarowania na modelu IFC,
siły do magazynu wyników, most MES → Robot w toolkicie, przeniesienie RFEM 6 → Robot. Robot 2017 w programach Marka
(COM, wymiarowanie EC3, format `.rtd`) zostaje w umiejętności `mkk`; obie można włączyć razem w profilu.

## Pliki
| Plik | Co to |
|---|---|
| `profil.json` | profil wiedzy; `internet: true` (publiczna dokumentacja Autodesk), bez `chmura` |
| `system.md` | rola i 7 zasad, w tym jednostki SI przy każdej liczbie i bezpieczeństwo pracy na kopii |
| `karta.md` | ściąga: COM, jednostki, przekroje/osie/podpory/kombinacje, tabela narzędzi CS, ograniczenia, mapa „pytanie → plik” |
| `przyklady.jsonl` | 10 przykładów (9 wyszukiwań, 1 odmowa z instrukcją) |
| `testy.jsonl` | 6 testów CB01–CB06 |
| `zrodla.json` | 8 wzorców: ROBOT_COM_API, ROBOT_DO_IFC_WYTEZENIE, pitfalls/programy_zewnetrzne, README mes_robot, rfem_to_robot (README i MAPOWANIE), jadro_pretowe, k0_eksperyment |
| `wiedza/robot_w_cs.md` | 8 sekcji streszczenia w gicie |

## Uwagi
- `handbook/pitfalls/programy_zewnetrzne.md` (122 KB) zawiera pułapki Robota, RFEM, IDEA i licencji; trafia tu, bo większość wpisów dotyczy Robota i COM; pułapki IDEA ↔ RFEM są w `cs_polaczenia`.
- Most MES → Robot nie był sprawdzony do końca na żywo (brak licencji 2026-10-23); `system.md` każe to mówić.
