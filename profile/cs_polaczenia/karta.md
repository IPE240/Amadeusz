# Karta: połączenia w CS-check

## Śruby i otwory (moduł bolt)
Każda śruba z IFC ma jedną oś (`core.bolt_axis`, metry); `core.bolt_group` = łącznik (śruby, kotwy, otwory D38/D45/D51 bez łącznika, `klasa_p2 = OTWORY`); `analysis.bolt_connection` = przebicie (śruba fizycznie przebija element; jedno jądro przebicia `pierce_kernel.py`); `views.bolt_connection_set` = zestaw śrubowy (grupa powiązanych przebić, np. flansza; bez limitu liczby elementów). Otwory w blachach i ściankach prętów: `bolt_hole_on_blacha` / `bolt_hole_on_pret`; bryły otworów (koło, fasolka) z reprezentacji śrub (R18). Powtarzalność detalu: `connection_unique_id`.

## Strefy i typy połączeń (strefa_polaczenia, L14)
Strefa połączenia = element OBLICZENIOWY: wycięty fragment blachy albo ścianki pręta wokół JEDNEGO zestawu śrub, z grubością tej części. Figura: 1 śruba → kwadrat 6·D; 2 śruby → prostokąt rozstaw + 2·3D; ≥ 3 → otoczka wypukła + odsunięcie 3·D (D = średnica śruby); blacha ∩ kontur, pręt w poprzek CAŁA szerokość ścianki. Numeracja pięciowarstwowa: `SP001` (strefa podobna, lustra zwinięte), `P0001` (`polaczenie_code`: strefy i śruby JEDNEGO węzła), `GP001` (`polaczenie_grupa_code`: jednakowe połączenia; KOT 4 031 połączeń w 1 227 typach). `typ_polaczenia`: `doczolowe` (blacha czołowa w stosie albo pręt dochodzący z osią ∥ do osi śrub, ≤ 15°), `zakladkowe` (⟂, ≥ 75°), `skosne` (15–75°), `inne` (jednostronne, brak osi śrub). Eksport: nakładka IFC `/api/exports/ifc/strefy-polaczen?model_id=N`.

## Wspólne krawędzie i spoiny przewidziane (shared_edges, L11)
Krawędzie styków pręt–blacha, blacha–blacha, pręt–pręt wewnątrz zespołu, liczone wyłącznie z warstwy analitycznej (pozy z `assembly_layout`, ścianki profilu, kontury blach), na zespole kanonicznym (instancje dziedziczą). Jedna krawędź = jedna grubość ścianki (dwie blachy po jednej stronie = dwie krawędzie); wyokrąglenia: grubość cieńszej ścianki (środnik); grubość spoiny z reguł `weld_rules.weld_spec` (zasada z wykładu: 0,5 cieńszej ścianki). `detector` (c27, c29 skośne blachy, c30 widelec w rurze, c34 siodło) to napis diagnostyczny; `OGÓLNY` = tylko drabina ogólna. Eksport spoin = sama nakładka (`shared-edges`, `bare=1`), `&bare=0` na tle modelu. Znane luki: spoiny blacha↔pręt w środku długości, pręty pełne, blachy niepłaskie (stożek).

## Spoiny rzeczywiste ze źródeł (feature spoiny, S3a)
Trzy źródła z dokumentacji: model IFC (`IfcFastener`), rysunki (DWG→DXF nałożone na model), uwagi ogólne („spoiny nieoznaczone a = 0,5 t”). Łańcuch `effective_weld_spec`: `zrodlo_spoiny` w kolejności siły: `nadpisanie` (projektant na arkuszu) > `rysunek` > `model` > `detal_typowy` > `uwagi_ogolne` > `cs_regula`; `pewnosc_spoiny`: `pewna`, `przeniesiona`, `z_uwag_ogolnych`, `przewidziana_cs`; znaczniki `rozbieznosc`, `z_innego_zespolu`, `nie_do_spelnienia`, `do_obejrzenia`; stan rozbieżności `obejrzana` / `przyjeta` (`work.spoina_rozbieznosc`). Lista do obejrzenia: `GET /api/spoiny/do-obejrzenia`; toolkit Assembly → przełącznik „Spoiny” → panel. Bez źródeł wynik = `weld_spec` bit w bit.

## Sztywność i katalog połączeń (łuk 2026-10-31)
Cel: sztywność typowych połączeń z geometrii CS bez MES (doczołowe: efekt dźwigni, Mz równorzędny z My, paski nośności; zakładkowe: luz w otworach, podatność na docisk, drugi kierunek pracy blach, mimośrody jednociętych; podstawy słupów: sprężyna obrotowa). W modelu MES sprężyny za flagą (`mes/sztywnosc/`). Filozofia audytu: ~80 % połączeń z katalogu (klasy 60/80/100 % nośności elementu jako hipotezy), reszta indywidualnie w IDEA; raz policzyć katalog w IDEA i trzymać bazę.

## IDEA StatiCa (REST API 25.0/25.1)
`python tools/idea_setup.py` (5 kroków, bez klucza); `with idea_api.polaczenie() as api:` — serwis rezerwuje miejsce licencyjne i gaśnie w `finally`; klient `ideastatica_connection_api==25.1.*` (API 3.0) albo `25.0.*` (2.0); 23.1 i 24.0 bez REST. API nie dodaje blach, śrub, spoin: geometria z IOM, szablonu (`apply_template`) albo gotowego `.ideaCon` (`tools/idea_iom.py`). Siły z RFEM 6 do IDEA: `python tools/rfem_do_idea.py --sily eksport.xlsx --przegub 121 --top 4`, droga pewna = sonda szablonu i rzut na osie; gotowy `.ideaCon` z siłami: `tools/idea_wsad_wpisz.py`. Dopasowanie końców prętów do galerii węzłów IDEA: `tools/idea_dopasuj_wezly.py` (próg 0,55). Wyniki CBFEM przez REST per komponent; mapy naprężeń jako obrazy z raportu. Baza `cs_idea` (schemat `idea`) i galeria IFC węzłów biura.

## Gdzie szukać (repo CS)
| Pytanie o… | Plik |
|---|---|
| wartości i definicje | glossary/polaczenia.md (§3, §11, §23, §30), glossary/spoiny.md (§31) |
| strefy, pięć warstw numeracji, osie stron, pułapki zmierzone | src/features/strefa_polaczenia/README.md |
| krawędzie i detektory spoin | src/features/shared_edges/README.md; WYKLAD_SPOINY_Z_MODELU_ANALITYCZNEGO |
| spoiny ze źródeł | src/features/spoiny/README.md; WYKLAD_SPOINY_ZRODLA; PLAN_SPOINY_ZE_ZRODEL |
| śruby, łączniki, otwory, bryły otworów | src/features/bolt/README.md |
| sztywność, katalog, audyt | WYKLAD_POLACZENIA_DOCZOLOWE_SZTYWNOSC, _ZAKLADKOWE_, WYKLAD_AUDYT_KONSTRUKCJI_KATALOG_POLACZEN, PLAN_SZTYWNOSC_POLACZEN |
| IDEA: połączenie, licencja, wersje, IOM | docs/how-to/IDEA_API.md; siły: RFEM_DO_IDEA_SILY.md; matcher: idea_match/README.md |
