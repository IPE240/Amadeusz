# IFC w CS-check: import, geometria, jednostki, nazwy profili, psety eksportu

## Jak CS wczytuje IFC: dwa wejścia (geometria IfcOpenShell, metadane web-ifc), trzy przebiegi, file_hash
Import ma dwa niezależne wejścia. Geometria (osie, placement, BREP, operacje boolowskie CSG) zawsze idzie przez
IfcOpenShell-Python. Metadane (psety, materiał, objętość, nazwa profilu, znak zespołu) idą szybką ścieżką manifestu
web-ifc-node (BFO 7342 elementów: 0,7 s zamiast 11,6 s; MWS 141 186 elementów: 12 s zamiast 246 s), gdy manifest jest
(`CS_USE_WEBIFC=1` albo dostarczony z viewera), inaczej zwykłym przebiegiem IFC. Manifest nie pokrywa geometrii.
Orkiestrator `ExtractionService.process_ifc_file` robi trzy przebiegi: metadane modelu → elementy i właściwości → zapis
hurtowy do `core.model`, `core.element`, `core.property` (i `core.bolt_group`). Tożsamość modelu = SHA-256 pliku
(`file_hash`, `uq_model_file_hash`): ten sam plik wczytany drugi raz nie tworzy nowej wersji. Relacje spoin i śrub autora
modelu (`IfcRelConnectsWithRealizingElements`) trafiają do `core.weld_relation` (Tekla przy `Welds:On`, Advance Steel zawsze).

## Bliźniak analityczny zamiast siatki: core.pret (profil + oś + cięcia), core.blacha (płaszczyzna + kontur + grubość)
CS jest analitycznym bliźniakiem CSG-first: podstawową reprezentacją części stalowej jest zapis parametryczny, nie siatka.
Pręt (`core.pret`, moduł `pret_analytical`): kontur profilu z katalogu, lokalny układ 3×3, końce osi i `cuts_json` (cięcia
kształtujące końce); łuki `axis_kind='arc'`; unikaty `unique_element_id` / `unique_with_holes_id`; oś krótkiej rury
i pręta okrągłego z bryły siatkowej biegnie wzdłuż rury (reguła „okrągły obrys ⟂ lokalnego X”). Blacha (`core.blacha`,
moduł `blacha_geometry`): płaszczyzna (początek + ramka) + kontur 2D + grubość; otwory z osi śrub (siatki są ślepe na otwory).
Siatka to zapas (Tier 3) tylko wtedy, gdy ścieżka parametryczna nie rozwiąże kształtu. Blachownica z trzech blach (dwa pasy
+ środnik), króciec i profil krzyżowy składają się w zastępczy pręt (`core.pret_wirtualny`), który uczestniczy w osiach
i modelu obliczeniowym jak pręt z pliku. Krata pomostowa to produkt kupowany (`core.krata`, kierunek nośny = dana
bezpieczeństwa, symbol katalogowy w `klasa_p4`).

## Jednostki w CS i w programach zewnętrznych: IFC mm, baza mm z wyjątkami w metrach, Robot RFEM IDEA m
Zasada: obliczenia wewnętrzne w metrach, baza w mm, wektory kierunków znormalizowane. Wyjątki w metrach: `core.blacha.cs_origin_*`
(a `vertices_2d_json`, `obb_*`, `thickness_mm` tej samej blachy w mm), `core.bolt_axis`, AABB brył (`core.geometry_shape.bbox_*`),
`analysis.strefa_polaczenia.cs_origin_*`; `core.krata.cs_origin_*` w mm świata; `core.pret.axis_*` w mm. IFC z Tekli deklaruje mm
w `IFCUNITASSIGNMENT` (czytaj deklarację). Programy zewnętrzne pracują w metrach: IDEA StatiCa (IOM), Autodesk Robot (COM;
`robot_pull.py` mnoży ×1000), RFEM 6 (API II i `model.db` w SI: m, N, Pa, rad, K), RFEM 5 (RF-COM; z dokumentacji, niezmierzone).
Skutki pomyłki: szukanie blach w promieniu 700 mm od końca pręta zwracało zero wierszy (mm kontra m); puste wyniki nie krzyczą.
Kod: `src/core/units.py` (`APLIKACJE`, `jednostka_pola`, `przelicz_punkt`), bramka `python tools/jednostki_audyt.py`
(obrys grupy kolumn kontra obrys modelu, rozjazd 1000× wychodzi natychmiast).

## Nazwy profili w CS: skróty klasa_p3, zapis klasa_p4, separatory * x spacja, Revit, Advance Steel, Tekla
Skrót → pełna nazwa: IPE, HEA, HEB, HEM (dwuteowniki EN 10365), HL (Jumbo, `HL1000*642`, `HL1100B`; Revit `HL 1000x642`),
HE (zapis masą, `HE600*399`), UPN (Revit PL `C220`), UPE, L (kątownik równoramienny `L50*5`), LU (nierównoramienny),
LZG (kątownik zimnogięty, parametryczny), SIGMA (płatew zimnogięta, `SIGMA350*65*2`; Kingspan `MBEAM-M350065200`), T,
`0.5-IPE240` (połówka dwuteownika, `klasa_p3 = T`), RHS (`RHS100*60*5`), SHS (`SHS100*5`; Revit PL `50x50x4 SHS`), CHS
(`CHS76.1*4`; IDEA `CHS(cf)193.7/6.0` = adnotacja, nie wariant), CFRHS, D (pręt okrągły pełny `D25`; Revit `RB 25`, AS `RD20`),
BL (blacha `BL10` = grubość), RP (blacha prostokątna `RP200*100*10`), WTA/WTB/WTC/WTS (blachownica falista), M (śruba `M16x80`),
NUT. Separatory `*`, `x` i spacja są równoważne i normalizowane do `*` (`profile_matcher.normalize_profile_name()`).
`brak_w_bazie` = kształt rozpoznany, rozmiar spoza katalogu (celowe). Katalog `cs_catalog.profile_catalog` ma 4559 wierszy
w konwencji Tekli; znaczenie kolumn (wymiary, grubości, zaokrąglenia, pole, momenty bezwładności, spadek półek INP/UPN):
handbook/catalog_columns.md.

## Blachownica falista WT (SIN, ZEKON) i blachownica dwuteowa HI/WI: zapis, grubość środnika, masa
WT to rodzina parametryczna bez wierszy katalogu: zapis kanoniczny `WT{L}{hw}/{bf}*{tf}` (np. `WTC1250/350*25`, pasy symetryczne)
albo `WT{L}{hw}/{bf1}*{tf1}/{bf2}*{tf2}` (asymetryczne, Robot WTS). Grubość środnika pochodzi WYŁĄCZNIE z litery: A = 2,0,
B = 2,5, C = 3,0, S = 2,5 mm; kontury Tekli rysują środnik ×1,2, więc nigdy nie czytać tw z geometrii. Fala: długość 155 mm,
amplituda 20 mm (A/B/S) albo 21,5 mm (C); stosunek rozwinięcia s/w = 1,1482 / 1,1690. Pole do masy A = Σ bf·tf + hw·tw·(s/w).
Zapis Robota `WTB1000-300x20` jest przyjmowany i normalizowany. Eksport IFC rysuje falisty środnik domyślnie (`wt_sin=false`
wyłącza) z psetem `CS_SIN`; `tools/wt_podmien_srodnik.py --ifc MODEL.ifc` podmienia środniki w oryginalnym pliku bez bazy.
Blachownica dwuteowa spawana: `klasa_p2 = BLACHOWNICA DWUTEOWA`, `klasa_p3` HI/WI, zapis `HI{h}-{tw}-{tf}*{b}`; z programu
Hala także `BL{h}*{b}t{tf}/{tw}` (reguła `BL_blachownica_hali_na_HI`, tylko program `hala`; `BL10*200` z Tekli zostaje blachą).

## Eksport wzbogaconego IFC: compose-ifc, psety CS_*, profil user i full, GlobalId, strumień do viewera
Eksport `compose-ifc` składa nowy plik IFC z bazy (geometria CSG-first z zapasem siatki, deduplikacja kształtów) i dokleja psety
`CS_*`. Lista psetów nie jest ręczna: introspekcja tabel po kluczach obcych buduje rejestr, ten sam dla eksportu i dla strumienia
do viewera (`GET /api/models/{id}/element-data?profile=user`, segment CS panelu Properties). Profil `user` (domyślny, po
przeglądzie 525 właściwości 2026-08-11): klasyfikacje i tożsamość, unikaty, kody połączeń i zestawów, klasyfikacja blach,
grubości, pola, objętości, koszty jednostkowe zespołu; zero geometrii w psetach (osie, otwory, ramki niesie bryła) i zero
duplikatów (profil tylko w `klasa_p4`). Profil `full`: wszystko (debug, maszynowo); przełącznik `pset_profile`, env `CS_PSET_PROFILE`.
`guid_norm` nie jedzie w psecie, bo jest równy `GlobalId` produktu (sprawdzone na KOT: 28 186/28 186). Spoiny nie są w eksporcie
standardowym: jadą jako osobna nakładka (`/api/exports/ifc/shared-edges`, domyślnie `bare=1`, plik `{model}_spoiny.ifc`), którą
ogląda się nałożoną na eksport modelu; `&bare=0` = spoiny na tle pełnego modelu (diagnostyka). Strefy połączeń i osie połączeń
to także nakładki. Pułapka: tabela z dwoma kluczami do elementu wypada z rejestru jako relacja (`_FORCE_DIRECT`).

## Narzędzia IFC: dopisz psety z tabeli, spis IFC z dysku, csx, DXF osi i blach, walidacja, cięcie paneli, gięcie prętów
`python tools/ifc_dopisz_psety.py` dopisuje psety do KOPII pliku IFC z tabeli (JSON, CSV albo inny IFC jako dawca) po kluczu
z elementu, np. numerze pręta RFEM; GUID-y i bajty pliku zostają (doklejka). `tools/ifc_spis` robi spis plików IFC z dysku
biura (program źródłowy, data, teczka projektu, materiały z objętością i masą, zakres, dyscyplina, poziom szczegółowości,
miniatura SVG) do SQLite i HTML; 115 plików / 3,5 GB w kilkanaście minut; docelowo wyniki w bazie CDE. `tools/cs_pack.py`
pakuje cały model do przenośnego `.csx`. DXF: `tools/export_bar_axes_to_dxf.py` (osie prętów), `tools/export_bar_local_cs_to_dxf.py`
(lokalne układy), `tools/export_blacha_to_dxf.py` (kontury blach), `tools/export_brep_to_dxf.py`. `tools/validate_ifc.py`,
`tools/export_ifc_by_class.py`. Zabawy geometrią: `tools/podziel_panele_na_kwadraty.py MODEL.ifc --bok 1` (panele obudowy na
kwadraty), `tools/zegnij_prety_na_segmenty.py MODEL.ifc --strzalka 1:20` (gięcie prętów w łuk z segmentów, kontrakt
`handbook/kontrakt_giecia_pretow.md`). Walidacji IFC (IDS, clash) CS nie robi: `ifcopenshell.validate`, Solibri, BIMvision.

## Układ współrzędnych i konwencje: placement, ramki 3×3, direction_class, klasy IFC a klasa_p1
Transformacje świata składają łańcuch `IfcLocalPlacement` od korzenia do obiektu w jedną macierz 4×4
(`compose_object_placement`). Ramki analityczne (`core.pret.cs_*`, `core.blacha.cs_axis_*`) to ortonormalne macierze 3×3 zapisane
jako 9 liczb; punkty `core.shape_cut_edge` są lokalne dla kształtu (stosuj `element_placement.transform_4x4`). Kierunek elementu:
`direction_class` (`pionowe_z`, `poziome_xy`, `diagonalne_xyz`…) i `direction_category`. Klasy IFC decydują o części klasyfikacji:
`IfcWall`, `IfcSlab`, `IfcFooting` zawsze `POWIERZCHNIE`; `IfcCovering` tylko w modelach z RFEM (w IFC z Tekli to znacznik powierzchni
niemalowanej, nigdy nie przejmowany); `IfcPlate` bez prefiksu opisu = `UZBROJENIE`/`BLACHA`; pokrycia `IfcCovering` nie należą do osi
ani płaszczyzn. Beton z Revita → `POWIERZCHNIE` (flaga `CS_BETON_POWIERZCHNIE=global`).
