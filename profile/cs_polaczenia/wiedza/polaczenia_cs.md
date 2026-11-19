# Połączenia w CS-check: śruby, strefy, krawędzie i spoiny, spoiny ze źródeł, sztywność, IDEA StatiCa

## Śruby, osie, przebicia, zestawy śrubowe, otwory (bolt_axis, bolt_group, bolt_connection, bolt_connection_set)
Każda śruba w IFC ma jedną oś (`core.bolt_axis`, współrzędne w metrach), agregowaną do `core.bolt_group` (element poziomu
`klasa_p1 = ŁĄCZNIKI`: śruby, nakrętki, kotwy, a także sam wzór otworów bez łącznika — `klasa_p2 = OTWORY`, `klasa_p4 = D<Ø>`).
`analysis.bolt_connection` to przebicie: śruba fizycznie przebija element (od FAZY 3 tylko metoda analityczna `A1`, jedno
jądro przebicia `pierce_kernel.py`). `views.bolt_connection_set` to członkostwo w zestawie śrubowym: logiczna grupa
powiązanych przebić (np. wszystkie śruby flanszy), bez limitu liczby elementów (limit 4 zdjęty 2026-08-29, bo przy dwóch
blachach czołowych z futrówkami jedna blacha wypadała z połączenia). Otwory w blachach i ściankach prętów:
`analysis.bolt_hole_on_blacha` / `bolt_hole_on_pret` (33 % par prętowych w `bolt_connection` nie ma otworu — śruby podłużne).
Bryły otworów (koło, fasolka, Ø, wydłużenie, kierunek) z reprezentacji śrub w bazie (R18). Grupy 1-śrubowe (T1).
Powtarzalność detalu śrubowego: `core.bolt_group.connection_unique_id`. Relacje spoin i śrub według autora modelu
(`core.weld_relation`, `realizing_kind` = `weld` albo `bolt`, `is_site` montażowa) z `IfcRelConnectsWithRealizingElements`.

## Strefa połączenia: wirtualna blacha wokół zestawu śrub, 3D, grubość, pięć warstw numeracji SP P GP
Strefa połączenia (`analysis.strefa_polaczenia`) to element OBLICZENIOWY, nie fizyczny: wycięty fragment blachy
(`host_kind = blacha`) albo ścianki pręta (`pret_scianka`, pasmo z `profile_walls`) wokół JEDNEGO zestawu śrub, z grubością
tej części. Pomysł użytkownika (wykład 2026-08-29): obszar = obwód osi skrajnych śrub + odsunięcie 3D (trzy średnice śruby),
przycięty do granic blachy; dla pręta wzdłuż osi 3D, w poprzek cała szerokość ścianki (pas, środnik). Figury proste bez łuków:
1 śruba → kwadrat o boku 6·D; 2 albo komplet współliniowy → prostokąt `rozstaw + 2·3D` × `2·3D`; ≥ 3 → otoczka wypukła + 3·D
ze złączem ostrym. Pary idą z tabel otworów, nie z przebić. Podobieństwo z wyniku (kontur + wzór otworów + grubość + średnica),
lustra zwinięte do jednego kodu z wariantem na wystąpieniu. Pięć warstw numeracji po oglądach KOT: `SP001…` (grupa podobnych
stref), `polaczenie_code` `P0001…` (strefy i śruby JEDNEGO węzła spięte w całość), `polaczenie_grupa_code` `GP001…` (które
POŁĄCZENIA są takie same; KOT: 4 031 połączeń w 1 227 typach), LEVEL 1/2/3, scalanie zestawów śrub. Eksport: plik-nakładka
ze strefami i śrubami `/api/exports/ifc/strefy-polaczen?model_id=N` (najpierw moduł `strefa_polaczenia`, L14). Moduł liczy
jednowątkowo (świadome odstępstwo). To baza do nazywania rodzajów połączeń i katalogów połączeń.

## Typ połączenia: doczolowe, zakladkowe, skosne, inne; osie stron; kanapka; reguła 15° i 75°
`analysis.polaczenie_pret.typ_polaczenia` (ta sama wartość w wierszach obu stron; `typ_strony` = klasa z jednej strony):
`doczolowe` — blacha czołowa pełna lub półpełna w stosie ALBO pręt dochodzący z osią równoległą do osi śrub (≤ 15°);
`zakladkowe` — pręt dochodzący z osią prostopadłą do osi śrub (≥ 75°), a gdy żadna strona nie dochodzi, któraś oś
równoległa do płaszczyzny zestawu śrub; `skosne` — kąt 15–75°; `inne` — brak osi śrub, połączenie jednostronne (śruby
przebijają jeden element), obie strony bez pręta, pręty równoległe spięte żebrami. Kolejność reguł: brak osi → `inne`;
jednostronne → `inne`; blacha czołowa → `doczolowe`; strony dochodzące; którakolwiek strona ⟂ → `zakladkowe`. Zasada
użytkownika: „jedna z osi musi być równoległa do płaszczyzny zestawu śrubowego”, a blacha czołowa wyklucza zakładkowe.
Progi 15°/75° to wartości robocze (flagi `CS_STREFA_TYP_KAT_*`). Osie stron połączeń (`sciezka_osi_polaczenia`,
`rola_strony_polaczenia`, `ciecie_polaczenia` jedno/dwu) z kaskady czytającej `assembly_layout`, `plate_fork_link`
i `shared_edge_contact`. Połączenie kanapkowe (`analysis.polaczenie_kanapka`, KAN-1). Konsument: model MES
(`mes.odczyt.typy_polaczen`): zakładkowe = oś → F → uskok → węzeł `PZ:` → żebro; doczołowe = F na płaszczyźnie styku.

## Wspólne krawędzie i spoiny przewidziane: shared_edges, jedna krawędź = jedna grubość, 0,5 t, detektory, nakładka
Moduł `shared_edges` (L11) liczy wspólne krawędzie styków pręt–blacha, blacha–blacha i pręt–pręt wewnątrz zespołu wyłącznie
z warstwy analitycznej (pozy z `analysis.assembly_layout`, ścianki profilu i cięcia z `core.pret`, kontury z `core.blacha`),
bez siatek; jednostką jest zespół kanoniczny, instancje dziedziczą. Zasady z wykładu użytkownika (2026-07-11): krawędzie
wyznaczamy w zakresie każdego zespołu z wykorzystaniem powtarzalności (unikat elementu, zespoły identyczne i podobne, zestawy
blach połączeń); krawędź niesie parę elementów, lokalizację i grubości ścianek; jedna krawędź dotyczy tylko jednej grubości
ścianki (pręt z dwiema blachami po drugiej stronie = dwie krawędzie); na wyokrągleniu profilu grubość bierzemy z cieńszej
ścianki (środnik dwuteownika); wielkość spoiny szacujemy z grubości ścianek — zasady CS (`weld_rules.weld_spec`) dają
przewidzianą spoinę, np. a = 0,5 cieńszej ścianki. Tabele `analysis.shared_edge_segment`, `shared_edge_contact`
(`contact_class`, `detector` = napis diagnostyczny: c27, c29 skośne blachy, c30 widelec w slocie rury CHS, c34 ślad siodła;
`OGÓLNY` = tylko drabina ogólna, kandydat na regułę), `shared_edge_fillet_def`. Eksport spoin = sama nakładka IFC
(`/api/exports/ifc/shared-edges?model_id=N`, `bare=1`, plik `{model}_spoiny.ifc`); `&bare=0` na tle modelu (diagnostyka).
Znane luki: spoiny blacha↔pręt w środku długości, pręty pełne, styczność rur równoległych, blacha niepłaska (stożek garnka),
fantomowe spoiny żebro→słup przy podkuwce. Intent `przeglad-shared-edges`: kampanie przeglądu znalezisk w trzech klasach.

## Spoiny rzeczywiste ze źródeł: model IFC, rysunki, uwagi ogólne; zrodlo_spoiny, pewnosc_spoiny, rozbieżności
Spoiny przewidziane przez CS są domysłem; projekt ma spoiny w trzech miejscach (wykład 2026-10-17): w modelu (Tekla
`IfcFastener`; nie wszystkie są modelowane, np. przy blachach na widelec do rur kwadratowych biuro oznacza jedną z czterech
krawędzi), na rysunkach (DWG → DXF nałożony na model IFC, szukane tylko oznaczenia spoin i odpowiadające krawędzie) i w uwagach
ogólnych („spoiny nieoznaczone wykonać jako a = 0,5 t”). Feature `spoiny` składa spoinę rzeczywistą każdego styku (`WeldKey`)
w odczycie, jedną ścieżką `effective_weld_spec` → `spoiny.lancuch`; bez źródeł wynik = `weld_spec` bit w bit. `zrodlo_spoiny`
w kolejności siły: `nadpisanie` (zmiana projektanta na arkuszu, `work.override`) > `rysunek` > `model` > `detal_typowy`
(ze starszych projektów) > `uwagi_ogolne` > `cs_regula`; spoina przypięta wprost wygrywa z przeniesioną. `pewnosc_spoiny`:
`pewna` (oznaczona wprost dla tej krawędzi albo przeniesiona z zespołu o tym samym znaku w grupie `identical`),
`przeniesiona` (takie samo miejsce: widelec, drugie żebro, inny zespół, detal typowy), `z_uwag_ogolnych`, `przewidziana_cs`.
Znaczniki: `rozbieznosc` (dwa źródła mówią co innego), `z_innego_zespolu`, `nie_do_spelnienia`, `do_obejrzenia`. Rozbieżność
jest wyliczana, nie zapisana; wiersz `work.spoina_rozbieznosc` powstaje, gdy człowiek ją obejrzał (`obejrzana`) albo przyjął
(`przyjeta`, tekst wymagany). Lista: `GET /api/spoiny/do-obejrzenia`; toolkit Assembly → przełącznik „Spoiny”. Konsumenci:
arkusze zespołów, koszty spawania „wg projektanta”, nakładka IFC, spoiny 3D. Etapy: S3a (łańcuch), S3b (źródło model),
S4 (uwagi ogólne), S5/S6 (rysunki). Pomiar S0: `tools/spoiny/s0/`.

## Sztywność połączeń doczołowych i zakładkowych: cel bez MES, dźwignia, Mz, luz, docisk, mimośród, sprężyny w MES
Cel użytkownika: sztywność i nośność typowych połączeń z geometrii CS bez MES, bo CS zna dokładną geometrię (śruby, blachy,
spoiny), a do modelu MES potrzebne są sztywności. Doczołowe (cytaty 2026-09…10): nośność na rozciąganie była zaniżona — rozważyć
mechanizm z efektem dźwigni (śruba M24 10.9 więcej niż 150 kN); Mz równorzędny z My (dodatkowy suwak, nośność i sztywność
z dźwignią w drugim kierunku, wypadkowa przesuwa się w obu kierunkach); sztywności Mz wydają się za duże przy jednoczesnym My;
prezentacja jako paski nośności; więcej modeli i typów (KOT); węzeł na płaszczyźnie styku; „metoda prętowa”; podstawy słupów
jako sprężyna obrotowa k_φ. Zakładkowe (wykład 2026-10-11): często traktowane jako przegub, a przy dużych rozstawach śrub
przenoszą spory moment, więc przegub w statyce globalnej nie zawsze jest po stronie bezpieczeństwa; istotny luz w otworach
(d0−d) i podatność na docisk (K_b); niebezpiecznie, gdy nośność na docisk jest dużo większa od nośności śruby na ścinanie
(zerwanie śrub); w drugim kierunku blachy też mają sztywność; mimośrody jednociętych; cierne = osobna rodzina; pierwszy limit,
sztywność styczna i sieczna. W modelu MES sprężyny doczołowe za flagą (`mes/sztywnosc/`, S1). Plan: PLAN_SZTYWNOSC_POLACZEN
(S1 doczołowe, S1b, S2 zakładkowe, S3, etap „katalog”).

## Audyt konstrukcji i katalog połączeń: 80 % z katalogu, klasy 60/80/100 %, IDEA do nietypowych, koszty jako miara
Filozofia użytkownika (2026-09-06): obliczenia połączeń są czasochłonne; ważniejsze są typowe, technologiczne i bezpieczne
połączenia bez zbędnych żeber oraz dobra statyka globalna i koordynacja. CS ma rozpoznawać geometrię i proponować miejsca
połączeń powiązane ze statyką („projektuj globalnie jak rzeźbiarz metodą kolejnych przybliżeń”). Około 80 % połączeń można
przyjąć z katalogu i sprawdzić z siłami wewnętrznymi, tylko część wymaga podejścia indywidualnego (IDEA StatiCa); nie budować
kolejnego MES. Hipotezy klas: połączenie na ~80 % nośności elementu na zginanie w jednym kierunku, 60 % i 60 % dwukierunkowo;
stężenia na 60 % nośności przekroju na rozciąganie (wpływ wyboczenia). Jedno połączenie dla HEA200 na ~80 %, drugie na 100 %,
reszta do analizy szczegółowej; grubości blach czołowych i śruby skalują się w serii (IPE), więc katalog można raz policzyć
w IDEA i trzymać bazę. CS ocenia istniejącą konstrukcję z IFC: koszt, jakość, prostsze rozwiązania; siłą CS jest klasyfikacja
i baza geometrii. Procenty to hipotezy, nie zatwierdzone parametry. Koszty spawania w module kosztów (ściegi, starty, NDT).

## IDEA StatiCa Connection przez REST API: idea_setup, licencja w finally, wersja klienta, co API nie robi, wyniki CBFEM
`python tools/idea_setup.py` (5 kroków: wersje IDEA, klient Pythona, serwis REST, podpięcie, odczyt szablonu z instalacji;
`--sprawdz`, `--wersja 25.0`). Bez klucza i konta: serwis `IdeaStatiCa.ConnectionRestApi.exe` chodzi lokalnie. Dwie rzeczy:
(1) serwis rezerwuje miejsce licencyjne (`ReserveSeat`) i zapomniany zjada licencję całemu biuru, dlatego w `tools/idea_api.py`
żyje tylko wewnątrz `with idea_api.polaczenie() as api:` i gaśnie w `finally`; (2) wersja klienta = wersja IDEA
(`ideastatica_connection_api==25.1.*` → API 3.0, `25.0.*` → 2.0); REST jest w 25.0 i 25.1, a 23.1 i 24.0 to droga IOM + .NET
(nierobiona). Dokumentacja: `IdeaRS.OpenModel.xml` w instalacji (3 623 składowe IOM), developer.ideastatica.com, SDK na
GitHubie. API nie ma endpointów dodawania blach, śrub, spoin, cięć: geometria tylko z szablonu (`apply_template`), propozycji
z biblioteki albo importu IOM z `ConnectionData` (`tools/idea_iom.py`; cztery rzeczy, których nie da się zgadnąć). Wyniki CBFEM
przez REST: kontrole per komponent (`get_results`), surowy JSON (`get_raw_json_results`, JSON w JSON), geometria sceny; map
naprężeń na siatce nie ma — jako obrazy z raportu. Schemat `idea` (baza `cs_idea`): węzły biura z plików `.ideaCon`, galeria IFC
wszystkich węzłów obok siebie.

## Siły z RFEM 6 do IDEA: konwencja znaków, który koniec pręta, sonda szablonu, wsad, gotowy ideaCon, matcher węzłów
Sedno: eksport RFEM „Member Hinges | Forces” nie mówi, którym końcem pręt dochodzi do węzła, a bez tego nie da się przeliczyć
znaków; informację trzeba dobrać z modelu. Narzędzie: `python tools/rfem_do_idea.py --sily eksport.xlsx --przegub 121 --top 4`
(skrajne przypadki z wartościami odpowiadającymi; `--mapa "B2=111"` przydział pozycji, `--globalne`, `--tylko-zgodne-wezly`,
`--szablon-sonda odczyty.csv`, `--szablon-iom iom_10.xml` = osie belek szablonu wprost z `.ideaCon`). Droga pewna: zmierzyć
szablon sondą i rzutować siły na jego osie; trzy korekty starszej drogi; przydział pozycji musi być geometryczny; obrót pręta
wokół osi i konwencja pręta dokładnie pionowego zmierzone; niezrównoważenie w IDEA rzędu 0,1 kN i 0,03 kNm jest własnością
wyników, wartości o rząd wyższe = błąd konwencji albo niekompletny eksport (porównać liczbę przegubów z modelem). Gotowy plik
`.ideaCon` z siłami bez ręcznego importu arkusza: `python tools/idea_wsad_wpisz.py --szablon T6_T7.ideaCon --wsad wsad.csv
--wyjscie ...`. Wyniki dla typu połączenia z eksportu XLSX: `tools/rfem_obwiednia_przegubow_xlsx.py --przeguby 101,1101`.
Dopasowanie końców prętów modelu do galerii węzłów IDEA (`idea_match`, `tools/idea_dopasuj_wezly.py --model-wsp 65
--model-galeria 78`): bramka równości profilu, przypisanie węgierskie blach, multiset odległości otworów, próg 0,55;
balustrady i drabiny wycięte z puli. Pułapki w `handbook/pitfalls/rfem_idea_sily.md`.
