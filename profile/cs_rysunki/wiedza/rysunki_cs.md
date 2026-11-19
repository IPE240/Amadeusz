# Rysunki w CS-check: Układy, arkusze zespołów, rysunki części, NC DSTV, nesting

## Układy: czym są, lista z axes_levels, wagi osi, osie użytkownika, kolumna Rys., strzałki
Układ to płaszczyzna konstrukcyjna razem z tym, co na niej stoi: elewacja na osi (rodzina X albo Y) albo rzut poziomu (Z).
Zakładka Układy rysuje cały układ naraz (całą ramę, całą ścianę, całe piętro): rysunek, po którym montażysta odnajduje się na
budowie („oś A”, „poziom +5,300”). Lista bierze się z modułu `axes_levels`; gdy model nie ma osi, uruchom ten moduł w Jobs.
Lewy panel: kierunek (X i Y elewacje, Z rzuty), waga osi (główne = rzędy słupów, pomocnicze = rygle, stężenia, podesty,
graniczne = oś na krawędzi, zwykle ściana szczytowa), „Tylko wzorce grup” (jedna oś na grupę powtarzalnych ram), U = osie
użytkownika (łamane przez wskazane punkty, np. podpora → słup → podpora; arkusz = rozwinięcie odcinków; zakłada skrypt albo
agent; bez wykresów i deformacji). Kolumna Rys.: ✓ arkusz policzony na serwerze, ⏳ czeka w kolejce „Generuj wszystkie”,
— policzy się przy otwarciu. Strzałki ← → przechodzą po liście. Pierwsze wejście liczy arkusz (pas przez halę trwa).

## Rysunek układu jako plaster: konwencja bazy, kierunek patrzenia, cięcie, kontrakt plane_v1, cache
Rysunek układu nie jest drugim silnikiem: to plaster — dwie równoległe płaszczyzny i patrzenie w głąb, liczone tym samym
rdzeniem HLR co arkusz zespołu (`assembly_view._section_payload`), z inną bazą, stacją i tożsamością elementów. Konwencja:
oś X — obserwator z −X, `up` = +Z; oś Y — z −Y; ściana z obudową po stronie dodatniej normalnej — obserwator z zewnątrz (lico
obudowy + 50 mm); poziom Z — z góry, cięcie 50 mm nad rzędną (lico „spód”: 50 mm nad najwyższą górą członków). Bliższa
płaszczyzna plastra = cięcie, dalsza = koniec okna (konwencja Tekli). `near`/`far` są względne wobec płaszczyzny w `h = 0`;
`flip` odwraca kierunek patrzenia (osobny wpis cache). Kontrakt `plane_v1` (tabela elementów, kontur wypełnienia, głębokość,
osie elementów); cache i świeżość per model; wydajność zmierzona 2026-08-22. Decyzje wcielone w kod i ogląd użytkownika
z 2026-08-22: src/features/plane_view/README.md.

## Co jest na arkuszu układu: wymiary osi, bąble, rzędne, opisy, przekroje, PDF kompletu, detale, wykresy, deformacje
Na arkuszu: łańcuchy wymiarów osi, bąble osi, rzędne poziomów, opisy elementów (znak zespołu z pliku albo z listy znaków,
inaczej numer CS; domyślnie wyłączone w zakładce), przekroje, mini-keyplan. PDF kompletu: „Rysunki zakresu do PDF” (okładka,
strona na arkusz, formaty A3–A1, tabliczka, keyplan); „główne” bierze osie główne, „wszystkie” także pomocnicze. Detale: ręczny
podgląd detalu z ramki (podgląd i SVG), bez arkuszy detali w PDF. Wykresy wielkości wewnętrznych na osiach prętów (prototyp
2026-09-17; pasek „Wykresy”, źródło DEMO / Robot / RFEM, przypadki bazowe + obwiednia, chip z numerem przebiegu), deformacje
na układzie (`deform_v1`, 2026-09-22), osie MES. Spoiny nigdy nie są liczone na arkuszach Układów. Wysokości pisma są
stałymi w kodzie (zakładka 2,5 mm × skala, PDF 1,8 mm, bąble 3,0 mm). Eksport do sceny viewera: okno Eksport → Układy → „Do sceny”
rysuje arkusze na swoim miejscu w modelu bez pliku.

## Szkice piórem na rysunkach układów: deska kreślarska, kotwiczenie w elementach, Wyślij do agenta, /szkic, produkty
Na rysunku układu można narysować piórem (tablet) uwagę albo polecenie: szkic jest kotwiczony w elementach po GUID
(`rola_kotwicy`), wersjonowany (`autor_wersji_szkicu`), ma rodzaj (`rodzaj_szkicu`) i prymitywy. Zakładka Układy → Szkice →
„Wyślij do agenta” tworzy pakiet dla agenta z polecenia projektanta; w Claude Code wpisujesz `/szkic`, a odpowiedź pojawia się
w panelu Szkice. Produkty ze szkicu: agent BUDUJE to, co projektant narysował. Słownik: glossary/osie_uklady.md §22
(`stan_elementu_na_rysunku`, `rodzaj_prymitywu_szkicu`). Wykład 2026-10-05 o dylatacjach: użytkownik chce oglądać wyniki
w Układach i elementy w assembly tylko dla jednego segmentu (zakres pracy).

## Co montażysta czyta z arkusza (wykład 2026-11-02): izometrie, poziom 0, dach, elewacje, wymiary, detale
Rysunek dla montażysty pokazuje elementy do zmontowania i sposób połączenia; może pokazywać detale (izometryczne, widoki,
przekroje). Komplet (reguły UL-01…UL-44): każda oś ma rysunek, identyczne osie opisuje jeden; dwie izometrie całej konstrukcji
z dwóch stron, z opisami pozycji, jako pierwszy rysunek kompletu (montażysta z izometrii wie, jak ma wyglądać konstrukcja);
rzut na poziom 0 do kontroli zakotwień, rozstaw kotew jednoznacznie na detalach w rzucie z góry, wysokość podlewki na detalu
z boku; rzut dachu ortogonalny z góry w całym zakresie od początku do końca spadku (dzisiejszy zakres jest błędny; rzut
w spadku tylko na żądanie), przy kratownicach osobny rzut na płatwie i pas górny, a pas dolny, gdy też w spadku; belki
podwieszone pod połacią w zakresie rzutu; widok ściany od obudowy do końca słupa (obudowa domyślnie niewidoczna), elewacje
od zewnątrz, na elewacji wymiary ryglówki (otwory w świetle) i otwory bram, okien, drzwi; zakres przekroju nie sztywny
(słup cały), szerokość pasma = oś i fragment przed i za słupem; wymiary na rzucie głównie osi i osi belek poza osiami, wymiary
kontrolne sprawdza się w BIM; rysunki czarno-białe, mniejsze pismo opisów, opisy elementów z Tekli albo CS, śruby wg zasad
UK1–UK3; domyślnie bez profili przy znakach zespołu (profil wiodący w zestawieniu elementów wysyłkowych z gabarytem i masą
całkowitą); detale węzłów na osobnych rysunkach w izometrii, spoiny domyślnie wyłączone, więcej detali niż mniej, jeden detal dla
jednakowych węzłów, cienkie kółko raz na arkusz. Bilans stanu w CS: jest 6, częściowo 14, brak 24 (szczegóły w tabeli wykładu).

## Arkusze zespołów (assembly_view): viewer zespołu, arkusz 2D, cache, ustawienia, spoina z arkusza, numery Tekli
Zakładka Assembly w toolkicie: szybkie przeglądanie pojedynczych zespołów wysyłkowych (GLB + sterowanie Trimble) i arkusz
rysunkowy 2D. Cache dwupoziomowy z pre-generacją (F1), pula liczenia zakładki (W-01). Ustawienia rysunku = parametry silnika
(runda 3); spoina zmieniona z arkusza (`work.override`, okienko spoiny); klasa spoiny a zawór przekrojów
(`DRAWING_ENGINE_VERSION = "13-zawor-arkusze"`); baza wymiarów arkusza z blach bez lidera; arkusz członka rodziny = jego własne
części, jedna etykieta = jedna pozycja; numery Tekli z atrybutu `Tag`; położenie każdej części i śruby dla wytwórni; potok
arkusza `_compute_sheet`; rdzeń HLR współdzielony z Układami. Weryfikacja wyłącznie oglądaniem; kryteria oglądu w README.
Eksport PDF zespołów; „Części członka rodziny na arkuszu zespołu” (Q-49). Wykład 2026-10-13 (zlecenie): linie wymiarowe
zgodnie z zasadami branżowymi, wspólna linia narastająca zamiast osobnej linii na każdą blachę, wymiary, opisy pozycji
i spoin powiązane, izometria na każdym rysunku; styl domyślny + własne zasady per projekt; nie wprowadzać niepotrzebnych
wymiarów (dla produkcji istotne powiązanie elementów w zespole).

## Rysunki części (part_view): pozycje, rama DSTV, widoki, otwory z osi śrub, masa, zestawienie stali, arkusze zbiorcze
Część (pozycja) = jedna sztuka: lista pozycji modelu (znak Tekli albo klucz CS, ilość, liczności w zespołach; sztuki o różnym
rodzaju otworów `-F1`, pogłębieniach `-P1`), numery Tekli z atrybutu IFC `Tag`. Rysunek części leży w ramie części DSTV
(wartość od lewego końca = `x` pliku NC), więc rysunek jest kontrolą NC. Pręt: widok główny = ściana `v`, widoki `o` (z góry)
i `u` (z dołu) tylko z cechą wyłączną, izometria (kamera 30°/20°); blacha: lico (`x` = dłuższy bok) + izometria. Geometria
z siatki IFC, gdy gabaryt = gabaryt materiału, inaczej bliźniak analityczny albo obrys × grubość. Otwory zawsze z osi śrub
(siatki ślepe na otwory); rodzaj (okrągły / fasolka) i Ø z brył otworów. Długość pręta = zakres analityczny (wyjątek z siatki
przy niezgodności, NC wstrzymany). Ostrzeżenia `otwory_nachodza`, `dlugosc_z_siatki`. Masa netto (przekrój „z wyokrągleniami”)
i brutto profilu wyjściowego; zestawienie stali pozycji wg profili (CSV/XLSX/PDF, `GET /api/parts/zestawienie`). Arkusze
zbiorcze części wg podobieństwa (blachy → profile DSTV). Reguły wymiarów `part-default-v1`; wymiarowanie do osi albo do
elementu (ustawienie), spód zawsze po lewej, izometria dla położonego słupa; wspólny prefiks pozycji raz nad tabelką.

## Pliki NC DSTV (nc_dstv): prototyp, dialekty, walidator, diff, pogłębienia, profile maszyn, paczka dla warsztatu
Generator, parser, walidator i diff plików NC (DSTV NC1) części — prototyp (2026-10-13) NIE DO PRODUKCJI do zaliczenia planu
testów (PLAN_TESTOW_NC); każdy plik niesie komentarz `** PROTOTYP CS - NIE DO PRODUKCJI` (walidator VX4). Dialekt domyślny
`tekla_ods_bfo` zmierzony na korpusie ODS (porównanie z 475 plikami NC Tekli BFO: 284/433 par identycznych, 86 w tolerancji;
KOT 349 i WSP 198 → dialekt z sygnaturą `tekla_ods_si`); `tekla_compat` = wcześniejsza hipoteza. Moduły: `model.py` (nagłówek
ST 24 pola, BO, AK/IK z łukami i fazami, SI, KO/PU, KA), `writer.py` (zapis deterministyczny, ASCII, CRLF), `parser.py` (ścisły
i tolerancyjny), `validator.py` (V1–V18, VX1–VX4; plik z błędem nie powstaje), `builder.py` (blacha B, I/U/L/M na każdej
ścianie, RO = rozwinięcie, straż siatki), `diff.py` (identyczne / tolerancja / ogląd / błąd, konwencje ramy), `render.py`
(podgląd PNG, porównanie dwóch plików), `countersink.py` (pogłębienia pod łby stożkowe DIN 7991), `machine_profiles.py`
(profil maszyny warsztatu N4: nakładka na dialekt, ankieta), `paczka.py` (zip `.nc1` z przewodnikiem `_PRZEWODNIK_CS.md`).
CLI: `tools/nc_dstv.py export | check | diff | scan | render | porownaj | paczka` (`--machine-profile`, `--fasolki`).
Podgląd NC w panelu części; pliki części do fazowania krawędzi dla spoin — kolejny etap po spoinach.

## Nesting 2D blach i 1D prętów: arkusze, sztangi, eksport DXF, koszty produkcyjne
Nesting 2D rozmieszcza kontury blach na arkuszach (minimalizacja odpadu; metoda B, wybór użytkownika 2026-07-11); nesting 1D
prętów na sztangach handlowych (`oned/`). Eksporty `nesting-dxf` / `nesting1d-dxf` z zakładek Nesting 1D/2D w toolkicie.
Wynik zasila tryb Produkcyjny modułu kosztów (brutto prętów z rozkroju 1D, brutto blach z wykorzystania arkuszy 2D).
Uwagi domenowe i otwarte follow-upy: src/features/nesting/README.md. Statystyki rysunków (`drawing_stats`) i ustawienia
rysunku (`drawing_settings`) mają własne README.
