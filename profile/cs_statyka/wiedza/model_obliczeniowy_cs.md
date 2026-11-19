# Model obliczeniowy w CS-check: MES z IFC, static, wyniki, obciążenia, obudowa, porównanie modeli

## Co CS robi, a czego nie: model prętowy z IFC, solver zewnętrzny (RFEM, Robot), wyniki z powrotem
CS nie liczy statyki, naprężeń, wymiarowania ani stateczności (handbook/limity.md §2). Cel łuku MES (wykład 2026-09-15):
CS ma przygotować model obliczeniowy MES z pliku IFC, policzyć go zewnętrznym solverem (RFEM, Autodesk Robot) i wrócić
z wynikami do CS i przeglądarki BIM; na początku tylko modele prętowe (1D) z elementów `klasa_p1 = PRĘT` z wyłączeniami
(drabiny, balustrady, kraty: R19). Główne zadanie to dołożenie elementów łączących pręty IFC w jeden model. Droga do Robota:
zakładka MES → Robot w toolkicie (most `mes_robot`), wyniki przez `tools/robot_sily.py` do magazynu `static.static_run`.
RFEM 6: eksporter `static` to na razie zaślepka; wyniki RFEM czyta się z żywego programu (`tools/rfem_obwiednia.py`) albo
z pliku `.rf6` (`rf6_plik.py`, pisarz `rfem6` do magazynu). Obciążeń CS z RFEM nie pobiera (tylko kombinacje i sytuacje),
podpory czytają Robot i RFEM 5, a RFEM 6 (API II) nie, więc model z RFEM 6 nie przechodzi walidacji `static` (mechanizm).
Solver prętowy biura (OptimaFEA, MKK) jest rozpoznany w planie PLAN_SOLWER_MES_W_CS.

## Pojęcia modelu MES z wykładu: węzeł obliczeniowy, łańcuch prętów, podstawa modelu, trzy systemy, hierarchia
Węzeł obliczeniowy (punkt) to szkielet systemu prętowego, osobny od węzłów połączeń stalowych (które też mogą stać się takimi
punktami); w przyszłości do edycji geometrii. Łańcuch prostych prętów = ciąg prętów o osiach równoległych w jednej płaszczyźnie,
oddalonych mniej niż 0,5 h (wysokości pręta), nie pokrywających się (np. ciąg słupów); automatyczna podpora tylko na samym dole
łańcucha słupów. Podstawa modelu = automatycznie wyznaczona płaszczyzna, w której leżą podpory (słupy mogą kończyć się nieco
wyżej lub niżej). Trzy systemy: konstrukcyjny (pręty IFC i z blach), osiowy (w płaszczyźnie układu) i obliczeniowy.
Hierarchia w układzie pionowym (XZ, YZ): 1) słupy (łańcuchy słupów), 2) belki poziome, 3) inne pręty (stężenia); w układzie
poziomym XY belki dochodzą do słupów, belki drugorzędne do głównych, stężenia na końcu; połacie dachu podobnie. Hierarchia
prętów = najkrótsza droga do podpory (eksperyment: przybliżony rozkład obciążenia przed obliczeniami). Układy płaskie (osie
konstrukcyjne) pozwalają myśleć płaszczyznami: najpierw układy pionowe, potem poziome, potem połacie. Panel Układów w toolkicie
jest miejscem podglądu systemu osi, węzłów obliczeniowych i punktów połączeń (środki ciężkości zestawów śrub). Pręty wirtualne
z blach (króćce dwuteowe, krzyże) uczestniczą równoprawnie; nowe produkty: tabela węzłów obliczeniowych, pręty wirtualne
uzupełniające (krótkie odcinki łączące pustki), podział pręta systemu konstrukcyjnego.

## Dane warstwy MES: analysis.pret_systemu (system, rola), wezel_obliczeniowy (rodzaj), flaga_luk, podpory
`analysis.pret_systemu.system`: `konstrukcyjny` (pręt IFC albo z blach, warstwa M1) / `obliczeniowy` (uzupełniający, sztywny; M2).
`rola`: `ifc` (z `core.pret`), `z_blach` (z `core.pret_wirtualny`: blachownica, króciec, krzyż), `uzupelniajacy` (wydłuża oś pręta
bazowego do węzła, dziedziczy przekrój; od A′ sięga tylko do miejsca styku F = rzut P na oś dochodzącego; w doczołowym F
= przecięcie osi z płaszczyzną styku blach), `sztywny_wirtualny` (pręt sztywny: ⟂ do partnera, w osi pręta dochodzącego, ∥ przez
węzeł `PZ:` żebra zakładkowego, albo odcinek na pręcie bazowym od F do końca na drodze siły — MES-G07), `sztywny_import` (rezerwa).
`analysis.wezel_obliczeniowy.rodzaj`: `przeciecie` (przecięcie osi/przedłużeń; od PR-5 także stopa F zakładkowego),
`polaczenie` (węzeł na punkcie P = środek zestawu śrub albo punkt spoiny; od A′ P jest ATRYBUTEM węzła F: `polaczenie_kod`,
`p_x/y/z_mm`, `odleglosc_od_osi_mm`), `koniec_wolny`. `flaga_luk` (zbiór otwarty): `lewituje_bez_dowodu` (pręt bazowy bez
żadnego łącznika), `lewituje_mimo_dowodu` (jest dowód w grafie `mes.pret_polaczenie`, warstwa nie zrealizowała połączenia — luka
reguł), `lewituje_kotwa` (zostaje w modelu), `lewituje_bez_podpory` (składowa grafu bez podpory, model z „patyczaka”),
`dojscie_probne` (dojście z dowodu tam, gdzie osie nie dochodzą; idzie do `static`), `sztywny_dlugi` (> 800 mm; idzie do `static`),
`bez_roli_statycznej` (tylko do oglądu). Flaga = „wymaga oka projektanta albo nie wolno liczyć”; `static_export` pomija `lewituje*`.
Podpory: `wiez_podpory`, `zrodlo_podpory`; `mes/podpory_geometryczne.py`.

## Iteracja 1 modelu MES (wariant A′): zakładkowe, doczołowe, sprężyny sztywności, model z patyczaka, bramka mechanizmu
Kod na `main` (scalenie pracy Karola 2026-09-24), iteracja 1 wg wariantu A′ w sesji autonomicznej 2026-09-26/27 (PR #547–#555,
migracje 176–178); budowa poza pipeline’em: `build_mes` (`tools/`), flagi warstwy OFF (`CS_MES_WARSTWA_OBL/M3/STATIC`).
Trzy kroki: `bar_extractor` (`core.*` → `mes.mes_node` / `mes.mes_bar`, `source_kind='ifc_element'`), wykrycie płaszczyzn
(z `analysis.structural_plane` albo klastrowanie Z prętów poziomych), `connector_pipeline` (pręty łączące luki, `synthetic`).
Połączenia zakładkowe (PR-5): oś → F → uskok ⊥ → węzeł `PZ:` → żebro ∥ do osi dochodzącego (`_r9.dojscie_zakladkowe`);
wszystko inne (dwu, R9b, kalenica, tandem, spoiny) zostaje A′. Odcinek pręta za węzłem połączenia na drodze siły jest sztywny
(MES-G07 = A, MES-G10 = B, 2026-10-31). Sztywność połączeń doczołowych jako sprężyny za flagą (S1, `mes/sztywnosc/`). Model
z „patyczaka” (MOR1A 3.4): pliki bez śrub i spoin dostają połączenia z dowodu geometrycznego (`kind='geom'`). Równoległe płatwie
na zakład bardzo blisko siebie: kod za flagą (Q-319). Kryteria oglądu A′ i PR-5 przyjęte 31.10. Bramka mechanizmu przed Robotem
(< 1 s, numpy, ze skupiskami i elementami IFC w oknie). Pułapki: handbook/pitfalls/mes_model_obliczeniowy.md.

## Schemat static: neutralny model obliczeniowy (IR), zasady twarde, tabele, API, rewizje, łuki
`static.*` to autorski, neutralny model obliczeniowy (Etapy 1–3 kompletne 2026-07-05). Zasady twarde: SI wewnątrz (m, N, Pa,
kg, rad; baza analityczna `core.*`/`mes.*` jest w mm, konwersja wyłącznie w adapterach importu); zero pojęć programowych
(mapowania na programy w `external_refs`); tożsamość UUID + opcjonalny `tag` (numeracja RFEM/Robot to mapowanie, nie tożsamość);
dane AUTORSKIE: `static.*` nie odtwarza się z IFC, nie czyścić przy resetach. 13 tabel (rdzeń, loads, kombinacje, rewizje);
pręty łukowe `geometry_type` `straight`/`arc` z punktem kontrolnym `arc_control_x/y/z` [m]. API (wymagane `project_id`):
`POST/GET/DELETE /api/static/models`, `POST /models/{id}/bulk` (cały graf jedną transakcją), `validate` (silnik reguł; brak podpór
= „model has no supports (mechanism)” blokuje eksport), `revisions` + `diff` (snapshoty z sha256). Konwencja osi lokalnych
RFEM-like (docstring `StaticMember`). Flaga `CS_STATIC_MODULE` domyślnie ON. Generatory modelu i kontrakt eksportu (Etap 3);
eksporter RFEM to zaślepka. Model z Robota: `python tools/robot_pull.py --projekt 1095_WSP --do-core` (bez `--do-core` zostaje
w `static` i analizy CS go nie widzą).

## Magazyn wyników analizy: static_run i bloki, stacje, przebiegi, zwolnienia, osie lokalne, pisarze Robot i RFEM 6
Magazyn (migracja 174) trzyma przebiegi `static.static_run` i bloki wyników float32: `static_run_rodzaj`, `static_result_layout_kind`,
`static_result_domain`, `static_result_quantity`, zwolnienia `static_member_release_dof`; ugięcia i osie lokalne (2026-09-22),
strzałki i zwolnienia (2026-09-24). Stacje per pręt zależą od długości (od 2026-09-27): < 0,3 m — 2, < 1 m — 3, < 3 m — 7, dłuższe
— 11 (`--podzial rowny` = 11 wszędzie). Siły przywęzłowe per przypadek prosty (kombinacje ręczne z mnożnikami; CS składa je
superpozycją), przemieszczenia węzłów, reakcje podpór. Osie lokalne prętów to migawka przebiegu (`static_run.raw_files`
`osie_lokalne_prety_f64`), bo należą do biegu, nie do modelu. Pisarze: Robot (`tools/robot_sily.py --core-id N`; gdy model nie jest
policzony, sam uruchamia obliczenia), RFEM 6 (`rfem6`, S2-36, z pliku `.rf6`), SYMULACJA. Konsument: Układy → pasek „Wykresy”
(źródło DEMO/Robot/RFEM; N/Vy/Vz/T/My/Mz na prętach układu, przypadki bazowe + obwiednia), `results3d` w viewerze. Wykład
2026-09-19: dane z analizy zawsze pod ręką, dom wyników, przewidywanie kroku użytkownika na Układach, powtarzalność jako szybka
weryfikacja, liczenie fragmentu modelu z blokadami zarządzanymi przez CS.

## Obciążenie śniegiem PN-EN 1991-1-3 (feature loads): 8 przypadków, dwa tryby, decyzje, kontrakty, jednostki
Silnik EC1-1-3 z polskim załącznikiem krajowym liczy na żądanie (parametry normowe to decyzja inżyniera, nie dane z IFC).
Przypadki: 5.3.2 dach jednospadowy, 5.3.3 dwupołaciowy (układy i/ii/iii), 5.3.4 wielopołaciowy (zaspa w dolinie), 5.3.5 walcowy
(μ₃ z h/b, kąt wezgłowia β), 5.3.6 dachy bliskie i przylegające, zał. B.3 zaspa wyjątkowa przy wyższej budowli, 6.2 zaspa przy
przeszkodzie / attyce, 6.3 nawis [kN/m], 6.4 barierka [kN/m] (6.3 i 6.4 nie tworzą stref, wynik w notce). Zakładka Obciążenia:
7 stron; tryb parametryczny (strony 1–6, nic nie zapisuje) i „Z modelu” (połacie i attyki z geometrii, ocena i strefy
`analysis.load_zone` do bazy; overlay śniegu w viewerze). Parametry terenu (strefa śniegowa albo sₖ, Cₑ, Cₜ) wspólne. Notka
obliczeniowa = dowód z wzorami NA, odnośnikami do tablic i podstawieniem. Decyzje użytkownika 2026-08-19: dach bliski — szczelina
skraca strefę (`Lₛ,eff = Lₛ − x`, μ₂ = μs + μw bez mnożnika), kąt wezgłowia `atan2(b/2; R − h)` zamiast `arcsin`. Kontrakty:
`formula_ref` wynika z typu dachu, `mu_left/right` to μ₁ połaci bez mnożnika układu, dla wielopołaciowego `s_right` = dolina.
Rozkład na pręty (OB7) i obudowa: katalog i ciężar (`cladding/`, karta OBK). Wiatru jeszcze nie ma (pusty katalog `loads/wind/`).

## Obudowa, płaszczyzny i obciążenia (łuk 2026-10-31): słownik, powierzchnia obciążenia, kierunek przenoszenia, rozkład
Użytkownik: „nasz model obliczeniowy nic nie policzy bez obciążeń”; brakuje obudowy, do której przykłada się śnieg, wiatr, stropy
i obciążenia użytkowe. Słownik (propozycja sesji przed wykładem): obudowa (pokrycie dachu + ściany; nie nośna, oddaje obciążenia
na pręty), płaszczyzna konstrukcyjna (`structural_plane`), połać (`roof_plane` RP#, dziś prostokąt w rzucie), powierzchnia
obciążenia (płaski wielokąt z otworami, rodzaj, kierunek przenoszenia, pręty podporowe — „okładzina” u Marka, „load transfer
surface” w RFEM; dziś brak), rodzaj powierzchni (dach, ściana podłużna/szczytowa, attyka, strop/pomost, krata, blacha podestowa),
kierunek przenoszenia (jedno-, dwukierunkowy, kopertowy; u Marka X/Y/XY/K), pręt podporowy (płatew, rygiel ścienny, belka
stropowa, dźwigar), pręt „nie podpiera” (stężenia, pas dolny), krawędzie połaci (okap, kalenica, kosz, naroże, uskok), attyka
(worki śniegu, osobne pole wiatru), strop/pomost (płyty `core.powierzchnia`, kraty `core.krata`), strefa obciążenia, przypadek
(`static_load_case.category`), kombinacja PN-EN 1990, rozkład na pręty (pole przynależne „jak Robot”, dźwignia, koperta; brak —
najcenniejsze z metody Marka), obciążenie trapezowe na pręcie, ciężar obudowy (przypadek stały CO), obudowa z pliku / domniemana
/ użytkownika, z_e. 14 pytań blokujących odpowiedzianych 1.11, 36 otwartych; plan PLAN_OBUDOWA_OBCIAZENIA.

## Porównanie modelu fizycznego z analitycznym (model_compare): dwa poziomy, przesunięcie układów, splot, 1:N
`model_compare` nie jest modułem pipeline’u, lecz serwisem na żądanie (para fizyczny Tekla-IFC ↔ analityczny RFEM/Robot).
Dwa poziomy odpowiedniości: globalny (`analysis.model_correspondence` + `_datum`: jeden rekord na parę modeli, dziś przesunięcie
układu; sloty wektorowy/liczbowy/tekstowy z `source`, `rule`, `evidence`) i per element (`analysis.cross_model_match`: który pręt
obliczeniowy odpowiada której części fizycznej). Bez cofnięcia przesunięcia matcher przy tolerancji 50 mm dawał 5 dopasowań, z nim
1 381 (para WSP w układach oddalonych o 22 m). Znak: zapisane `d` znaczy `obliczeniowy = fizyczny + d`; do nałożenia stosuje się
`−d`. Automat nie nadpisuje ręcznego (`source='manual'`). Trzeci poziom: SPLOT, wspólny symbol do kolorowania obu modeli.
Dopasowanie 1:N i parytet długości rozwiniętej; osie łuków z `core.pret.arc_*`. Narzędzie: `tools/dopasuj_model_obliczeniowy.py
--obliczeniowy 72 --fizyczny 65 --zapisz --powiaz-prety --wytezenie wytezenie.json`; `--eksport-para` daje dwa pliki IFC
(obliczeniowy przesunięty do układu fizycznego); `--eksport-ifc` = sam model obliczeniowy (scalony plik 22× większy jest opcją
`--z-fizycznym`). Baza zostaje w układzie Robota; przesunięcie jest własnością eksportu. Model powłokowy tą drogą nie przechodzi.

## Wyniki statyki 3D w viewerze (results3d): kontrakt results3d_v1, deformacja, bramka wyniki3d, właściciel wyników
Łuk „wyniki statyki 3D w viewerze” (od 2026-10-08; program S2 wykonany 2026-10-12, migracje 190–192). Kontrakt `results3d_v1`
(viewer czyta dokładnie to) i deformacja 3D na bryłach IFC (`deform3d_v1`, kroki D-0…D-10). Źródło: plik `.rf6` RFEM 6
(`model.db` + wyniki `!solver`) zapisany do magazynu `static.*` pisarzem `rfem6`; bramka wzorcowa `python tools/wyniki3d.py
check <plik.rf6>`; zmierzone na Core Ultra 9: model.db w cache. Właściciel wyników = plik IFC projektu (`owners`, sesja „jeden
model”). Psety wyników §41: `N_max`/`N_min` z `_konwencja_N`: „+” = rozciąganie dla każdego solvera. Brak wykładu użytkownika
o tym, co inżynier chce zobaczyć najpierw (N/M/V na prętach, mapy na powierzchniach, LOD, etykiety, kombinacje, deformacja) —
odpowiedzi 1–64 i S2 (`PYTANIA_WYNIKI_3D`) go zastępują. Na Układach: przeglądarka statyki (wykresy na osiach prętów,
deformacje `deform_v1`).
