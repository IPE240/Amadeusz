# Plik RFEM 5 (.rf5): co program czyta bez RFEM-a, jednostki, znaki sił

- Plik .rf5 to archiwum (ZIP albo starszy kontener OLE) ze strumieniem modelu, siatką solvera (`Mesh`) i wynikami
  (`Results_Main_V_2`). Program czyta go z dysku — RFEM nie musi być zainstalowany ani uruchomiony.
- Z modelu program czyta: węzły (X, Y, Z w m), linie (węzły), pręty (typ, linia, obrót, przekrój na początku i końcu, komentarz,
  w modelach wyeksportowanych z Tekla Structures identyfikator elementu Tekli — GUID, w wyniku „element Tekli …”),
  przekroje (opis, nazwa biblioteczna, materiał, A, Iy, Iz, It), materiały (opis, rodzaj, E, G, ciężar objętościowy, norma),
  przypadki obciążeń (numer, opis), kombinacje obciążeń (numer, opis, wyrażenie) i kombinacje wyników.
- Długość pręta liczona jest z współrzędnych węzłów linii (dla linii łamanej: suma odcinków). Łuków nie ma.
- Jednostki w odpowiedziach narzędzi: m, kN, kNm, cm², cm⁴, GPa, mm. W pliku wszystko jest w SI (m, N, Nm, Pa).
- Znaki sił jak w RFEM: N ujemne = ściskanie. Składowe: N, Vy, Vz, Mt, My, Mz w układzie lokalnym pręta; x = odległość od
  początku pręta (pierwszego węzła linii). Znak Mz nie był sprawdzany z programem — przy wątpliwościach porównaj z RFEM.

# Wyniki w pliku .rf5: kiedy są, kombinacje KO, przypadki PO, pręty bez sił

- Wyniki są tylko w pliku zapisanym po obliczeniu. Plik zapisany przed obliczeniem albo „bez wyników” ma tylko model —
  narzędzia mówią wtedy „Wyniki: brak”.
- Każde obliczenie solvera to jeden zestaw: najczęściej kombinacja obciążeń (KO), rzadziej pojedynczy przypadek (PO).
  Program czyta z protokołu solvera, która to kombinacja (numer KO) i z jakich przypadków ze współczynnikami się składa,
  więc etykieta „KO5 (1.35*PO1 + 1.5*PO11)” jest pewna.
- W biurze liczy się zwykle same kombinacje z generatora Eurokodu, dlatego przypadki (PO) często nie mają własnych wyników.
  Gdy w RFEM policzono także przypadki, ich wyniki są w pliku: `rfem_sily` z `obciazenie=PO3` daje przebieg, a w ekstremach
  pojawiają się etykiety PO („PO14 Wiatr X+”). Które przypadki i kombinacje mają wyniki, mówi `rfem_przypadki`.
- Pręty sztywne (BeamRigid) i pręty wyłączone z obliczeń nie mają sił w plikach solvera — narzędzie mówi „pręt nie ma sił”.
- Kombinacje wyników (KW, w RFEM RC) to obwiednie („KO1 lub KO2”); ich wyniki nie są w plikach solvera, tylko w RFEM.
- Ekstrema „ze wszystkich zestawów” to największa i najmniejsza wartość każdej składowej po wszystkich kombinacjach
  i punktach pręta — z numerem KO i miejscem x. To odpowiada obwiedni „wszystkie KO” w RFEM.

# Czego program nie czyta z .rf5: powierzchnie, podpory, obciążenia, moduły, RFEM 6

- Nie czyta: powierzchni i ich wyników, podpór i reakcji, przegubów, zwolnień i mimośrodów, wartości obciążeń, imperfekcji,
  wyników modułów (RF-STEEL EC3, RF-STABILITY), kombinacji wyników (obwiedni liczonych w RFEM).
- Model powierzchniowy (płyty, ściany) ma węzły i linie, ale „0 prętów” — to normalne, nie błąd.
- Numery prętów w modelu mają luki (RFEM zachowuje numery po kasowaniu) — „nie ma pręta 4” jest poprawną odpowiedzią,
  `rfem_szukaj_pretow` bez kryteriów wypisze wszystkie.
- Gdy użytkownik widzi pręt w RFEM, a odczyt mówi, że go nie ma, odczyt pliku jest niepełny (zapis rekordu, którego program
  nie zna — tak było z modelami z Tekli przed poprawką z 2026-11) — powiedz to wprost i zaproponuj otwarty RFEM
  (`rfem_otwarty_pret`); nie twierdź, że pręta nie ma w modelu.
- Pliki RFEM 6 (.rf6) mają inną budowę — do nich służy Web Service / API RFEM 6, nie ta umiejętność.
- Nazwy w pliku są w stronie kodowej systemu (polskie modele: Windows-1250); modele z innych krajów mogą mieć zniekształcone znaki.

# Modele z Tekli (eksport Tekla Structures → RFEM 5): element Tekli, GUID, duże pliki

- Model wyeksportowany z Tekla Structures ma w każdym pręcie identyfikator elementu Tekli (GUID, np.
  f6a3088c-df30-4d0d-b2ff-b83ef199c498); `rfem_pret` pokazuje go jako „element Tekli …”, osobno od komentarza pręta.
  Po tym GUID element można odnaleźć w Tekli (Inquire / „Dowiedz się” pokazuje GUID obiektu). Węzły i linie identyfikatora nie mają.
- Takie modele bywają duże (setki MB): pierwsze polecenie na pliku trwa kilka–kilkanaście sekund (cały plik czytany do pamięci),
  kolejne korzystają z pamięci podręcznej, dopóki plik się nie zmieni.

# Otwarty RFEM 5 (RF-COM): polecenia rfem_otwarty*, rfem_wskaz, licencja, czego nie widać

- Gdy RFEM 5 jest uruchomiony na tym komputerze z otwartym modelem, program czyta ten model bezpośrednio przez RF-COM
  (dodatek COM do licencji RFEM 5), bez pliku: `rfem_otwarty` (co jest otwarte), `rfem_otwarty_pret`, `rfem_otwarty_przekroje`,
  `rfem_otwarty_przypadki`, `rfem_otwarty_sily` (siły i przemieszczenia z obliczeń, także dla kombinacji wyników KW),
  `rfem_otwarty_reakcje` (reakcje w węźle podporowym — tego z pliku .rf5 nie ma), `rfem_wskaz` (RFEM otwiera okno wyboru,
  użytkownik klika pręty, węzły albo linie i potwierdza OK).
- Zaznaczenia zrobionego wcześniej w oknie RFEM nie da się odczytać (biblioteka RF-COM 5.7 nie ma takiej metody); dlatego
  „zaznaczony pręt” obsługuje `rfem_wskaz` — trzeba kliknąć jeszcze raz w oknie wyboru.
- Każde polecenie zajmuje licencję RF-COM na czas odczytu (sekundy; ekstrema ze wszystkich kombinacji przy setkach KO — kilkanaście
  sekund) i zwalnia ją; przez ten czas okno RFEM jest zablokowane. Bez dodatku RF-COM w sesji (w biurze jest na licencji „stalowej”,
  nie na „żelbetowej”) wraca komunikat o braku licencji. Nic nie jest zapisywane do modelu.
- Wyniki w otwartym modelu są po obliczeniu w RFEM (nie trzeba zapisywać pliku). Jednostki i znaki jak w poleceniach na pliku:
  m, kN, kNm, cm², cm⁴, GPa, mm; N ujemne = ściskanie; przemieszczenia w osiach pręta; reakcje w osiach globalnych.
- Gdy RFEM nie jest uruchomiony, nie oddaje się przez COM (RFEM i Amadeusz na różnych poziomach uprawnień) albo nie ma otwartego
  modelu, polecenia mówią to wprost — wtedy zostają polecenia na zapisanym pliku .rf5.
