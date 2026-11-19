# Umiejętność `cs_ifc`: IFC w CS-check

Część zestawu umiejętności z repozytorium CS (podział i decyzje: `profile\cs_program\README.md`). Zakres: jak CS czyta IFC
(IfcOpenShell i web-ifc), bliźniak analityczny pręta i blachy, jednostki (najczęstsze źródło pomyłek), nazwy i katalog profili,
psety eksportu wzbogaconego IFC, narzędzia (dopisywanie psetów, spis IFC, DXF, `.csx`).

## Pliki
| Plik | Co to |
|---|---|
| `profil.json` | profil wiedzy; `internet: true` (publiczna dokumentacja buildingSMART, IfcOpenShell, producentów CAD), bez `chmura` |
| `system.md` | rola i 7 zasad, w tym obowiązek podawania jednostek |
| `karta.md` | ściąga: ekstrakcja, bliźniak analityczny, jednostki, skróty profili, psety, narzędzia, mapa „pytanie → plik” |
| `przyklady.jsonl` | 10 przykładów (9 wyszukiwań, 1 kalkulator) |
| `testy.jsonl` | 6 testów CI01–CI06 |
| `zrodla.json` | 25 wzorców: docs/reference (IFC_GEOMETRY, PSET_SEMANTICS, UNITS, SCHEMA_REFERENCE), how-to/IFC_DOPISZ_PSETY, IFCOPENSHELL_USAGE_GUIDE, handbook (web-ifc, profile_naming, catalog_columns, glossary/geometria), README modułów pret, blacha, pret_wirtualny, krata, export, tools/ifc_spis |
| `wiedza/ifc_w_cs.md` | 8 sekcji streszczenia w gicie |

## Uwagi
- Wykład o pre-analizie IFC (spis BUDOWY) ma w repo nazwę klienta zastąpioną kluczem projektu; `system.md` zakazuje podawania nazw klientów.
- `SCHEMA_REFERENCE.md` (38 KB) jest w źródłach, bo pytania „gdzie w bazie jest X” padają często; kolumny per tabela są generowane i nie są pobierane.
