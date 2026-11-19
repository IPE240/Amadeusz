# CS-check: obsługa programu (instalacja, viewer, toolkit, projekty, serwisy, eksporty)

## Co to jest CS-check (Check Structures), dwa programy: CS i viewer IQSteel
CS-check to silnik geometrii i baza danych konstrukcji stalowych. Wczytuje model IFC (z Tekli, Advance Steel, Revita)
i rozkłada go na elementy: pręty, blachy, śruby, spoiny; rozpoznaje profile z katalogu (4559 przekrojów), materiały,
połączenia, zespoły i powtarzalność; wczytuje żywy model z RFEM i trzyma go obok modelu z Tekli w jednym układzie;
porównuje wersje; eksportuje wzbogacony IFC, DXF, PDF, zestawienia. Repozytorium ma dwa programy: CS (backend Python,
FastAPI, PostgreSQL) i viewer IQSteel (`viewer/`, JS/WebGPU). Liczby pochodzą z CS, piksele z viewera.
Projektant pracuje przez Claude Code w katalogu repozytorium (plik wejściowy ODS.md) albo klikając w viewerze i toolkicie.
CS nie liczy statyki ani nośności: obliczenia zostają w RFEM i Robocie, a CS czyta ich modele i wyniki.

## Instalacja CS-check: INSTALUJ.bat, /instaluj, Python 3.12, Node, PostgreSQL, bez administratora
Pobierz repozytorium (`git clone` albo ZIP) i uruchom dwuklikiem `INSTALUJ.bat` albo w Claude Code wpisz `/instaluj`.
Trwa 10–20 minut (najdłużej pobranie PostgreSQL ~323 MB i bibliotek viewera). Nie potrzeba administratora: Python 3.12,
Node.js 22 i PostgreSQL 15 przychodzą w wersjach przenośnych do katalogu repozytorium (`.localpy\`, `.localnode\`,
`.localdb\`), jeśli w systemie ich nie ma; nic nie ląduje w PATH ani w rejestrze. Instalator można przerwać i uruchomić
ponownie: zrobione kroki wypisują `POMINIETO`. Ten sam instalator doinstalowuje brakujące rzeczy na komputerze, który CS
już ma (`git pull`, potem `INSTALUJ.bat`); dane i projekty zostają. Deweloper: `INSTALUJ.bat -Web -Dev`.
Miejsce: ok. 2,6 GB sama instalacja, dane projektów rosną osobno (trzy duże modele ≈ 4,5 GB w bazie).

## Uruchomienie: URUCHOM.bat, karta startowa, viewer :5000, toolkit :8000, gdy coś nie działa
`URUCHOM.bat` pokazuje kartę startową „CS się uruchamia…” (baza → serwer → widok) i otwiera dwie karty przeglądarki:
viewer 3D `http://localhost:5000` (okno bazowe: projekty, modele, podgląd 3D, Układy, eksport, okno agenta) i toolkit
`http://localhost:8000` (tabele, eksporty, narzędzia). Po starcie viewer otwiera ostatni projekt.
Gdy coś nie działa: „nie można połączyć” = program nie działa, uruchom `URUCHOM.bat`; „could not connect to server” =
baza nie działa, `powershell -File tools\localdb.ps1 start`; instalacja się wysypała = uruchom `INSTALUJ.bat` ponownie;
Claude gubi się w repozytorium = „przeczytaj ODS.md i trzymaj się go”. Pełny runbook: docs/how-to/TROUBLESHOOTING.md.
Tryb deweloperski i testy: docs/how-to/run-locally.md.

## Zakładki toolkitu (IFC Toolkit): Data group, Table, Export, Statyka, Nesting, Assembly, Koszty, Jobs
Data group: import plików IFC i panel Serwisy obliczeniowe. Table: tabelaryczny podgląd elementów z filtrami i selekcjami.
Export: eksporty IFC/DXF (zastąpione oknem Eksport w viewerze). Statyka: powiązanie modelu obliczeniowego (Robot/RFEM)
z modelem IFC. Nesting 1D/2D: rozkrój prętów na sztangach i blach na arkuszach. Assembly: zespoły (elementy wysyłkowe),
pozycje, wzorce powtarzalności, arkusze rysunkowe. Koszty: moduł kosztów wytworzenia. Jobs: kolejka zadań w tle.
Settings: ustawienia interfejsu. Zasada: analizy liczą się na żądanie i pokazują raport pokrycia (co policzono, co pominięto
i dlaczego); liczby bez wyjaśnienia to błąd, nie oszczędność miejsca.

## Serwisy obliczeniowe: 7 grup, Licz, Dolicz, Licz od nowa, Przerwij, kilka modeli naraz
Panel Serwisy obliczeniowe (toolkit → Data group, kolumna Serwis przy modelu, np. `2/7`). CS liczy ok. 35 modułów,
pogrupowanych w 7 grup: Wczytanie i podział (elementy, geometria 3D, krawędzie cięć, obiekty i segmenty dylatacyjne),
Klasyfikacja (pręt, blacha, łącznik, spoina; profil i materiał z katalogu; elementy podobne), Pręty i blachy (przekroje, osie,
cięcia, kontury, powierzchnie, kraty, unikaty), Zespoły (elementy wysyłkowe, element wiodący, typy blach, rodziny i wzorce),
Połączenia śrubowe (śruby, otwory, strefy i typy połączeń), Spoiny (styki ścianek i grubości spoin), Układ konstrukcji
(osie, poziomy, płaszczyzny, dachy, role prętów). Licz/Dolicz liczy grupę razem z modułami, od których zależy, gotowe pomija;
„Dolicz wszystko” = wszystkie brakujące; „Licz od nowa…” czyści wyniki (także modułów zależnych); Przerwij zatrzymuje
analizę pliku. Koszty, rysunki, model obliczeniowy i rozkrój liczą się na żądanie w swoich zakładkach.

## Projekty: teczka biura, nazwa numer_SYMBOL, ifc/, snapshots/, skrypty/, co trafia do gita
Projekt = teczka biura w jednym programie: katalog `projekty/1102_FSM/` na dysku i kafel w viewerze to to samo. Nazwa
`numer_SYMBOL_Pełna nazwa` (numer 3–6 cyfr z wiodącymi zerami, symbol 2–5 znaków); katalog nazywa się krótko (`1102_FSM`),
pełna nazwa w README.md teczki. Numery 9000–9999 są zarezerwowane na projekty testowe (9100_BFO, 9145_KOT, 9189_WSP).
Zakładanie: `/nowy-projekt 1102_FSM_Pełna nazwa` w Claude Code albo „+ Nowy projekt” w viewerze. W teczce: `README.md`,
`ifc/` (modele z Tekli), `snapshots/` (pobrania z RFEM), `skrypty/`, `wyniki/` lub `eksport/`. Program obserwuje `ifc/`:
wrzucony plik w kilka sekund staje się nową wersją (bez analizy; „Analizuj w CS” klikasz sam); komentarz rewizji w pliku
`<nazwa>.ifc.rewizja.txt`; w README program pisze tylko sekcję „Modele” między znacznikami. Do gita trafiają README
i `skrypty/`; modele klientów (`*.ifc`, `*.rf6`, `*.rtd`), eksporty i klucze API nie. Korzeń teczek: zmienna `CS_PROJEKTY`.

## Wersje i zmiany: v1 v2, zmiana = powód, Cofnij do vN, Cofnij tę zmianę, dziennik projektu
Każdy plik ma historię wersji nadawaną przez program (nowy plik z Tekli pod tą samą nazwą = nowa wersja; ten sam plik drugi
raz = nic). Wersja nigdy nie ginie; menu ⋯ → „Wersje…” pokazuje kto, kiedy, powód, kartę pliku, sumę kontrolną. Zmiana ma:
kto (osoba, agent w imieniu osoby, system), czym, dlaczego i na czym (wersja przed → po), z pozycjami „+a / −b / ~c”.
„Cofnij do vN” tworzy nową wersję o stanie N (bez ponownej analizy); „Cofnij tę zmianę” to zmiana odwrotna, dozwolona, gdy
późniejsze zmiany nie dotykają tych samych elementów. Zakładka Dziennik: jeden wpis na zdarzenie (plik, wersja, moduł,
widok, eksport, polecenie agenta, kosz), kto i kiedy; tylko dopisywany, filtry rodzin zdarzeń, „Pokaż w 3D”.

## Eksport: okno Eksport w viewerze, IFC z psetami, Układy, Połączenia, Spoiny, DXF osi, paczki
Okno „Eksport…” ma trzy wejścia: ten model (menu ⋯ pliku), wybrane modele (zaznaczone na liście) i selekcja 3D (prawy klik
→ „Eksport z selekcji (CS)…”). Grupy produktów: IFC z bazy CS z psetami analizy (preset Standardowy = profil `user`;
Własny = całe moduły psetów), Układy (rysunki osi i poziomów do sceny, bez pliku), Połączenia (strefy ze śrubami i osie
połączeń jako nakładki IFC), Spoiny (nakładka IFC, osobno), Dane prętów (osie prętów, lokalny układ, model połączony MES jako
DXF, osie jako strumień), Paczki (pakiet modelu `.csx`). Dwa czasowniki: „Zapisz plik” (artefakt pod modelem, pobranie)
i „Pokaż w viewerze” (produkt ładuje się do sceny obok modelu). Nesting i assembly żyją w zakładkach toolkitu.

## Paczka projektu .csx: Wynieś projekt (project, archive, client), Wczytaj projekt, wymiana między stanowiskami
Paczka projektu to jeden plik `.csx` (SQLite z manifestem i sumami kontrolnymi), który przenosi cały projekt: pliki, wersje
z historią, widoki, adnotacje, przypięte eksporty i policzone wyniki, bez ponownego liczenia i bez serwera.
Profile: project (domyślny; bajty IFC jako wymagania), archive (plus kosz, bajty źródłowe, wszystkie eksporty), client (tylko
wersje udostępnione, bez poleceń i prywatnych widoków, do wysłania na zewnątrz). Plik trafia do `CSX_export/`.
„Wczytaj projekt (.csx)” na liście projektów: te same numery wersji i identyfikatory co u nadawcy; treści, które stanowisko już
ma (ten sam hash), są dowiązywane; paczka zbudowana na starszym stanie projektu jest odrzucana („nieaktualny push”).
Format: docs/reference/CSX_FORMAT.md.

## Koszty: Skala Kozieła, zł/kg, tryb Porównawczy i Produkcyjny, cennik, stanowiska
Moduł kosztów liczy obiektywny koszt wytworzenia [zł/kg] jako koszt własny wytwórni bez marży, żeby lepsze rozwiązania
konstrukcyjne wygrywały na liczbach (wykład „Skala Kozieła”). Strony w kolejności produkcji: Zakup materiału (cennik stali,
magazyn, śruby), Przygotowanie detali (cięcie, wiercenie, znakowanie), Scalanie (heftowanie na stole), Spawanie (pełne
spawanie + badania NDT); w budowie: malowanie, robocizna śrub, kratki. Tryb Porównawczy = zużycie netto 1:1 bez odpadu
i magazynu (do porównań wariantów); Produkcyjny = jak zamawia wytwórnia (brutto z rozkroju 1D i 2D, magazyn, styki
warsztatowe). Cennik i park maszynowy (piła, plazma, stół scalania, stanowisko spawalnicze) w zakładce Zakup materiału;
klon cennika = wariant wytwórni; parametry mają prowenencję seed / ręczny / statystyka.

## Okno agenta AI w CS: Agent AI, token cst_, MCP, karta „Agent chce”, Ctrl+Alt+I
Przy prawej krawędzi toolkitu i viewera jest okno agenta (✦ Agent AI). Piszesz po swojemu („zaznacz słupy HEM550 w OB1-S4
i pokaż je w Układach”), a agent (Claude Code na repozytorium CS) odpowiada i steruje programem: zaznacza elementy,
przełącza zakładki, otwiera rysunki Układów, uruchamia przygotowaną pracę. Razem z wiadomością idzie lekki kontekst
(zakładka, projekt, plik, zakres, liczba zaznaczonych). Zaznacz/przełącz/pokaż wykonuje od razu; zmiany danych (analiza,
skrypt, zmiana pliku) wymagają karty „Agent chce: …” z przyciskami Wykonaj / Odrzuć (brak decyzji w 10 min = odmowa).
Agent wchodzi tokenem `cst_…` (`python tools/cs_token.py create --name "Claude Code" --scopes read,workstate,change:propose`),
plik `.mcp.json` w katalogu projektu. Skróty: Ctrl+Alt+I zwija okno, Enter wysyła, Shift+Enter nowa linia.

## Co CS już umie: ODS_MOZLIWOSCI (MA, PRAWIE, NIE), recepty biura, dziennik sesji
`handbook/ODS_MOZLIWOSCI.md` to indeks dla projektanta w trzech stanach: MA (gotowe narzędzie), PRAWIE (dane są, brak
polecenia), NIE (CS tego nie ma). Przykłady MA: import IFC z pełną analizą, `python tools/rfem_pull.py --zywy --core --static`,
`python tools/robot_pull.py --sprawdz-tylko`, eksport osi prętów do DXF (`tools/export_bar_axes_to_dxf.py`), kontury blach do
DXF, złożenie IFC z psetami (`compose-ifc`), GLB, `.csx` (`tools/cs_pack.py`), dopisanie psetów do kopii IFC z tabeli
(`tools/ifc_dopisz_psety.py`), obwiednie z RFEM (`tools/rfem_obwiednia.py`), strefy połączeń i spoiny jako nakładki.
PRAWIE: zestawienie stali (dane są w `core.pret`, to zapytanie SQL), porównanie modeli, podpory z RFEM 6. NIE: analiza MES,
obciążenia z RFEM, IDEA 23.1/24.0 (bez REST). Recepty biura: `handbook/ODS_recepty.md` („zapisz to jako receptę”). Na koniec
sesji Claude Code powstaje notatka w `handbook/ODS_dziennik/` bez danych klientów (wyłączenie: `CS_ODS_DZIENNIK=0`).

## Czego CS nie robi (limity): statyka, wymiarowanie, walidacja IFC, edycja modelu, żelbet
CS zakłada konstrukcje stalowe; żelbet, drewno i aluminium trafiają jako `klasa_p1=NULL` albo PRĘT/ŻELBET (to świadomy zakres,
nie błąd). Nie liczy obciążeń ani naprężeń: statyka, wytrzymałość, wymiarowanie EC3, stateczność, obliczenia połączeń
i dynamika są poza zakresem (wyjątki: obciążenie śniegiem PN-EN 1991-1-3 w zakładce Obciążenia i model obliczeniowy
budowany dla RFEM/Robota). Nie sprawdza poprawności IFC (clash detection, IDS, kompletność właściwości) — do tego
`ifcopenshell.validate`, Solibri, BIMvision. Nie edytuje IFC: pipeline czyta plik i pisze do bazy, eksport tworzy nowy plik
z dodanymi właściwościami. Nie projektuje samodzielnie.
