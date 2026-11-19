# Karta: rysunki w CS-check

## Układy (plane_view): rysunek całej płaszczyzny
- Układ = płaszczyzna konstrukcyjna z tym, co na niej stoi: elewacja na osi (rodzina X albo Y) albo rzut poziomu (Z); lista z modułu `axes_levels` (brak osi → uruchom moduł w Jobs). Wagi osi: główne, pomocnicze, graniczne; „Tylko wzorce grup”; osie użytkownika U (łamane przez wskazane punkty, arkusz = rozwinięcie). Kolumna Rys.: ✓ policzony, ⏳ w kolejce, — policzy się przy otwarciu.
- Rysunek = plaster: dwie równoległe płaszczyzny i patrzenie w głąb (ten sam rdzeń HLR co arkusz zespołu). Konwencja: oś X — patrzymy z −X, oś Y z −Y, poziom Z z góry (cięcie 50 mm nad rzędną); ściana z obudową po stronie dodatniej normalnej — z zewnątrz. Kontrakt `plane_v1`, cache i świeżość.
- Zawartość: łańcuchy wymiarów osi, bąble osi, rzędne poziomów, opisy elementów (znak zespołu z pliku albo numer CS), przekroje, mini-keyplan; PDF kompletu („Rysunki zakresu do PDF”: okładka, A3–A1, tabliczka). Szkice piórem (deska kreślarska, kotwiczone w elementach po GUID, wersjonowane) → „Wyślij do agenta” → w Claude Code `/szkic`; agent buduje to, co projektant narysował. Wykresy N/Vy/Vz/T/My/Mz na osiach prętów (źródło DEMO/Robot/RFEM), deformacje `deform_v1`, ramka detalu.

## Co montażysta czyta z arkusza (wykład 2026-11-02, reguły UL-01…UL-44; bilans: jest 6, częściowo 14, brak 24)
Komplet: dwie izometrie całej konstrukcji na początek (z dwóch stron, z opisami pozycji) — brak; każda oś ma rysunek, identyczne osie jeden rysunek; rzut poziomu 0 do kontroli zakotwień, rozstaw kotew na detalach z góry, podlewka z boku — brak; rzut dachu ortogonalny z góry w całym zakresie spadku (dziś zakres błędny), rzut na płatwie i pasy kratownic; elewacje od zewnątrz, cały zakres od obudowy do końca słupa (obudowa domyślnie niewidoczna), otwory bram i okien — brak; wymiary na rzucie: głównie osie i osie belek poza osiami; czarno-białe (częściowo: wypełnienia i bąble kolorowe); pismo mniejsze; domyślnie bez profili przy znakach zespołu, profil w zestawieniu elementów wysyłkowych z gabarytem i masą; detale węzłów na osobnych rysunkach w izometrii, cienkie kółko raz na arkusz, jeden detal dla jednakowych węzłów; spoiny domyślnie wyłączone; śruby wg zasad UK1–UK3.

## Arkusze zespołów (assembly_view) i rysunki części (part_view)
- Assembly: viewer 3D zespołu (GLB) + arkusz 2D; cache dwupoziomowy i pre-generacja, pula liczenia (W-01); ustawienia rysunku (silnik), spoina zmieniona z arkusza, klasa spoiny a zawór przekrojów, wymiary z blach bez lidera, numery Tekli z atrybutu `Tag`, położenie każdej części i śruby dla wytwórni. Wykład 2026-10-13: wymiary jedną wspólną linią narastającą (nie osobna linia na blachę), wymiary i opisy pozycji i spoin jako jeden układ, izometria na każdym rysunku, wspólny prefiks pozycji raz nad tabelką, wymiarowanie do osi albo do elementu (ustawienie), osie elementów kratownic widoczne, styl domyślny + per projekt.
- Części (part_view): lista pozycji (znak Tekli albo klucz CS, ilość, liczności w zespołach), rysunek części w ramie DSTV (pręt: ściana `v` jako widok główny, `o` i `u` tylko z cechą wyłączną, izometria; blacha: lico + izometria), otwory zawsze z osi śrub (fasolki z brył otworów), masa netto i brutto profilu wyjściowego, zestawienie stali (CSV/XLSX/PDF), arkusze zbiorcze wg podobieństwa, reguły `part-default-v1`. API `GET /api/parts…`.

## NC DSTV (nc_dstv): prototyp NIE DO PRODUKCJI
Generator, parser, walidator (V1–V18, VX1–VX4; plik z błędem nie powstaje), diff semantyczny, dialekty (`tekla_ods_bfo` domyślny, zmierzony na korpusie ODS: 284/433 par identycznych; `tekla_ods_si`), profile maszyn warsztatu (N4), paczka NC z przewodnikiem i ankietą; fasolki, pogłębienia pod łby stożkowe (DIN 7991), cięcie wzdłużne, blachy gięte. CLI `tools/nc_dstv.py export | check | diff | scan | render | porownaj | paczka`. Rysunek części i NC mają ten sam układ (NC-R3), więc rysunek jest kontrolą NC. Plan testów przed produkcją: PLAN_TESTOW_NC.

## Nesting
Nesting 2D blach (kontury na arkuszach, metoda B) i 1D prętów (sztangi handlowe); eksport `nesting-dxf` / `nesting1d-dxf` z zakładek toolkitu; wynik zasila tryb Produkcyjny kosztów.

## Gdzie szukać (repo CS)
| Pytanie o… | Plik |
|---|---|
| zakładka Układy: wybór, rysunek, szkice, wykresy | docs/help/uklady_*.md; src/features/plane_view/README.md |
| co ma być na arkuszu montażowym | WYKLAD_UKLADY_CO_CZYTA_KONSTRUKTOR (reguły UL); PLAN_UKLADY_MONTAZOWE |
| zespoły, wymiary, opisy, spoiny na arkuszu | src/features/assembly_view/README.md; WYKLAD_RYSUNKI_WARSZTATOWE_ZESPOLY_CZESCI_NC; RAPORT_ROZPOZNANIE_RYSUNKI_WARSZTATOWE |
| części, zestawienie stali, masa | src/features/part_view/README.md |
| NC, dialekty, walidacja, paczka | src/features/nc_dstv/README.md; docs/how-to/ANKIETA_WARSZTAT_NC.md |
| nesting | src/features/nesting/README.md |
| szkice i osie użytkownika (słownik) | glossary/osie_uklady.md §22 (w `cs_konstrukcja`) |
