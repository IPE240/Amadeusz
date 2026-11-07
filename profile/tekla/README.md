# Profil A: Tekla Structures 2019.1 dla lokalnego modelu (Bielik)

Profil zamienia polskie polecenia na wywołania z katalogu; każde polecenie to szablon skryptu C# 5 dla mostu TeklaBridge.

## Zawartość
| Plik | Co to |
|---|---|
| `narzedzia.json` | katalog 23 poleceń (nazwa, opis, `zmienia_model`, schemat parametrów, ścieżka szablonu) |
| `szablony/*.cs` | 23 szablony skryptów (`Script.Run(Model)`), znaczniki `{{parametr}}` |
| `system.md` | polecenie systemowe (ok. 380 słów) |
| `karta.md` | ściąga wiedzy do promptu (ok. 730 słów) |
| `przyklady.jsonl` | 15 przykładów few-shot (w tym 3 z pytaniem doprecyzowującym) |
| `testy.jsonl` | 30 sesji testowych (A01-A30), 1-3 kroki |
| `wiedza/*.md` | 5 notatek dla `szukaj_wiedzy` |

## Konwencje
- Jednostka jest w nazwie parametru: `_mm` (milimetry: X, Y, wymiary), `_m` (metry: poziomy Z, tolerancja poziomu), `_stopni`. Szablon mnoży `_m` przez 1000.
- `id` to liczba całkowita (typ `integer`); wynik wstawiania: `OK id=<ID>`; błąd: `BLAD: ...`.
- Podstawianie (konwencja harnessu): liczba podana -> literał z kropką; liczba opcjonalna NIEPODANA (także integer) -> `double.NaN`, a szablon zamienia ją na wartość domyślną funkcją `Num()` (NaN -> 0 = domyślne); tekst -> zawartość literału w cudzysłowie, niepodany -> `""`. Brak parametrów boolean (tak/nie to enum tekstowy). Na górze każdego szablonu jest lista parametrów z wartościami domyślnymi.
- Filtry selekcji: `poziom_m` niepodany (NaN) -> brak filtra poziomu; podany (także 0) -> filtr, a `poziom_dotyczy` puste = `gora`.
- Wszystkie szablony pracują w globalnym układzie współrzędnych (przełączają i przywracają plane).

## Co sprawdzono
- Kompilacja: wszystkie 23 szablony skompilowano `csc.exe /t:library /langversion:5` (.NET Framework 4, referencje do `Tekla.Structures.dll`, `.Model.dll`, `.Datatype.dll` z Tekli 2019.1), w dwóch wariantach: wszystkie parametry wypełnione oraz tylko wymagane (opcjonalne: `""`/`double.NaN`). Wynik: 46/46 OK, 0 błędów. Pliki wynikowe leżą w `%TEMP%\tekla_profil_kompilacja`.
- Spójność danych: przykłady i testy sprawdzone skryptem względem `narzedzia.json` (istnienie polecenia i parametrów, typy, wartości `enum`, parametry wymagane w przykładach).
- Nic nie uruchamiano: ani TeklaBridge, ani Tekli, ani skompilowanego kodu. Folder `exchange` mostu nie był dotykany.

## Czego nie da się sprawdzić bez Tekli (do testu na kopii modelu)
- Strona grubości blach/płyt (`Position.Depth` FRONT/BEHIND): szablon sam sprawdza bryłę po wstawieniu i w razie potrzeby odwraca, ale ta ścieżka nie była wykonywana.
- Profil okrągły kotew `D<średnica>` i profil `PL<grubość>` dla płyt: mogą nie istnieć w katalogu środowiska (szablony dopisują UWAGĘ tylko dla poleceń z jawnym profilem).
- `Operation.MoveObject` / `CopyObject` (kompilują się, działanie na żywo niesprawdzone), `Weld` (spoina słup-blacha, w `try/catch`), `Identifier(int)` i `SelectModelObject`.
- Pionowość słupa: tolerancja 1 mm w X/Y; słupy lekko pochylone zostaną odrzucone w poleceniach podstawy/głowicy/poziomów.
- Wydajność na dużych modelach (9000+ belek): enumeracja wszystkich belek i płyt; filtr poziomu płyt czyta bryłę.
- Typ „plyta” rozpoznawany po materiale zaczynającym się od `C<cyfra>` lub `B<cyfra>`; inne oznaczenia betonu będą uznane za blachę.
- Orientacja profilu (`Rotation.FRONT`) nie gwarantuje pionowego środnika; polecenie `obroc_element` ustawia tylko `RotationOffset`.

## Źródła
- Przeczytano: `PROTOKOL.md`, `PAMIEC.md`, `CLAUDE.md`, `Program.cs`, wzorce z `exchange\done\*.cs` w `C:\Users\ODS33\Documents\DG_TeklaBridge\` (tylko odczyt).
- Repo wiedzy Tekla-Memory (`github.com/DominikGawel19/Tekla-Memory`): niedostępne (strona i raw zwracają 404, repo prywatne); lokalnego klona obok mostu też nie ma. Praca bez niego.
- Generator profilu (skrypty Pythona) leży w folderze roboczym sesji, nie w profilu.
