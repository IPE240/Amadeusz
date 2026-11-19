# RFEM 5 z pliku — ściąga

## Które narzędzie
- nowy plik → `rfem_info` (co jest w modelu, czy są wyniki).
- „jaki przekrój / jaka długość ma pręt 28” → `rfem_pret` (numer).
- „jaka siła w pręcie 28” → `rfem_sily` (numer) = ekstrema N, Vy, Vz, Mt, My, Mz ze wszystkich kombinacji; „od KO5” albo „od PO1” → `rfem_sily` z `obciazenie=KO5` / `PO1` (przypadek ma wyniki, gdy był liczony — `rfem_przypadki` zaznacza [wyniki]).
- „ugięcie pręta” → `rfem_sily` z `co=przemieszczenia`.
- „które pręty mają HEA 160 / są kratownicowe / schodzą się w węźle 47 / są dłuższe niż 6 m” → `rfem_szukaj_pretow`.
- „jakie są przekroje / materiały” → `rfem_przekroje`; „jakie są kombinacje” → `rfem_przypadki`; „gdzie jest węzeł 47” → `rfem_wezel`.

## Jak czytać wynik
- Długości w m, współrzędne węzłów (X; Y; Z) w m, siły w kN, momenty w kNm, A w cm², Iy/Iz/It w cm⁴, E w GPa, przemieszczenia w mm.
- N < 0 ściskanie, N > 0 rozciąganie. Vz i My — zginanie w płaszczyźnie z pręta (zwykle pionowej), Vy i Mz — w płaszczyźnie y. Mt — skręcanie. Znaki jak w RFEM.
- `x` to odległość od początku pręta (pierwszy węzeł linii). Punkt powtórzony z innymi wartościami = skok od siły skupionej.
- PO = przypadek obciążeń (w RFEM LC), KO = kombinacja obciążeń (CO), KW = kombinacja wyników (RC). Wyrażenie `1.35*PO1 + 1.35*PO3 + PO21` = współczynniki razy przypadki. „lub” w KW = obwiednia.
- Typy prętów: belka (Beam), pręt sztywny (BeamRigid — bez przekroju i bez sił w wynikach), kratownicowy (Truss, TrussN — tylko N), cięgno (Tension), tylko ściskanie (Compression), lina (Cable).
- Przekrój zbieżny ma „przekrój na końcu”. Nazwa z normą („HE A 160 | DIN 1025-3:1994”) to nazwa z tabeli RFEM; w nawiasie nazwa biblioteczna („HE-A 160”).
- „komentarz „…”” to komentarz pręta z RFEM; „element Tekli f6a3088c-…” to GUID elementu w modelu Tekla Structures (modele wyeksportowane z Tekli) — po nim element można odnaleźć w Tekli.

## Czego w odczycie nie ma
- powierzchni, podpór, przegubów, zwolnień, mimośrodów, wartości obciążeń, wyników z modułów (RF-STEEL, RF-STABILITY), reakcji podporowych — tylko RFEM.
- wyników w pliku zapisanym przed obliczeniem albo bez wyników („Wyniki: brak”).
- plików RFEM 6 (.rf6) — inna budowa, nie czytamy.
- Numery prętów mają luki (np. 28, 29, 40…): „nie ma pręta 4” bywa poprawne (`rfem_szukaj_pretow` pokaże, które są), ale gdy użytkownik widzi pręt w RFEM — odczyt bywa niepełny: `rfem_otwarty_pret`.

Teczki projektów mają nazwy `NNNN_SKRÓT_Nazwa` (np. `1095_WSP`); modele .rf5 leżą zwykle w podfolderze `RFEM` albo `Obliczenia`.

## Otwarty RFEM 5 (polecenia rfem_otwarty* i rfem_wskaz)
- Działa, gdy RFEM 5 jest uruchomiony na tym komputerze z otwartym modelem i sesja ma licencję RF-COM (dodatek; w biurze na licencji „stalowej”). Nic nie zapisuje do modelu.
- „co jest otwarte w RFEM / ile prętów ma ten model” → `rfem_otwarty`; „jaki przekrój ma pręt 28 w otwartym modelu” → `rfem_otwarty_pret`; „jakie przekroje / kombinacje” → `rfem_otwarty_przekroje`, `rfem_otwarty_przypadki`.
- „zaznaczony / ten / wskazany pręt” (bez numeru) → `rfem_wskaz`: RFEM otwiera okno wyboru, użytkownik klika pręty (węzły, linie) i daje OK; jeden pręt = pełny opis z przekrojem, kilka = lista. Wcześniejszego zaznaczenia program nie widzi.
- „siła w pręcie 28” z otwartego modelu → `rfem_otwarty_sily` (bez `obciazenie` = ekstrema ze wszystkich kombinacji z wynikami, trwa kilkanaście sekund przy setkach KO; z `obciazenie=KO5`/`PO1`/`KW1` = przebieg; KW daje też, od której KO pochodzi wartość); „ugięcie” → `co=przemieszczenia` (osie pręta).
- „reakcje w węźle 1” → `rfem_otwarty_reakcje` (osie globalne PX, PY, PZ, MX, MY, MZ; bez `obciazenie` ekstrema). Tego z pliku .rf5 nie ma.
- Błędy: „nie jest uruchomiony” (otworzyć model), „nie ma licencji RF-COM” (inna licencja w sesji), „nie oddaje się przez COM” (RFEM i Amadeusz na różnych poziomach uprawnień), „nie ma wyników” (policzyć). Wtedy zostają polecenia na pliku .rf5.

## RFEM 6 i RF-COM w CS-check (wiedza z repozytorium CS, nie polecenia na pliku)
- RFEM 6 → API II (gRPC, port 9000, pakiet `dlubal.api` w wersji dopasowanej do buildu RFEM, klucz `ak-…` z Extranetu Dlubala, plan Free 1000 zapytań/mies., pełne pobranie modelu ≈ 19). `python tools/rfem_setup.py` konfiguruje; `python tools/rfem_pull.py --zywy --core --static` pobiera żywy model do CS (8 rodzajów obiektów, niemutowalne snapshoty; bez podpór i obciążeń); `python tools/rfem_obwiednia.py --sytuacja DS20 --przegub 133` daje obwiednię N, Vy, Vz, Mt, My, Mz z wartościami odpowiadającymi i kontrolą wobec obwiedni RFEM (`ZGODNE` / `ROZBIEZNOSC`); wyniki z pliku `.rf6` bez API: `rf6_plik.py`.
- RFEM 5 → RF-COM (COM lokalnie, dodatek w licencji, bez limitu, `comtypes`, nie pywin32; sprawdzianem licencji jest `LockLicense()`, nie `IsComLicensed()`; blokadę trzymać krótko w `try/finally`; COM jest na licencji „stalowej”, nie „żelbetowej”). `python tools/rfem5_setup.py`; pobrania RFEM 5 do bazy CS nie ma.
- Zapis do żywego RFEM: trzy bramki — plan → `--apply` (tylko pamięć sesji) → `--save` (świadoma decyzja).
- Gdzie szukać: docs/how-to/RFEM_API.md, RFEM_WYNIKI.md, RFEM5_COM.md, tools/rfem_client/README.md, src/features/rfem_mirror/README.md; siły do IDEA → umiejętność `cs_polaczenia`; model MES i wyniki 3D → `cs_statyka`.
