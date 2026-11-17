# Dokumenty Office w biurze — ściąga

## Które narzędzie
- „co jest w…”, „przeczytaj”, „streść”, „ile…”: `word_czytaj` / `excel_czytaj` / `powerpoint_czytaj` (najpierw czytasz, potem odpowiadasz).
- „otwórz”, „pokaż mi”: `office_otworz` (okno dokumentu na wierzchu; treści nie zwraca).
- „dopisz”, „dodaj akapit/nagłówek/punkt”, „napisz notatkę/pismo do Worda”: `word_dopisz` (nowy plik powstaje sam).
- „zamień X na Y”, „popraw numer rewizji w całym dokumencie”: `word_zamien`.
- „wpisz do komórki”, „dodaj formułę”, „policz sumę w B10”: `excel_zapisz`. Kilka komórek: `excel_wstaw_wiersze`.
- „zrób tabelę/zestawienie w Excelu”: `excel_wstaw_wiersze` z nagłówkiem w pierwszym wierszu (nowy plik powstaje sam).
- „zapisz jako PDF”, „wyeksportuj”: `office_zapisz_jako` z rozszerzeniem `.pdf`.
- „przygotuj maila”, „napisz do…”: `outlook_szkic` (użytkownik sam wyśle). „co mi przyszło”, „maile od…”: `outlook_odebrane`, potem `outlook_czytaj`.
- „dodaj slajd”: `powerpoint_dodaj_slajd` (tytuł + punkty; nowy plik powstaje sam).

## Pułapki
- Pełna nazwa pliku od użytkownika („koszty.xlsx”) = od razu polecenie na tym pliku; `szukaj_plikow` tylko po błędzie „nie ma pliku” albo przy opisie bez nazwy.
- Rozszerzenie mówi, który program: docx/doc/rtf → Word, xlsx/xlsm/xls/csv → Excel, pptx/ppt → PowerPoint. PDF ani TXT nie da się edytować tymi narzędziami.
- `word_czytaj` numeruje akapity; w tabelach komórki w wierszu są rozdzielone „ | ”. Dopisując „po akapicie N”, bierz N z tego wyniku.
- `excel_czytaj` domyślnie pokazuje aktywny arkusz. Gdy w wyniku jest lista arkuszy, a użytkownik mówi o innym, podaj `arkusz`.
- Adres komórki: litera kolumny + numer wiersza (B3). Wiersz 1 to zwykle nagłówki; dane zaczynają się od wiersza 2.
- Formuła sumująca kolumnę B od wiersza 2 do 9: `=SUMA(B2:B9)`; wpisz ją w komórkę pod danymi (B10), nie w nagłówek.
- Daty w Excelu pokazują się jako RRRR-MM-DD; wpisuj je tak samo (2026-11-17).
- Pliki otwarte u użytkownika w Wordzie/Excelu są edytowane na miejscu i zapisywane; użytkownik widzi zmianę od razu.
- Po edycji dokument zostaje otwarty na wierzchu — nie wołaj dodatkowo `office_otworz`.

Teczki projektów mają nazwy `NNNN_SKRÓT_Nazwa` (np. `1095_WSP`), rewizje to foldery `R00`, `R01`. Masa stali jest w kilogramach albo tonach; każdy gatunek (S235, S355) liczy się osobno.
