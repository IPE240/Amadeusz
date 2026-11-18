# RFEM 5 z pliku — ściąga

## Które narzędzie
- nowy plik → `rfem_info` (co jest w modelu, czy są wyniki).
- „jaki przekrój / jaka długość ma pręt 28” → `rfem_pret` (numer).
- „jaka siła w pręcie 28” → `rfem_sily` (numer) = ekstrema N, Vy, Vz, Mt, My, Mz ze wszystkich kombinacji; „od KO5” → `rfem_sily` z `obciazenie=KO5`.
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

## Czego w odczycie nie ma
- powierzchni, podpór, przegubów, zwolnień, mimośrodów, wartości obciążeń, wyników z modułów (RF-STEEL, RF-STABILITY), reakcji podporowych — tylko RFEM.
- wyników w pliku zapisanym przed obliczeniem albo bez wyników („Wyniki: brak”).
- plików RFEM 6 (.rf6) — inna budowa, nie czytamy.
- Numery prętów mają luki (np. 28, 29, 40…): „nie ma pręta 4” to normalna odpowiedź, `rfem_szukaj_pretow` pokaże, które są.

Teczki projektów mają nazwy `NNNN_SKRÓT_Nazwa` (np. `1095_WSP`); modele .rf5 leżą zwykle w podfolderze `RFEM` albo `Obliczenia`.
