# Klasyfikacja konstrukcji w CS-check: klasy, role, typy zespołów, lider, osie, poziomy, obiekty

## klasa_p1: PRĘT, UZBROJENIE, ŁĄCZNIKI, SPOINY, OKŁADZINA, POWIERZCHNIE, NIEKONSTRUKCYJNE, BRAK W BAZIE, NULL
Najwyższy poziom klasyfikacji każdego elementu IFC (`core.element.klasa_p1`, moduł cs1). Pochodzi z `Pset_*Common.Reference`
profilu i nazwy materiału dopasowanych przez katalog. Wartości: `PRĘT` (belki, słupy, stężenia, profile walcowane),
`UZBROJENIE` (element płaski, blacha, zwykle `klasa_p2='BLACHA'`), `ŁĄCZNIKI` (śruby, kotwy, nakrętki, `IfcMechanicalFastener`),
`SPOINY`, `OKŁADZINA` (kratki), `POWIERZCHNIE` (membrany i powłoki z RFEM, ściany, stropy, stopy z IFC klienta: `IfcWall`,
`IfcSlab`, `IfcFooting`), `NIEKONSTRUKCYJNE` (pomocnicze), `BRAK W BAZIE` (prefiks opisu jest, ale katalog go nie zna; wyjątek:
bryła jednej płaskiej płyty stałej grubości ≤ 60 mm → BLACHA, reguła `blacha_z_bryly`), `NULL` (pusty prefiks opisu, którego
nie przejęła żadna reguła po klasie IFC; nie znaczy „nie ma w katalogu”). Dla inżyniera: PRĘT niesie siły, UZBROJENIE jest
interfejsem między prętami. Wzorzec BFO: PRĘT 501, UZBROJENIE 1733, ŁĄCZNIKI 885, SPOINY 3649, NULL 118.

## klasa_p2, klasa_p3, klasa_p4: podkategoria, kształt, rozmiar; brak_w_bazie; kiedy sprawdzić ręcznie
`klasa_p2` to podkategoria: dla PRĘT — `WALCOWANE` (HEA, HEB, IPE, UPN, L…), `GIĘTE OTWARTE` (LZG, SIGMA), `ZAMKNIĘTE` (CHS, RHS,
SHS), `PEŁNE` (D, SQ, REC), `BLACHOWNICA DWUTEOWA` (HI/WI, także zapis Hali `BL…t…/…`), `BLACHOWNICA SKRZYNKOWA` (HK), `INNE`
(T, połówka `0.5-IPE240`); dla UZBROJENIE — `BLACHA`, `ELEMENTY ŁĄCZĄCE`; dla ŁĄCZNIKI — `ŚRUBY`, `KOTWY`, `OTWORY` (sam wzór
otworów bez łącznika, `klasa_p4 = D<Ø>`); dla OKŁADZINA — `KRATKI`; dla NIEKONSTRUKCYJNE — `POMOCNICZE`. `klasa_p3` = skrót
kształtu (BL, IPE, HEA, RHS, CHS, M, NUT…), `klasa_p4` = identyfikator profilu z katalogu (`HEA100`, `RHS100*60*5`, `BL10`) albo
`brak_w_bazie` (kształt rozpoznany, rozmiar poza katalogiem: świadomy wynik, nie śmieć). Ufaj `klasa_p4`, gdy
`matched_profile_name` nie jest puste. Sprawdź ręcznie, gdy `klasa_p1 = NULL`: profil niestandardowy, literówka w Reference
(alias w katalogu, handbook/profile_naming.md) albo element nie jest stalą.

## obiekt_konstrukcyjny: rola pręta wewnątrz zespołu (leading_element, dlugi_pret, krociec, poprzeczka, skratowanie…)
Klasyfikacja na poziomie elementu (`analysis.cs2_element_analysis.obiekt_konstrukcyjny`), tylko dla `klasa_p1 = PRĘT`. Każdy
zespół ma jeden `leading_element` (element główny, odniesienie dla reszty) i pręty wspierające: `dlugi_pret` (drugi długi,
równoległy, co najmniej 0,4 długości głównego), `krociec` (krótki pręt, króciec, wspornik), `poprzeczka` (prostopadły, ≥ 60°,
np. belka między słupkami balustrady), `przedluzenie` (współliniowe przedłużenie), `skratowanie` (ukośny, 20–60°, krzyżulec
kratownicy albo stężenia), `pochwyt` (pozioma poręcz), `slupek` (pionowy słupek balustrady), `kolano` (pręt gięty), `inne`
(bez dopasowania; sprawdź ręcznie, gdy większość prętów zespołu ma `inne`). Role balustrady nadpisują pozostałe. Liczniki
ról (`pochwyt_count`, `skratowanie_count`…) są w `cs2_summary`. Pisownia polska jest święta (nie anglicyzować).
Stan 2026-10: rolę ma tylko część elementów (BFO 7,6 %, KOT 24 %, WSP 22 %); „inne” to worek: 13–29 % na modelach testowych,
68–92 % na projektach biura (pręt pod kątem do głównego bez drugiego równoległego). Decyzje w toku: typ „zakotwienie”, rola
„przeciąg”, typ „schody”, atrapa DUMMY bez typu.

## element_konstrukcyjny: typ całego zespołu (slup, belka, rygiel, ryglowka, kratownica, stezenia, drabina, balustrada, kraty)
Klasyfikacja całego zespołu (`analysis.cs2_summary.element_konstrukcyjny`) z punktacji ról członków i geometrii; punkty per
kategoria w `scores_json`. Nośne: `slup` (pionowy pręt nośny; `is_slup_osi` na przecięciu osi X i Y ma twardy priorytet),
`belka` (poziomy nośny dachu lub stropu: lider w ścianie zewnętrznej i w dachu albo na poziomie konstrukcyjnym), `rygiel`
(poziomy drugorzędny), `kratownica` (lider + ≥ 1 długi pręt + próg liczby skratowań i poprzeczek; gdy spełniony, wygrywa),
`stezenie_pionowe` (lider ukośny w XZ/YZ poza dachem), `stezenie_poziome` (kierunek `poziome_xy`), `stezenie_polaciowe`
(`diagonalne_xyz`). Drugorzędne: `ryglowka` (rygiel na ścianie zewnętrznej z pionowym sąsiadem; pisownia bez ó), `balustrada`
(pochwyt + słupki + wypełnienie), `drabina` (dwa równoległe pionowe pręty + szczeble co regularny odstęp; wygrywa nad balustradą),
`krata_pomostowa` i `krata_schodowa` (TWARDA reguła, nie punktacja: zespół z kratą to dokładnie jedna krata + blachy burtowe
+ śruby; bez niej WSP etykietowało 1548 krat jako `slup`, bo osią kraty jest jej grubość), `NULL` (żaden próg nie przeszedł;
także lider będący blachą liczoną grubością albo łącznikiem). Typ zależy od globalnych osi X/Y/Z: w modelach obróconych
w rzucie prawie nie ma belek. Role `drabina`, `balustrada`, `krata_*` są wykluczone z modelu MES (R19).

## Zespół (assembly) i element wiodący: reguła lidera, grupy identyczne, wzorce, zestawy końców, ALCS
Zespół montażowy (`core.assembly`) = elementy dzielące `assembly_guid` z IFC (`IfcRelAggregates`); `assembly_mark` z właściwości
Tekli (CastUnitMark, AssemblyMark). Jedna reguła lidera CS (`src/core/leading_element.py`): pręt, gdy zespół ma pręty (zwykle
najdłuższy; wyraźnie cięższy, nieco krótszy wygrywa z długim lekkim; pas górny kratownicy wygrywa; remis = mniejszy GUID);
łączniki (śruba, nakrętka, podkładka, otwór, spoina) nigdy nie prowadzą, gdy jest inny członek; blacha konkuruje największym
wymiarem w płaszczyźnie, nie grubością; element liniowy spoza katalogu konkuruje jak pręt tylko, gdy ≥ 1,25× dłuższy. Lider
wstępny przy imporcie, ostateczny w module `assembly`. Zastępczy lider (`assembly.service.zastepczy_lider`), gdy zapisany
jest spoiną, kotwą albo go brak. Podobieństwo zespołów: grupy `identical` (ten sam skład z tym samym `klasa_p4`),
`canonical_assembly_id` (reprezentant), `canonical_compliance_pct`; wzorce powtarzalności W/V (`assembly_pattern`);
zestawy końców pręta wiodącego (`connection_set`); ALCS = lokalizacja blach w zespole (`alcs_role`, `plate_type`,
`plate_orientation`, `typ_kraty`). Zespoły z Advance Steel bez `IfcRelAggregates` syntetyzuje `assembly_synthesis`
z relacji spoin warsztatowych (spoina łączy, śruby dzielą).

## Osie i poziomy konstrukcyjne (axes_levels): oś w osiach słupów, klasy osi, poziomy, bramka brak siatki
Moduł `axes_levels` jest jedynym źródłem osi w CS (dawny `structural_systems` usunięty). Zasada nadrzędna: oś konstrukcyjna
SIEDZI W OSIACH SŁUPÓW; rysunki, rytm siatki i `IfcGrid` są warstwą raportową i nie odciągają osi od słupów. Miara: % słupów
na osi z dokładnością 0,5 mm (KOT 59,8 %, WSP 96,2 %, HUT 99,0 %). Algorytm: kierunki rodzin X/Y z histogramu 1° ważonego
długością (konstrukcja ortogonalna daje 0,0000° / 90,0000°); słupy = piony ≥ 2 m (słupki balustrad ~440 mm odpadają);
stosy wyrównania z regułą porządku konstrukcyjnego; backstop dwumodalny (osie skrajne nośne + fasadowe); snap do dominanty
±0,5 mm; ważność (główne / pomocnicze / graniczne) z przełomu objętościowego i powtarzalności. Poziomy: klaster topów belek
głównych (30 mm), scalanie < 250 mm, typy konstrukcyjny / komunikacyjny (kraty) / posadowienie (podstawy słupów), nazwa
`Z_11,360`, flaga `wyrazny` (min 3, max 8); konstrukcja drugorzędna bez słupów: poziom ze spodu belek. Bramka „brak siatki”:
poniżej 50 % słupów na osiach ≥ 2-słupowych (estakady) model nie dostaje pozornych osi. Tabele `analysis.os_konstrukcyjna`
(tag `X_01`, tag eksportu `X1`, `coord_mm`, klasa, waga), `poziom_konstrukcyjny`, `os_membership`, `poziom_membership`.
Pokrycia `IfcCovering` nie należą do osi ani płaszczyzn.

## Płaszczyzny konstrukcyjne i połacie (cs3): structural_plane axis_x axis_y axis_z roof, roof_plane RP#, plane_model
Rejestr płaszczyzn `analysis.structural_plane` konsoliduje osie (z `axes_levels`), połacie dachu i obudowę; `plane_type`:
`axis_x` (płaszczyzna YZ w pozycji X), `axis_y`, `axis_z` (poziome), `roof`; każda ma równanie `n·p + d = 0` w mm i obrys z trzech
punktów; `plane_id` czytelny (`X_00010540`, `RP1`). Połacie (`analysis.roof_plane`, RP#) wykrywa się z prętów ukośnych liderów
zespołów (spadek i tolerancja kąta); `roof_plane_member` z regułą przynależności; most połaci v2 (okap po końcach dłuższych
krokwi, pola pod wyższą konstrukcją). `plane_model` = wirtualny model wszystkich płaszczyzn jednego IFC do podglądu
w zakładce MS (nie do analizy). Wiersz Mapy słownika: cs3 → `glossary/struktury.md` §4 + `glossary/osie_uklady.md` §21.
Stan: połacie to dziś prostokąty, które się nie stykają (łuk Obudowa i obciążenia ma to zmienić).

## Podział pliku na obiekty i segmenty dylatacyjne (podzial_obiektow): OB1, WY3, OB1-S4, przecina_dylatacje, zakres pracy
Jeden plik IFC niesie często kilka niezależnych konstrukcji (dwie bliźniacze hale, wysoki obiekt, estakady, wyspy fundamentów)
albo długi budynek podzielony dylatacjami. Moduł `podzial_obiektow` (L3) daje każdemu elementowi z geometrią: obiekt (`OB1`
konstrukcja, `WY3` wyspa betonu; obiekt = stal bliżej niż 1 m w rzucie), segment dylatacyjny (`OB1-S4`, numer, oś i zakres
od–do w m), flagę `przecina_dylatacje` (np. płatew zachodząca za dylatację). Udostępnienie: pset `CS23_podzial_obiektow`
(profil `user`), moduł psetów `podzial` w oknie eksportu, `.csx`. Zakres pracy = aktywny kontekst (jeden obiekt lub segment):
Układy, zespoły i produkty tylko dla tego zakresu; pliki wydzielone do osobnej analizy, korekty i nazwy; analiza wstępna
(podział zaraz po wczytaniu, MOR1A 112 s do drzewa); „plaster” = dylatacja i granica obiektów bez ciągłości stali. Wykład
użytkownika: dylatacje i rozbite modele w jednym pliku, typy dylatacji i budynek L (odpowiedzi O9, O10). Nie mylić obiektu
z podziału (OB1) z rolą pręta (`obiekt_konstrukcyjny`) ani typem zespołu.

## Podobieństwo elementów i wzorce: similarity_type, unikaty, grupy, tryb Wzorce, powtarzalność
Grupowanie podobieństwa (`similarity`) działa na warstwie analitycznej: unikaty prętów (`unique_element_id`, z otworami
`unique_with_holes_id`), unikaty blach (`cs4_blacha`: kontur + otwory + grubość, pamięć `podobienstwo.py`), grupy zespołów
(`assembly_similarity`: `identical`, `canonical`), rodziny zespołów i wzorce W/V (tryb „Wzorce” w UI), zestawy połączeń
i strefy z numerami `SP001…`, `P0001…`, `GP001…`. Powtarzalność jest podstawą rysunków (jeden arkusz na grupę), kosztów
i wykrywania spoin (zespół kanoniczny liczony raz, instancje dziedziczą). Wejście do podobieństwa idzie dwoma zapytaniami
bez złączenia (BRNO: z 1392 s do sekund). Użycie w UI: filtr `WHERE element_konstrukcyjny='balustrada'`, kolorowanie po typie,
DXF per typ; `interpretacja.md` §4–§6 mówi, kiedy ufać grupom i kontaktom.

## Jak czytać wyniki klasyfikacji: kiedy ufać, kiedy sprawdzać, intent debug-classification, co AI raportuje
Każdy element po pipeline ma 4 warstwy klasyfikacji + grupę podobieństwa + relacje; inżynier powinien wiedzieć, co system wie,
czego nie wie (brak sygnału) i kiedy weryfikować ręcznie. Ufaj `klasa_p1`, gdy Reference i materiał są czytelne; sprawdź przy
NULL. Ufaj typowi zespołu, gdy liczniki ról zgadzają się z intuicją; sprawdź, gdy `element_konstrukcyjny = NULL` (zespół
nietypowy, sama blacha) albo wynik kłóci się z intuicją (drabina jako balustrada) — wtedy intent `debug-classification`
(najczęstszy intent projektanta, 3–10 razy na nowy IFC): AI diagnozuje punktację i proponuje poprawkę reguły, a zmiana
klasyfikacji wymaga ponownego biegu modułu na BFO i obejrzenia w UI. Po imporcie AI raportuje rozkład `klasa_p1`, liczbę
zespołów i typów, osie i poziomy, grupy podobieństwa oraz odchylenia od wzorca (`tests/fixtures/baselines/BFO_model.expected.json`).
Faza deweloperska: dane w bazie to testy, numeracja i wyniki mogą się zmieniać między wersjami.
