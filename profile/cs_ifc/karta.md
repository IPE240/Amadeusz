# Karta: IFC w CS-check

## Jak CS czyta IFC
- Dwa wejścia: geometria (osie, placement, BREP, CSG) zawsze przez IfcOpenShell-Python; metadane (psety, materiał, objętość, nazwa profilu, znak zespołu) przez szybki manifest web-ifc (15–20× szybciej) albo zwykły przebieg IFC. Manifest nie pokrywa geometrii.
- Reprezentacja = bliźniak analityczny, nie siatka: pręt `core.pret` (kontur profilu z katalogu + oś + cięcia końców `cuts_json`), blacha `core.blacha` (płaszczyzna + kontur 2D + grubość). Siatka to tylko zapas (Tier 3). Łuki: `axis_kind='arc'`.
- Klasyfikacja wstępna z `Pset_*Common.Reference` i materiału przez katalog `cs_catalog.profile_catalog` (4559 przekrojów, konwencja Tekli). Element bez prefiksu opisu = `klasa_p1 NULL`; prefiks spoza katalogu = `BRAK W BAZIE`; kształt znany, rozmiar nie = `klasa_p4 = brak_w_bazie` (celowe, nie błąd).
- Tożsamość modelu = SHA-256 pliku (`file_hash`); ten sam plik drugi raz nie tworzy nowej wersji.

## Jednostki (najczęstsza pomyłka)
- IFC (Tekla) deklaruje mm w `IFCUNITASSIGNMENT` (czytaj deklarację, nie zakładaj). Baza CS liczy wewnętrznie w metrach, zapisuje przeważnie w mm, ale `core.blacha.cs_origin_*`, `core.bolt_axis` i AABB brył są w metrach, `core.krata.cs_origin_*` w mm.
- Programy obliczeniowe pracują w metrach: Robot (COM), RFEM 6 (API II i `model.db`), IDEA (IOM), RFEM 5 (dokumentacja, niezmierzone). W kodzie CS: `src/core/units.py` (`APLIKACJE`, `jednostka_pola`, `przelicz`), bramka `tools/jednostki_audyt.py`. Puste wyniki zapytań = często rozjazd m/mm.

## Nazwy profili (skróty `klasa_p3` / `klasa_p4`)
IPE, HEA, HEB, HEM, HL, HE (masa), UPN, UPE, L, LU, LZG (gięty, parametryczny), SIGMA (płatew gięta), T, `0.5-IPE240` (połówka), RHS, SHS, CHS, CFRHS, D (pręt pełny), BL (blacha, `BL10`), RP (blacha prostokątna), WTA/WTB/WTC/WTS (blachownica falista SIN, `WTC1250/350*25`, grubość środnika tylko z litery), HI/WI (blachownica dwuteowa `HI{h}-{tw}-{tf}*{b}`, także zapis Hali `BL{h}*{b}t{tf}/{tw}`), HK (skrzynkowa), M (śruba). Separatory `*`, `x`, spacja są równoważne (`RHS100*60*5` = `RHS100x60x5`); Revit pisze `50x50x4 SHS`, `HL 1000x642`.

## Eksport wzbogaconego IFC i psety
- `compose-ifc` składa nowy plik z bazy (geometria CSG-first, psety `CS_*`); profil `user` (domyślny) = klasyfikacje, tożsamość, unikaty, kody połączeń, klasyfikacja blach, koszty zespołu, bez geometrii w psetach i bez duplikatów; `full` = wszystko (debug). Spoiny jedzie osobna nakładka (`/api/exports/ifc/shared-edges`, `bare=1`), strefy połączeń też nakładka.
- `GlobalId` produktu = `guid_norm` bajt w bajt. Strumień do viewera `GET /api/models/{id}/element-data?profile=user` = ta sama mapa.
- Dopisanie psetów do KOPII pliku IFC z tabeli (JSON/CSV/inny IFC) po kluczu, np. numerze pręta RFEM, bez zmiany GUID-ów: `python tools/ifc_dopisz_psety.py` (docs/how-to/IFC_DOPISZ_PSETY.md).
- Paczka modelu `.csx` (`tools/cs_pack.py`): wszystkie tabele modelu w jednym pliku.

## Inne narzędzia IFC
`tools/ifc_spis` (spis plików IFC z dysku biura: program, data, materiały, masa, zakres, miniatura), `tools/validate_ifc.py`, `tools/export_ifc_by_class.py`, `tools/podziel_panele_na_kwadraty.py`, `tools/zegnij_prety_na_segmenty.py`, `tools/wt_podmien_srodnik.py` (falisty środnik WT w oryginalnym pliku).

## Gdzie szukać (repo CS)
| Pytanie o… | Plik |
|---|---|
| ekstrakcja, CSG-first, bliźniaki, układ współrzędnych | docs/reference/IFC_GEOMETRY.md |
| co niesie który pset, profile user/full | docs/reference/PSET_SEMANTICS.md |
| jednostki CS i programów | docs/reference/UNITS.md |
| skróty profili, WT, separatory | handbook/profile_naming.md; kolumny katalogu: handbook/catalog_columns.md |
| web-ifc a IfcOpenShell | handbook/web-ifc.md; docs/plans/IFCOPENSHELL_USAGE_GUIDE.md |
| oś pręta, kontur blachy, blachownice z blach, kraty | src/features/pret, blacha, pret_wirtualny, krata (README) |
| spis IFC z BUDOWY, poziomy skrótu L0–L3 | tools/ifc_spis/README.md, WYKLAD_PREANALIZA_IFC_SPIS_BUDOWY |
