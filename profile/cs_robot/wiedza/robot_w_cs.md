# Autodesk Robot w CS-check: COM z Pythona, jednostki, przekroje, osie, podpory, narzędzia, most MES → Robot

## Połączenie z Robotem przez COM: Dispatch zamiast GetActiveObject, Interactive, druga kopia, wersje 2017 i 2025
`win32com.client.GetActiveObject("Robot.Application")` NIE działa (zweryfikowane 2026-07-27 na RSA 2025): obiekt w ROT pod
CLSID Robota nie wystawia IDispatch, wynik MK_E_UNAVAILABLE. Działa `Dispatch("Robot.Application")`: klasa jest MULTIPLEUSE,
więc przy działającym Robocie zwraca ISTNIEJĄCĄ instancję (ten sam PID). Bezpieczeństwo „nie uruchom mi drugiego Robota”
bierze się z warunku na PROCES przed wywołaniem COM (`tools/robot_pull.py`, `tools/robot_com.py::procesy_robota`); przy wielu
Robotach COM oddaje pierwszą zarejestrowaną instancję, niekoniecznie aktywne okno. `robot.Interactive = 0` wyłącza
przerysowania i okna dialogowe na czas operacji hurtowych (ustaw 1 na końcu). `Nodes.Count` / `Bars.Count` nie istnieją
(`GetAll()`); `Project.SaveToFile` nie istnieje (`Project.SaveAs(path)`). Konsola Windows (cp1250) pada na znakach `—`, `→`, `³`
w nazwach: `sys.stdout.reconfigure(encoding="utf-8", errors="replace")`. Wersje: API jest addytywne, Robot 2017 (Version 16.0)
4805 stałych, 2025 (25.0) 5020, DISPID-y 2531 wspólnych metod identyczne; wszystkie 55 stałych CS są w 2017. Pułapka to cache
pywin32 (`EnsureDispatch` nie odróżnia wersji: ta sama GUID i wersja tlb). Test na żywo 2026-10-23: `Dispatch` w trakcie startu
Robota uruchamia drugą, ukrytą kopię (rodzic DcomLaunch), a Robot uruchomiony jako dziecko serwera CS ginie razem z serwerem
(start przez `explorer.exe <robot.exe>`). Robot 2017 w programach Marka: umiejętność `mkk`.

## Jednostki API Robota: SI (Pa, N/m³, m, N, Nm), Gamma w stopniach, konwersje do mm i z radianów
API Robota pracuje w SI, nie w jednostkach wyświetlanych w oknie: naprężenie i moduł Younga w Pa (S235: `E = 2.1e11`, nie 210000),
gęstość jako ciężar właściwy w N/m³ (`RO ≈ 77010` = 7850 × 9,81, nie 7850; `Kirchoff` = G w Pa), długość w m, siła w N, moment
w Nm. `IRobotBar.Gamma` jest w STOPNIACH, a `member.rotation_angle` w RFEM w radianach: `b.Gamma = rad * 180 / π`; bez konwersji
π/2 ląduje jako 1,57° w kolumnie „Obrót”. Konwersja RFEM 5 → RFEM 6 potrafi wyzerować `rotation_angle` (flaga `is_rotated`).
CS: `robot_pull.py` mnoży ×1000 do mm; `src/core/units.py` (`APLIKACJE["robot"]`); błąd jednostek daje sztywność 1000× złą bez
komunikatu, dlatego po eksporcie porównuje się kilka współrzędnych i sprawdza sumę reakcji z wypadkową obciążeń (MKK).

## Katalog przekrojów w Robocie: LoadFromDBase bez błędu, ShapeType, polskie nazwy rur, NS, złożone 2 C 300
`IRobotBarSectionData.LoadFromDBase(name)` nie zgłasza braku: zostawia `AX = 0` i `ShapeType = 0`; zawsze sprawdzaj
`sd.ShapeType != 0` (lepsze niż `GetValue(I_BSDV_AX) > 0`), a przy chybieniu przejdź na przekrój parametryczny, RAW albo
zaślepkę. Domyślny katalog europejski używa POLSKICH nazw rur (RK kwadratowe, RP prostokątne, RO okrągłe); nazwy z RFEM/IFC
(SHS, RHS, CHS) trzeba przekształcić (§3.1). Rejestr `ShapeType` i indeksy `I_BSDV_*` zdekodowane z żywej sondy (§3.2–3.3);
przekroje złożone `2 C 300` nie mają wymiarów, ale da się je wyprowadzić (§3.3.1); przekroje nietypowe parametryczne (NS)
i parametr wysokości zmierzony 2026-10-03 (§3.4). Dostęp do pręta: `Bars.Get(no)` zwraca bazowy `IRobotDataObject`, trzeba
rzutować `wc.CastTo(b_raw, "IRobotBar")` przed `SetLabel(I_LT_BAR_SECTION, ...)`. Mapowanie przekroju CS → katalog Robota /
parametryczny / sztywny robi `mes_static_to_robot.przygotuj_model` (kontrola bez Robota w zakładce MES → Robot).

## Osie lokalne prętów: GetLocalMtx, nigdy z Gamma, znak momentów, migawka osi w przebiegu, ortonormalność
`IRobotBar.GetLocalMtx()` zwraca lokalny układ pręta jako 9 liczb wierszami (e_x, e_y, e_z w układzie globalnym); `GetLCS()`
to inna metoda z argumentem. To ta macierz niesie znak każdego wykresu momentów: siły wewnętrzne (`Forces.Value`)
i przemieszczenia prętów (`Displacements.Value`, obracać `u_glob = u_lok @ LCS`) są w tych osiach. Nie odtwarzać ramki z Gamma
ani z `core.element_placement.transform_4x4` (ta macierz CS kładzie lokalne +Z wzdłuż osi i ignoruje Gamma): zmierzone na KOT
— osie z Gamma błędne dla 468 z 1855 prętów. Osie należą do BIEGU, nie do modelu (ten sam pręt po offsecie albo zmianie Gamma
dostaje inny LCS), więc CS trzyma migawkę obok przebiegu (`static_run.raw_files['osie_lokalne_prety_f64']`, float64 [n, 9]);
czytać w tej samej sesji COM co wyniki i bramkować: |M·Mᵀ − I| ≤ 4,4·10⁻¹⁶, det = +1 dla 1855/1855 prętów. Implementacja:
`tools/robot_sily.py::przejrzyj_prety`.

## Podpory i zwolnienia: konwencja DOF odwrócona, flagi -1, sprężyny, kombinacje I_CBT, grupy, panele, ciężar własny
Najbardziej błędogenna część API. `IRobotNodeSupportData.UX/UY/UZ/RX/RY/RZ`: niezerowe = ZABLOKOWANE, 0 = wolne;
`IRobotBarReleaseData.{Start,End}Node.UX…`: niezerowe = ZWOLNIONE, 0 = zablokowane. Robot oddaje flagi jako `-1` (VARIANT_TRUE),
nie `1`: testuj `bool(flag)`, warunek `flag == 1` po cichu zrobi model bez podpór. Pręt bez typu zwolnienia jest w pełni sztywny
(same zera). Sprężyny: `KX/KY/KZ` (N/m) i `HX/HY/HZ` (Nm/rad) na podporach; zwolnienia to enum `IRobotBarEndReleaseValue`.
`Cases.CreateCombination` bierze jako trzeci argument `IRobotCombinationType` (`I_CBT_*`), nie `IRobotCaseType` (`I_CT_*`);
oba enumy kolidują na wartości 1. Grupy: `Structure.Groups` (nie `Selections`), `Groups.Create` ma trzeci argument STRING;
`Selections.CreatePredefined(k)` to enum częściowy. Panele (płyty, ściany): pełna sekwencja aktywacji, otwory, podłoże
sprężyste jednoosiowe, dane okładziny (§7). Obciążenia prętowe trapezowe: indeksy (§9). Ciężar własny wymaga flagi (§11).
RFEM SOAP jest kruchy (§10). Obciążenia ruchome przez COM nie działają (MKK).

## Wytężenie z Robota na modelu IFC: robot_pull, robot_wytezenie, dopasuj_model_obliczeniowy, robot_sily
Przepływ (docs/how-to/ROBOT_DO_IFC_WYTEZENIE.md): 1) `python tools/robot_pull.py --projekt 1095_WSP --do-core` czyta otwarty
model (tylko odczyt), zapisuje do `static.*`, z `--do-core` przenosi do `core.*` i klasyfikuje, wypisuje `core id` (bez
`--do-core` analizy CS go nie widzą); 2) `python tools/robot_wytezenie.py --wyjscie wytezenie.json` uruchamia weryfikację prętów
(~3 min na 2000 prętów) — wyniki wymiarowania są własnością sesji modułu, a listy prętów modułu nie da się ustawić z zewnątrz,
więc RAZ ręcznie w Robocie: Wymiarowanie → Wymiarowanie stali/aluminium → Weryfikacja prętów → Pręty `all` → przypadki →
Obliczenia; narzędzie porównuje liczbę policzonych prętów z liczbą wymiarowanych i przy rozjeździe przerywa bez pliku;
3) `python tools/dopasuj_model_obliczeniowy.py --obliczeniowy 72 --fizyczny 65 --zapisz --powiaz-prety --wytezenie wytezenie.json`
wyznacza i zapisuje przesunięcie układów, dopasowuje pręt do pręta, nadaje symbol splotu, wpisuje wytężenie po obu stronach;
4) `--eksport-para IFC_export/MOJ_PROJEKT` daje dwa pliki IFC (obliczeniowy przesunięty do układu fizycznego) do obejrzenia
kolorami; 5) opcjonalnie `python tools/robot_sily.py --core-id 114`: siły przywęzłowe w stacjach (2/3/7/11 wg długości pręta),
przemieszczenia, reakcje → przebieg `static.static_run`; na Układach pasek „Wykresy” → źródło Robot. Ograniczenia: wytężenie
to zdjęcie stanu; przesunięcie nie nałoży modeli z osią na górze belki (`40 + h/2`); pełny eksport WSP 197 MB
(`--z-dopasowanymi` kilkanaście MB); modele powłokowe nie przechodzą. `python tools/robot_pull.py --sprawdz-tylko` sprawdza relacje
pręt → przekrój → materiał; pierwszy raz na maszynie `pip install pywin32` (robi to też `INSTALUJ.bat`).

## Most MES → Robot w toolkicie (mes_robot): Model, Most, Wyślij, Wyniki, ochrona sesji projektanta, okna błędów
Zakładka MES → Robot wysyła model obliczeniowy MES modelu CS do działającego Robota 2025 jednym kliknięciem i przynosi wyniki.
Cztery kroki: Model (lista modeli projektu ze stanem MES), Most (lista kontrolna od najtańszej: system → pywin32 → instalacja
z rejestru → proces → główne okno → podpięcie COM → zapisany plik → kopia sesji `T2_stan.json`; przyciski Uruchom Robota, Zapisz
pusty projekt, Przywróć mój model, Zamknij ukryte), Wyślij (opcje: odczyt wyników do CS, zostaw model otwarty; „Sprawdź przed
wysłaniem (bez Robota)” = bramka mechanizmu numpy + plan budowy, BFO ~2,5 s, bez licencji; błędy modelu blokują zawsze, czerwoną
bramkę można świadomie pominąć), Wyniki z Robota (ΣFX/ΣFY/ΣFZ reakcji, największe przemieszczenia, obwiednie N/Vz/My/Mz
z elementem IFC pręta, przejście do wykresów na Układach). Sekwencja TEST 2: odcisk → kopia `SaveAs` → `New` → buduj → licz →
zapisz → powrót (`tools/mes_static_to_robot.py`, `tools/robot_sily.py --static-id`). Katalog kopii `CS_MES_ROBOT_KOPIE`
(domyślnie `C:/KKZ/Robot_kopie_T2`), pusty projekt w `Robot_puste_CS/`. Okna błędów z katalogiem (`bledy.py`): kod, tytuł,
kroki „co zrobić”, akcje; HRESULT-y: `RPC_E_CALL_REJECTED` = Robot zajęty, `RPC_S_SERVER_UNAVAILABLE` = rozłączony. Jeden wątek
COM z `CoInitialize`. Przeszło na żywo (2026-10-23, BFO 2252 węzły / 2534 pręty): stan mostu, start, pusty projekt, bramka,
odcisk, kopia, `New`, przerwanie → `ROBOT_ROZLACZONY`. NIE sprawdzone bez licencji: budowa do końca, obliczenia, odczyt sił,
powrót do kopii. Obliczenia Robot WSTRZYMANE decyzją użytkownika 2026-09-25 (TEST 2 nieaktualny po A′), przeliczone 2026-10-05.

## RFEM 6 → Robot 2025 (tools/rfem_to_robot): stary klient SOAP, osobna instalacja, mapowanie encji, status
Jednorazowy transfer żywego modelu analitycznego z RFEM do Robota: węzły, pręty, przekroje, materiały, podpory, przeguby,
obciążenia i kombinacje (`python tools/rfem_to_robot/convert.py --help`; pełne odwzorowanie encji, fazy i pułapki w MAPOWANIE.md).
To NIE jest to samo API co reszta CS: stoi na starym kliencie SOAP (pakiet `RFEM`, `from RFEM.initModel import Model`), a nie na
API II (`dlubal.api`); żadne środowisko CS go nie ma (ani `.venv`, ani `tools/rfem_client/.venv`), więc uruchomienie wymaga
osobnej instalacji klienta SOAP i włączonego w RFEM starego API — zaplanuj to jako osobny krok. Pochodzi z repozytorium
`rfem-robot-bridge` jako ukończona wcześniejsza ścieżka; do CS trafił 2026-07-28 przy likwidacji podmodułu; nie jest rozwijany
(pierwszy krok, gdyby był potrzebny: przepisanie na API II). Kod jest dosłowną kopią bez `black`, żeby diff wobec źródła pokazywał
różnice merytoryczne. Kopiowanie i edycję fragmentu ŻYWEGO modelu Robota (kratownice, przesunięcia) zrobiono raz ad hoc przez COM
(sesja 2026-08-24, 4 MWS → 10 kopii); skryptów nie utrwalono w `tools/` — własny skrypt wzorem `robot_pull.py`, kontrola odczytem.
