# Umiejętność formatowanie

Zasada dla modelu: odpowiedź w Markdownie — dane w wierszach jako tabela (ranking posortowany z kolumną „Lp.”, porównanie
z kolumną „Różnica”), kroki jako lista, najważniejszy wynik pogrubiony, a dużo liczb jako wykres w bloku ```` ```wykres ````.
Okno Amadeusza (Czat i Code, także panel `HarnessPanel` w innych programach) pokazuje Markdown jako tabelę z ramkami
i rysuje wykres; menu pod prawym przyciskiem ma „Kopiuj tabele i dane wykresów do Excela”.

- Bez narzędzi i bez wiedzy — sam `system.md` (`Eval rozmiar`: ~1270 tokenów szacunkowo, Bielik 11B liczy go na ok. 2000;
  ponad połowa to opis pól wykresu). Przy kontekście 8192 zostaje to w budżecie, ale profile z dużą wiedzą (tekla, tablice)
  i tak potrzebują 16384.
- Domyślnie we wszystkich profilach (Ustawienia → Profile); program dopisał ją raz do istniejących profili.
- Przy łączeniu z innymi umiejętnościami (`HarnessProfile.Combine`) umiejętność bez żadnych narzędzi nie jest
  „rolą”: jej tekst trafia na koniec polecenia jako „Zasady odpowiedzi”, a zasady pozostałych umiejętności
  (chmura, internet, zakończenie tury) zostają bez zmian. Dlatego `chmura` dopuszcza wszystkich dostawców,
  a `internet` jest włączony — część wspólna z resztą niczego nie zawęża.
- Zakładka Czat (bez umiejętności) też dostaje `system.md` tej umiejętności (`MainViewModel.FormattingRules`).

## Blok ```wykres

JSON czytany przez `ChartSpec.Parse` (`LokalneAI.Harness.Charts`, bez WPF) i rysowany przez `ChartView` (`LokalneAI.Harness.Wpf`).
Pola po polsku (angielskie też przechodzą, polskie znaki w nazwach pól nie przeszkadzają):

| Pole | Co robi |
|---|---|
| `typ` | `slupkowy` (domyślny), `poziomy` (słupki poziome — rankingi, długie nazwy), `skumulowany`, `liniowy`, `obszarowy` (linia z wypełnieniem), `kolowy`, `pierscieniowy` (z sumą w środku); także flagi `poziomy`, `skumulowany`, `pierscien`: true |
| `tytul`, `os_x`, `os_y` | tytuł i opisy osi (jednostka w `os_y`) |
| `etykiety`, `serie` | kategorie i serie `[{"nazwa", "wartosci", "kolor", "typ": "liniowy", "wypelnienie", "przerywana"}]`; `serie` może być też mapą `{"IPE": [..]}`; zamiast tego `dane`: wiersze `["IPE 100", 8.1]` albo `{"nazwa": …, "masa": …}` (+ `kolumny`) |
| `grupy`, `kolory_grup` | grupa każdej pozycji (np. rodzina IPE/HEA): słupki jednej serii w kolorze grupy, legenda po grupach |
| `kolory` | lista kolorów pozycji (jedna seria, kołowy) albo serii (gdy tyle kolorów, ile serii; jeden kolor = cała seria); nazwy po polsku (`niebieski`, `pomarańczowy`, `zielony`, `żółty`, `różowy`, `fioletowy`, `czerwony`, `turkusowy`, `brązowy`, `szary`, `czarny`, `granatowy`, `jasnoniebieski`…) albo `#rrggbb` |
| `sortuj`, `sortuj_wg` | `malejaco`, `rosnaco`, `nazwa` (także `true` = malejąco) po pierwszej serii albo po `sortuj_wg` (nazwa serii); zrobione już przy czytaniu — Excel dostaje tę samą kolejność |
| `odwroc_x`, `odwroc_y` | odwrotna kolejność kategorii; oś wartości rosnąca w dół (pionowy) albo w lewo (poziomy) |
| `zakres_y` | `[od, do]` własny zakres osi wartości (słupki poza zakresem są ucinane); także `os_y_od`, `os_y_do` |
| `wartosci` | `true`/`false` — podpisy liczb przy słupkach (domyślnie: jedna seria, do 12 słupków) |
| `wypelnij_miedzy`, `kolor_wypelnienia` | wypełnienie między dwiema seriami liniowymi (nazwy albo numery od 1) |
| `max_czesci`, `wysokosc` | kołowy: ile kawałków (2–10, reszta „Pozostałe”); wysokość w px (160–800; poziomy sam rośnie z liczbą pozycji) |

Czego nie ma (model ma o tym powiedzieć i dać dwa wykresy albo tabelę): druga oś Y, wykres XY z liczbami na obu osiach,
3D, wykresy w kolumnach obok siebie. Testy: `ChartSpecTests` (czytanie, sortowanie, kolory, zgodność `system.md` z polami).
