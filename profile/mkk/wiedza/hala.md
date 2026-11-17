# Hala — kreator modelu hali stalowej (program Marka Kowolika)

## Co to jest Hala, do czego służy, jak uruchomić (HALA.bat, INSTALUJ.bat)
Hala to okienkowy kreator modelu hali stalowej: z parametrów (rozstawy osi, nawy, typ kratownicy, dach, attyki, uskoki)
powstają pręty i węzły. Program liczy obciążenia (śnieg, wiatr), ma własny MES, eksport do Robota 2017 i RFEM 6,
wymiarowanie EC3 i SGU, węzły w IDEA StatiCa, żelbet i fundamenty. Kod: `kod/hala/` w repozytorium MKK (Python 3.12).
Uruchomienie: raz na komputerze `INSTALUJ.bat` (zakłada `.venv`, instaluje biblioteki, importuje bazę przekrojów z Robota),
potem `HALA.bat`. Tryb deweloperski w przeglądarce: `python -m tools.hala.serwer` → http://127.0.0.1:8790/.
Do liczenia potrzebne są licencje Robot / RFEM / IDEA StatiCa; klucz API RFEM (`.dlubal_api_key`) nie jest w repozytorium.

## Plik projektu Hali: .mkonH, .mkhal, zapis, ostatnia praca
Projekt zapisuje się jako `<nazwa>.mkonH` (menu **Plik**: Nowy, Otwórz, Ostatnie, Zapisz, Zapisz jako), domyślnie
w folderze `projekty\`. Stare pliki `.mkhal` też się otwierają. Ostatnia praca jest pamiętana na bieżąco w
`~/.cs_hala/praca.json` (nie przechodzi z katalogiem na inny komputer, dlatego projekt trzeba zapisać do pliku).
Dwuklik na `.mkonH` działa po jednorazowym uruchomieniu `SKOJARZ_PLIKI_MKONH.bat`. Kopiując katalog programu,
`.venv` (ok. 400 MB) można pominąć; `INSTALUJ.bat` odtworzy go.

## Budowa okna Hali: działy, zakładki, Ctrl+K, panel Podsumowanie, Wybór
Zakładki po lewej mają trzy poziomy: dział → zakładki działu → układy U1, U2… Działy: **GEOMETRIA**, **ODDZIAŁYWANIA**,
**OBLICZENIA** (statyka, dynamika), **KOMBINACJE**, **WYNIKI**, **WYMIAROWANIE** (pręty stalowe, węzły IDEA, żelbet,
fundamenty), **WZMOCNIENIA**; 📋 = budowa programu (decyzje, podsumowania), ⚙ = opcje.
**Ctrl+K** albo 🔍 = szybkie przejście do zakładki; „p 123” / „w 45” zaznacza pręt / węzeł w widoku.
Pasek widoku 3D: tryb geometrii, Ctrl+Z / Ctrl+Y, widoki, **Pokaż ▾** (osie, osie lokalne, zwolnienia, okładziny, numery).
Panel po prawej: **Podsumowanie** (masa, stan obliczeń MES / nośność / SGU), **Wybór** (pręt lub węzeł, jego układ i typ),
**Uwagi**. Raport obliczeń (HTML) i zestawienie stali (CSV): **Plik → Raport obliczeń**. Eksport osi do DXF: **Plik → Eksport geometrii do DXF**.

## GEOMETRIA: osie, nawy, układy poprzeczne U1 U2, kratownice, płatwie, podciągi
**Geometria ogólna**: materiał, rozstawy osi poprzecznych zapisane jako `liczba*rozstaw` (np. `7.5, 5*6, 7.5`), siatka naw
(szerokość, zakres osi od–do, wspólny słup albo oddzielne słupy), dylatacje (podwójny słup albo ukryta), który układ
poprzeczny stoi w której osi. **U1, U2…** (układy poprzeczne): dla każdej nawy kratownica, profil, ściana szczytowa
z ryglem albo z kratownicą, układ słupowo-ryglowy; „Wzorowany na: U1” kopiuje wzorzec. **Płatwie**: dla każdej nawy
przekrój i ptaszki węzłów pasa górnego (wszystkie / co 2. / co 3.). **Podciągi**: kratownica podłużna w rzędzie słupów
(Warren, Pratt, Howe albo profil pełnościenny). **Strop**, **Belki stropu**, **Podciągi stropu**: konstrukcja poniżej dachu.
**Typy**: które układy mają wspólne typy prętów do wymiarowania (grupy W1, W2…).

## Stężenia, ryglówka, obudowa, attyki, suwnice w Hali (metoda okienkowa)
**Stężenia**: krzyżulce X w wybranych przęsłach (`1, -1` = pierwsze i ostatnie): dachowe, pasa dolnego, ścienne, tężniki
pionowe dachowe i stropowe, rygielki; „+ stężenia standardowe”. Bez stężeń hala nie przenosi obciążeń wzdłuż i Robot
pokazuje niestateczność. **Metoda okienkowa** (Ryglówka, Stężenia): ⊞ „wskazuj okna w widoku” pokazuje pola płaszczyzn;
klik okna zaznacza je na żółto, w panelu **Wybór** wybierasz kształt (X, V, K, portale) i przekrój; pędzel = wiele okien
naraz; gumka usuwa stężenie w oknie. **Ryglówka**: rygle ścienne na wysokościach (np. `2, 4, 6`), podłużna i szczytowa.
**Obudowa**: słupki ścian podłużnych. **Attyki**: podłużne i szczytowe, wierzch względem innej attyki, stężenia attyki.
**Suwnice**: droga suwnicowa (nawa, osie, poziom główki szyny), suwnice wg PN-EN 1991-3, „▶ policz obwiednię”.

## Okładziny i obciążenia w Hali: śnieg, wiatr, dobór blach i płyt
**Okładziny** (jak okładziny w Robocie / powierzchnie przenoszące obciążenie w RFEM): dach, ściany podłużne i szczytowe,
attyki, stropy; kierunek rozkładu auto (dach na płatwie albo kratownice, ściany na ryglówkę); „+ okładziny standardowe”.
**ODDZIAŁYWANIA** liczy program półautomatycznie z geometrii: **Śnieg** wg PN-EN 1991-1-3 (sₖ ze strefy i wysokości n.p.m.,
S1 równomierny, S2/S3 nierównomierne, S4 kosze, S5 zaspy przy uskokach i attykach, zaspy wyjątkowe SA), **Wiatr** wg
PN-EN 1991-1-4 (strefa, kategoria terenu, pola A–E ścian i F–J dachów, c_pi +0,2 / −0,3). 👁 przy przypadku pokazuje go w 3D.
**Dobór okładzin** (blachy trapezowe, płyty warstwowe): tabela z tablic producentów (Pruszyński, Ruukki), płyty na
rzeczywistych podporach, σ_T od temperatury (EN 14509), karta TrayPan, sprawdzenie programem Pruszyński BP.

## OBLICZENIA w Hali: eksport do Robota 2017, własny MES, wyniki, porównanie
Zakładka **Robot 2017**: „▶ Eksportuj do Robota, policz i pobierz wyniki” zakłada w Robocie 2017 nowy projekt, przenosi
węzły, pręty (przekroje z bazy Robota), przeguby, podpory, okładziny, przypadki proste (ciężar własny, śnieg, wiatr), liczy
i pobiera wyniki. Druga droga: „⇢ Tylko eksport (liczę w Robocie)” i potem „⇠ Pobierz wyniki z Robota”. Raport: czasy,
brakujące przekroje, kontrola równowagi (wypadkowa obciążeń = reakcje), niestateczność (przemieszczenia > 1 m).
Kombinacji w eksporcie nie ma. Technika: `tools/hala/robot_com.py` w osobnym procesie Pythona z pythonnet
i `Interop.RobotOM.dll`. Hala ok. 1600 prętów i 35 przypadków liczy się ok. 1,5 min (większość to odczyt wyników).

## Własny MES Hali i zakładka Wyniki: siły N, Vy, Vz, Mx, My, Mz, reakcje, CSV do Excela
**Własny program (MES)**: własne obliczenia statyczne, rama przestrzenna MES (elementy belkowe 3D, przeguby, podpory, obrót
słupów) z tego samego modelu i obciążeń co eksport do Robota; po obliczeniu tabela porównania z Robotem.
**Wyniki**: źródło Robot 2017 albo własny program; wybór przypadku i wielkości (N, Vy, Vz, Mx, My, Mz, |u|), pręty w kolorach
w 3D, postać odkształcona (skala auto / ręczna), największe wartości, reakcje; klik pręta lub węzła pokazuje wartości
w panelu **Wybór** (siły w 0; 0,5; 1 długości). Znaki jak w Robocie. **WYNIKI → ⬇ CSV do Excela**: siły skrajne prętów,
reakcje i przemieszczenia bieżącego przypadku / kombinacji / obwiedni; **Nośność prętów → ⬇ CSV**: wytężenia EC3 i SGU.

## WYMIAROWANIE w Hali: nośność EC3, SGU, dobór przekrojów, pożar, węzły IDEA
**Nośność prętów (EC3)**: wytężenia EC3 i SGU (ugięcia belek L/n, przemieszczenia głowic słupów H/n; słupy H/150 i L/200),
pod spodem **propozycja doboru przekrojów** (najlżejszy przekrój tej samej rodziny), **Dobór iteracyjny** (przyjęcie → MES →
SGU → nośność aż przekroje przestaną się zmieniać), lista **Przyjęte przekroje**. **Pożar** (PN-EN 1993-1-2, orientacyjnie):
temperatury krytyczne i czas nagrzewania, wymagana klasa R w Ustawieniach → Pożar. **Węzły podporowe bez stężeń**: podstawy
słupów w typach, wersje P1–P2 (przegubowe) i U1–U5 (utwierdzone), „▶ Policz w IDEA StatiCa”, ★ najlżejsza spełniająca.
**✎ edytor węzła**: widok 3D, kontrole na żywo (kolizje, śruby, spoiny), korekty blach i spoin, 📄 lista blach, ⬇ DXF blach.
**Żelbet**: płyty (ugięcie z zarysowaniem, ścinanie, przebicie) i fundamenty (stopy, oczepy na palach, płyta MES, dobór najtańszego).

## Baza przekrojów Hali: przekroje.json, import z Robota i RFEM, kolorowe kropki
`baza\przekroje.json` to wspólna baza programu w notacji Robota (`HEA 300`, `RK 100x4`, `RO 219.1x8`); wymiary w mm,
charakterystyki w cm², cm³, cm⁴, masa w kg/m. **Robot 2017**: `python -m tools.przekroje.import_robot` czyta XML
z `C:\ProgramData\Autodesk\Structural\Common Data\2017\Data\Prof` (katalog Polska 2007 i UPE/UPN), obecnie 1253 przekroje.
**RFEM 6**: biblioteka jest zaszyfrowana, więc nazwy potwierdza się przez API: `python -m tools.przekroje.import_rfem`
(z `--wykonaj` zakłada tymczasowy model w RFEM, porównuje pole A z Robotem ±5 % i zamyka bez zapisu).
Kropka przy przekroju: zielona = zgodny z programem docelowym, pomarańczowa = nazwa RFEM niepotwierdzona, czerwona = brak.

## Decyzje i podsumowania budowy programu Hala (📋, DECYZJE.json, D123)
Zakładka 📋 **BUDOWA PROGRAMU → Do decyzji**: założenia przyjęte przy budowie (interpretacje normy, uproszczenia) ze stanem
(do decyzji / zaakceptowane / do zmiany / zrobione) i uwagą; zapis w `DECYZJE.json`. Numery decyzji **D123** pojawiają się
w całej dokumentacji MKK jako odnośniki. **Podsumowania** kolejnych etapów budowy są w `PODSUMOWANIA.json`.
W repozytorium MKK nie ma danych klientów: nazwy projektów zamieniono na PROJEKT_A…PROJEKT_D, biura na BIURO_1, BIURO_2;
nie ma też notatek z norm, map stref ani dzienników budowy programów.
