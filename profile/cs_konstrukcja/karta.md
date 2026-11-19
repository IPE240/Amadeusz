# Karta: rozumienie konstrukcji w CS-check

## Cztery warstwy klasyfikacji elementu (moduł cs1, `core.element`)
- `klasa_p1`: PRĘT · UZBROJENIE (blachy, elementy łączące) · ŁĄCZNIKI (śruby, kotwy, otwory) · SPOINY · OKŁADZINA (kratki) · POWIERZCHNIE (ściany, stropy, membrany) · NIEKONSTRUKCYJNE · BRAK W BAZIE (prefiks opisu spoza katalogu) · NULL (brak prefiksu). Źródło: `Pset_*Common.Reference` + materiał przez katalog.
- `klasa_p2`: BLACHA, ELEMENTY ŁĄCZĄCE, ŚRUBY, KOTWY, OTWORY, WALCOWANE, GIĘTE OTWARTE, ZAMKNIĘTE, PEŁNE, BLACHOWNICA DWUTEOWA (HI/WI), BLACHOWNICA SKRZYNKOWA (HK), INNE, KRATKI, POMOCNICZE.
- `klasa_p3`: skrót kształtu (IPE, HEA, RHS, CHS, BL, M…); `klasa_p4`: rozmiar z katalogu (`HEA100`, `RHS100*10`, `BL10`) albo `brak_w_bazie` (kształt znany, rozmiar nie; celowe).
- Ufaj, gdy profil ma czytelną Reference i materiał stalowy (90 % przypadków); sprawdź ręcznie przy NULL (profil niestandardowy, literówka w Reference, nie-stal).

## Rola pręta w zespole (cs2, `obiekt_konstrukcyjny`, tylko PRĘT)
`leading_element` (najdłuższy, odniesienie), `dlugi_pret` (≥ 0,4 długości głównego, równoległy), `krociec` (krótki), `poprzeczka` (≥ 60°), `przedluzenie` (współliniowy), `skratowanie` (20–60°), `pochwyt`, `slupek`, `kolano`, `inne`. Role balustrady nadpisują pozostałe. „Inne” to worek: na projektach biura 68–92 % ról innych niż główny.

## Typ zespołu (cs2, `element_konstrukcyjny`, `analysis.cs2_summary`)
Nośne: `slup` (pionowy, `is_slup_osi` = na przecięciu osi X i Y ma priorytet), `belka` (poziomy nośny dachu/stropu), `rygiel` (poziomy drugorzędny), `kratownica` (lider + długi pręt + skratowania; wygrywa gdy spełniony), `stezenie_pionowe` / `stezenie_poziome` / `stezenie_polaciowe`. Drugorzędne: `ryglowka` (rygiel na ścianie zewnętrznej), `balustrada`, `drabina` (wygrywa nad balustradą), `krata_pomostowa`, `krata_schodowa` (twarda reguła, nie punktacja: krata + blachy burtowe + śruby). `NULL` = żaden próg nie przeszedł. Punktacja w `scores_json`. Drabiny, balustrady i kraty są wykluczone z modelu MES (R19). Typ zależy od globalnych osi X/Y/Z: w modelach obróconych w rzucie belki wychodzą jako stężenia.

## Zespół i lider
Zespół (`core.assembly`) = elementy o wspólnym `assembly_guid` z IFC (`IfcRelAggregates`). Jeden lider na zespół (`leading_element_id`): pręt, gdy zespół ma pręty (zwykle najdłuższy; wyraźnie cięższy nieco krótszy wygrywa; pas górny kratownicy wygrywa); łączniki nigdy nie prowadzą; blacha konkuruje największym wymiarem w płaszczyźnie. Podobieństwo: grupy `identical` (ten sam skład i `klasa_p4`), `canonical_assembly_id`, wzorce W/V, zestawy końców (`connection_set`), ALCS (lokalizacja blach w zespole, `plate_type`, `plate_orientation`).

## Osie, poziomy, płaszczyzny, obiekty
- `axes_levels` (jedyne źródło osi): oś konstrukcyjna SIEDZI W OSIACH SŁUPÓW (dokładność 0,5 mm; KOT 59,8 %, WSP 96,2 %, HUT 99,0 %); rodziny X/Y z histogramu kierunków; słupy = piony ≥ 2 m; klasy osi główne / pomocnicze / graniczne; poziomy = góra belek głównych (`Z_11,360`), typy konstrukcyjny / komunikacyjny / posadowienie; bramka „brak siatki” poniżej 50 % słupów na osiach. Tabele `analysis.os_konstrukcyjna`, `poziom_konstrukcyjny`, `os_membership`, `poziom_membership`.
- cs3: rejestr płaszczyzn `analysis.structural_plane` (`axis_x`, `axis_y`, `axis_z`, `roof`), połacie `analysis.roof_plane` (RP#) z prętów ukośnych liderów zespołów.
- `podzial_obiektow`: każdy element dostaje obiekt (`OB1` konstrukcja, `WY3` wyspa), segment dylatacyjny (`OB1-S4`, zakres osi od–do), `przecina_dylatacje`; obiekt = stal bliżej niż 1 m w rzucie; pset `CS23_podzial_obiektow`; praca na zakresie (Układy, zespoły tylko jednego segmentu).

## Gdzie szukać (repo CS)
| Pytanie o… | Plik |
|---|---|
| definicje i wartości (SoT) | handbook/glossary.md (indeks) + glossary/klasyfikacja, struktury, assembly_alcs, osie_uklady, podzial_obiektow |
| co z tym zrobić w praktyce, kiedy ufać | handbook/interpretacja.md; handbook/intents/debug-classification.md |
| reguły cs1 (nazwa ≠ geometria, profil z pliku, mapowanie obcych programów) | src/features/cs1/README.md |
| osie i poziomy (algorytm, kryteria oglądu) | src/features/axes_levels/README.md |
| połacie, płaszczyzny | src/features/cs3/README.md |
| obiekty, segmenty, pliki wydzielone | src/features/podzial_obiektow/README.md; WYKLAD_DYLATACJE |
| „dlaczego” od użytkownika | WYKLAD_OBIEKTY_KONSTRUKCYJNE (OB-01…OB-14), WYKLAD_WIRTUALNE_PRETY_Z_BLACH |
