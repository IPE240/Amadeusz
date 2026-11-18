# Umiejętność `rfem`: modele RFEM 5 (.rf5) bez programu Dlubal

Program czyta plik .rf5 z dysku (ZIP albo starszy kontener OLE) i oddaje modelowi gotowe tabele: węzły, linie, pręty
z typem, długością i przekrojem, przekroje z charakterystykami i materiałem, przypadki (PO) i kombinacje (KO, KW),
a w pliku zapisanym po obliczeniu — siły wewnętrzne w prętach (N, Vy, Vz, Mt, My, Mz) i przemieszczenia dla każdej
kombinacji. RFEM nie musi być zainstalowany. Wszystko tylko do odczytu, bez pytań o zgodę; dozwolone foldery jak dla `pliki`.

## Polecenia

| Polecenie | Co robi |
|---|---|
| `rfem_info` | podsumowanie: wersja, kontener, liczby węzłów/linii/prętów (z typami), przekroje z liczbą prętów, materiały, przypadki, kombinacje, czy są wyniki |
| `rfem_pret` | pręt: typ, linia, węzły ze współrzędnymi, długość, obrót, przekrój (A, Iy, Iz, It, wymiary) i materiał; przy wynikach ekstrema sił ze wszystkich kombinacji |
| `rfem_wezel` | współrzędne węzła, linie przez węzeł, pręty w węźle |
| `rfem_przekroje` | tabela przekrojów (A [cm²], Iy, Iz, It [cm⁴], pręty, łączna długość) i materiały (E, G, γ, norma) |
| `rfem_przypadki` | PO z opisami, KO z wyrażeniami (`1.35*PO1 + 1.5*PO11`), KW (obwiednie), które mają wyniki |
| `rfem_sily` | siły pręta [kN, kNm]: bez `obciazenie` ekstrema ze wszystkich kombinacji (z numerem KO i x), z `obciazenie=KO5` przebieg wzdłuż pręta; `co=przemieszczenia` → ugięcia [mm] |
| `rfem_szukaj_pretow` | pręty po przekroju (numer albo fragment nazwy), typie, węźle, zakresie długości |
| `szukaj_plikow`, `pokaz_zawartosc` | jak w `komputer` (np. `*.rf5`) |

Bez `sciezka` polecenia biorą jedyny plik .rf5 z folderu bieżącego (folderu rozmowy).

## Pliki
| Plik | Co to |
|---|---|
| `profil.json` | `zakonczenie: model` (wszystko czyta, model odpowiada), `szukaj_wiedzy` i `kalkulator`, bez `chmura`, bez internetu |
| `system.md` | rola i 10 zasad: najpierw narzędzie, pełna nazwa pliku = od razu polecenie, siła bez kombinacji = ekstrema, jednostki, PO/KO/KW, czego nie ma w odczycie |
| `karta.md` | ściąga: które narzędzie, jak czytać wynik (jednostki, znaki, x), czego nie ma |
| `narzedzia.json` | 9 poleceń |
| `przyklady.jsonl` | 20 przykładów, w tym bez wołania (reakcje podporowe, pytanie bez numeru pręta) |
| `testy.jsonl` | 10 testów F01–F10 (`LokalneAI.Eval test`) |
| `wiedza/rfem5.md` | co program czyta z .rf5, jednostki i znaki, kiedy są wyniki, czego nie ma |

## Jak działa (`LokalneAI.Harness.Rfem`, bez paczek)
- `Rf5Archive`: kontener ZIP (`System.IO.Compression`) albo OLE (`CompoundFile`, własny czytnik CFB: sektory 512/4096 B,
  DIFAT, mini-strumienie). Cały plik czytany do pamięci — bez uchwytu, żeby RFEM mógł w tym czasie zapisywać.
- `Rf5Parser`: strumień modelu (`RFEM Input Data.bin` w ZIP, `RFEM_Data 2.002` w OLE). Każdy obiekt to rekord ze znacznikiem
  `OwnerParamGroup`; rozpoznawane układy: węzeł (3×f8 + układ współrzędnych), linia (lista `Numbers` węzłów), pręt
  (f4 obrót, i32 linia, typ `Beam`/`BeamRigid`/`TrussN`…, lista przekrojów). Tabele przekrojów, materiałów, przypadków,
  kombinacji obciążeń i kombinacji wyników mają własne stałe układy (opis w komentarzach kodu). Nazwy są w stronie kodowej 1250.
  Rekordy o nieznanym układzie (powierzchnie, podpory, przeguby, obciążenia) są liczone i pomijane (`Warnings`).
- `Rf5Mesh`: `Mesh/.MTO` (kolejność prętów w plikach wyników), `Mesh/.geo` (węzły, linie, MACR — zapas na długości).
- `Rf5Results`: `Results_Main_V_2/.HXN<nr>` (obliczenia nieliniowe) i `.HXL<nr>` (liniowe) — nagłówek int64 z offsetami
  bloków: pręty (liczba punktów, pierwszy punkt), punkty (f8 x), siły 6×f4 [N, Nm], przemieszczenia 6×f4. Który to przypadek
  albo kombinacja, mówi protokół solvera `.PRON<nr>`/`.PROL<nr>` (`NCOMB=`, `NCASE=`, `NCOEF=`) — etykieta „KO5 (1.35*PO1 + …)”
  jest z pliku, nie z założenia o kolejności.
- `RfemExecutor`: polecenia, pamięć podręczna wczytanych plików (odświeżana po zmianie daty albo rozmiaru), jednostki
  dla ludzi (m, kN, kNm, cm², cm⁴, mm, przecinek dziesiętny).

## Sprawdzone
- Testy jednostkowe (`RfemUnitTests`, 13) na danych syntetycznych w rozpoznanym układzie: rekordy modelu, przekroje, materiały,
  PO/KO/KW, siatka, protokół i plik sił, czytnik OLE, decyzje i odpowiedzi wykonawcy, pliki umiejętności.
- Testy na modelach biura (`RfemLiveTests`): ścieżki plików .rf5 w zmiennej `LOKALNEAI_RFEM_PROBKA` (rozdzielone `;`);
  bez zmiennej test przechodzi bez sprawdzania. Pliki klientów nie leżą w repozytorium.
- Rozpoznanie formatu: pięć modeli biura z RFEM 5.27 (ZIP i OLE, z wynikami i bez, prętowe i powierzchniowy). Znak N
  (ujemne = ściskanie) sprawdzony na ciężarze własnym słupa; kolejność składowych jak w RFEM 6 (MKK); znak Mz niesprawdzony.

## Ograniczenia
- Nie czyta powierzchni, podpór, przegubów, obciążeń, imperfekcji, wyników modułów ani kombinacji wyników (obwiedni).
- Nie czyta plików RFEM 6 (.rf6) ani RSTAB (.rs8 — ten sam kontener, inny strumień modelu).
- Układy rekordów są z inżynierii wstecznej: inna wersja RFEM 5 może zapisać inaczej — wtedy `Warnings` i liczby „0”.
  Dodając obsługę, porównaj plik z modelem w RFEM, nie zgaduj.
