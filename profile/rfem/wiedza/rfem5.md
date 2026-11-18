# Plik RFEM 5 (.rf5): co program czyta bez RFEM-a, jednostki, znaki sił

- Plik .rf5 to archiwum (ZIP albo starszy kontener OLE) ze strumieniem modelu, siatką solvera (`Mesh`) i wynikami
  (`Results_Main_V_2`). Program czyta go z dysku — RFEM nie musi być zainstalowany ani uruchomiony.
- Z modelu program czyta: węzły (X, Y, Z w m), linie (węzły), pręty (typ, linia, obrót, przekrój na początku i końcu),
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
- Pliki RFEM 6 (.rf6) mają inną budowę — do nich służy Web Service / API RFEM 6, nie ta umiejętność.
- Nazwy w pliku są w stronie kodowej systemu (polskie modele: Windows-1250); modele z innych krajów mogą mieć zniekształcone znaki.
