# Karta: model obliczeniowy w CS-check

## Granica
CS nie liczy statyki, wytrzymałości ani wymiarowania (handbook/limity.md). Buduje model prętowy z IFC, wysyła go do solvera (Robot przez most `mes_robot`, RFEM w planie), czyta wyniki (RFEM 6 obwiednie i plik `.rf6`, Robot `robot_sily.py`) i pokazuje je na Układach i w 3D. Obciążeń z RFEM nie pobiera (tylko kombinacje i sytuacje obliczeniowe); podpory czyta Robot i RFEM 5, nie RFEM 6 (API II).

## Model MES z IFC (feature mes, schemat mes.*, analysis.pret_systemu, wezel_obliczeniowy)
- Z wykładu 2026-09-15: model prętowy z elementów `klasa_p1 = PRĘT` z wyłączeniami (drabiny, balustrady, kraty — R19); główne zadanie = pręty łączące pręty IFC w jeden model. Łańcuch prostych prętów (osie równoległe w jednej płaszczyźnie, bliżej niż 0,5 h) → automatyczna podpora tylko na dole łańcucha słupów; podstawa modelu = płaszczyzna podpór. Trzy systemy: konstrukcyjny, osiowy, obliczeniowy; hierarchia w układzie pionowym: słupy → belki → stężenia; „oś jest święta”. Pręty wirtualne i blachownice z blach uczestniczą jak pręty.
- Dane: `analysis.pret_systemu` (`system`: `konstrukcyjny` / `obliczeniowy`; `rola`: `ifc`, `z_blach`, `uzupelniajacy` (wydłuża oś do węzła, dziedziczy przekrój), `sztywny_wirtualny` (rigid link ⟂ do partnera, w osi, przez `PZ:`, odcinek za F na drodze siły), `sztywny_import`), `analysis.wezel_obliczeniowy` (`rodzaj`: `przeciecie`, `polaczenie` (punkt P = środek zestawu śrub albo spoiny jako atrybut F), `koniec_wolny`), `flaga_luk` (`lewituje_bez_dowodu`, `lewituje_mimo_dowodu`, `lewituje_kotwa`, `lewituje_bez_podpory`, `dojscie_probne`, `sztywny_dlugi` > 800 mm, `bez_roli_statycznej`), `wiez_podpory`, `zrodlo_podpory`.
- Iteracja 1 (wariant A′): zakładkowe = oś → F → uskok → `PZ:` → żebro; doczołowe: F na płaszczyźnie styku; sprężyny sztywności doczołowych za flagą; model z „patyczaka” (pliki bez śrub i spoin) z dowodu geometrycznego; bramka mechanizmu przed Robotem (< 1 s, numpy). Budowa poza pipeline’em: `build_mes` (`tools/`, flagi `CS_MES_WARSTWA_OBL/M3/STATIC`).

## Neutralny model static (schemat static.*) i magazyn wyników
Zasady twarde: SI wewnątrz (m, N, Pa, kg, rad), zero pojęć programowych (mapowania w `external_refs`), UUID + `tag`, dane AUTORSKIE (nieodtwarzalne z IFC). 13 tabel: rdzeń, loads, kombinacje, rewizje = snapshoty z sha256 i diff; pręty łukowe przez punkt kontrolny. API `/api/static/models`, `bulk`, `validate` (brak podpór = mechanizm = błąd), `revisions`, `diff`. Magazyn wyników: `static.static_run` + bloki float32 (`static_run_rodzaj`, `static_result_layout_kind`, `static_result_domain`, `static_result_quantity`, zwolnienia `static_member_release_dof`); stacje per pręt zależne od długości (< 0,3 m 2, < 1 m 3, < 3 m 7, dalej 11); osie lokalne prętów jako migawka przebiegu. Pisarze: Robot (`robot_sily.py`), RFEM 6 (`rfem6`, S2-36), SYMULACJA.

## Obciążenia (feature loads) i obudowa
- Śnieg PN-EN 1991-1-3 z załącznikiem krajowym, 8 przypadków: 5.3.2 jednospadowy, 5.3.3 dwupołaciowy (układy i/ii/iii), 5.3.4 wielopołaciowy (dolina), 5.3.5 walcowy, 5.3.6 bliskie i przylegające, zał. B.3 zaspa wyjątkowa, 6.2 przeszkody i attyki, 6.3 nawis [kN/m], 6.4 barierka [kN/m]. Zakładka Obciążenia: tryb parametryczny (strony 1–6, bez zapisu) i „Z modelu” (połacie i attyki z geometrii, strefy do bazy, overlay w viewerze); notka obliczeniowa z wyprowadzeniem. Decyzje 2026-08-19: dach bliski — szczelina skraca strefę (`Lₛ,eff = Lₛ − x`), kąt wezgłowia `atan2(b/2; R − h)`. Liczone na żądanie (parametry normowe to decyzja inżyniera). Rozkład na pręty (OB7), obudowa: katalog i ciężar (`cladding/`).
- Łuk „Obudowa, płaszczyzny i obciążenia” (2026-10-31): słownik: obudowa, płaszczyzna konstrukcyjna, połać, powierzchnia obciążenia (rodzaj, kierunek przenoszenia, pręty podporowe, „nie podpiera”), płatew, dźwigar, rygiel ścienny, krawędzie połaci (okap, kalenica, kosz, naroże, uskok), attyka, strop/pomost, strefa obciążenia, przypadek, kombinacja, rozkład na pręty, pole przynależne, ciężar obudowy, obudowa z pliku/domniemana, z_e. Najcenniejsze do wzięcia z metody Marka (Hala): rozkład powierzchni na pręty.

## Porównanie modeli i wyniki 3D
- `model_compare`: fizyczny (Tekla IFC) ↔ analityczny (RFEM/Robot); dwa poziomy: globalny (`analysis.model_correspondence` + `_datum`, przesunięcie układów: `obliczeniowy = fizyczny + d`) i per element (`analysis.cross_model_match`); automat nie nadpisuje ręcznego; symbol splotu; `tools/dopasuj_model_obliczeniowy.py --zapisz --powiaz-prety --wytezenie`, `--eksport-ifc` = sam model obliczeniowy przesunięty do układu fizycznego.
- `results3d`: wyniki statyki 3D dla viewera (kontrakt `results3d_v1`, deformacja na bryłach `deform3d_v1`), bramka `python tools/wyniki3d.py check <plik.rf6>`; właściciel wyników = plik IFC projektu. Przeglądarka statyki pod zakładką Układy (wykresy N/Vy/Vz/T/My/Mz, źródło DEMO/Robot/RFEM).

## Gdzie szukać (repo CS)
| Pytanie o… | Plik |
|---|---|
| wartości i definicje MES, wyników, obciążeń, obudowy | glossary/obliczenia.md (§9, §25, §26), obciazenia.md (§20), obudowa.md (§33) |
| dlaczego tak (wykłady) | WYKLAD_MODEL_MES_WPROWADZENIE, WYKLAD_OBUDOWA_PLASZCZYZNY_OBCIAZENIA, WYKLAD_DANE_ANALIZY_SZYBKIE_WYNIKI |
| model MES, iteracja A′, sztywność, noty dla Karola | src/features/mes/README.md; plan PLAN_MODEL_MES_Z_IFC |
| model static, magazyn wyników | src/features/static/README.md |
| śnieg, obudowa | src/features/loads/README.md; docs/help/obciazenia_*.md |
| porównanie modeli | src/features/model_compare/README.md |
| wyniki 3D | src/features/results3d/README.md |
