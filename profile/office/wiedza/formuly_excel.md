# Formuły Excela po polsku (nazwy funkcji, średniki)

Polski Excel używa polskich nazw funkcji i średnika między argumentami. Narzędzie `excel_zapisz` najpierw próbuje
polskiej nazwy, a potem angielskiej, ale pewniej jest pisać po polsku. Przecinek jest separatorem dziesiętnym (12,5).

| Po polsku | Po angielsku | Co robi |
|---|---|---|
| `=SUMA(B2:B9)` | `=SUM(B2:B9)` | suma zakresu |
| `=ŚREDNIA(C2:C20)` | `=AVERAGE(C2:C20)` | średnia |
| `=MIN(C2:C20)`, `=MAX(C2:C20)` | tak samo | najmniejsza, największa |
| `=ILE.NIEPUSTYCH(A2:A50)` | `=COUNTA(A2:A50)` | ile wypełnionych komórek |
| `=LICZ.JEŻELI(B2:B50;"S355")` | `=COUNTIF(B2:B50,"S355")` | ile komórek spełnia warunek |
| `=SUMA.JEŻELI(B2:B50;"S355";C2:C50)` | `=SUMIF(B2:B50,"S355",C2:C50)` | suma C tam, gdzie B = S355 |
| `=JEŻELI(A2>0;"tak";"nie")` | `=IF(A2>0,"yes","no")` | warunek |
| `=ZAOKR(C2;2)` | `=ROUND(C2,2)` | zaokrąglenie do 2 miejsc |
| `=WYSZUKAJ.PIONOWO(A2;Cennik!A:C;3;FAŁSZ)` | `=VLOOKUP(A2,Cennik!A:C,3,FALSE)` | szukanie w tabeli |
| `=DZIŚ()` | `=TODAY()` | dzisiejsza data |
| `=A2*B2` | tak samo | iloczyn komórek |

# Suma kolumny, suma stali po gatunkach, masa w tonach

- Suma kolumny B od wiersza 2 do ostatniego z danymi (np. 9): `=SUMA(B2:B9)` wpisana w B10 (komórka pod danymi).
- Masa stali osobno dla każdego gatunku: `=SUMA.JEŻELI(B2:B50;"S355";C2:C50)` (B = gatunek, C = masa).
- Kilogramy na tony: `=C2/1000`; tony na kilogramy: `=C2*1000`.
- Procent: `=C2/C$10` z formatem procentowym; `$` blokuje wiersz przy kopiowaniu.

# Adresy komórek, zakresy, arkusze

- Adres = litera kolumny + numer wiersza: `B3`. Zakres: `A1:D20`. Cała kolumna: `C:C`.
- Odwołanie do innego arkusza: `Stal!C5`; nazwa ze spacją w apostrofach: `'Zestawienie stali'!C5`.
- Wiersz 1 to zwykle nagłówki; dane zaczynają się od wiersza 2. Sumę wpisuje się w pierwszym pustym wierszu pod danymi.
- Błędy: `#NAZWA?` = zła nazwa funkcji (np. angielska w polskim Excelu), `#DZIEL/0!` = dzielenie przez zero,
  `#ADR!` = usunięty zakres, `#ARG!` = zły typ argumentu (tekst zamiast liczby).

# Daty i liczby w arkuszu

- Daty pokazują się jako `RRRR-MM-DD` (2026-11-17); wpisuj je w tym formacie, Excel rozpozna datę.
- Liczba z przecinkiem (`12,5`) jest liczbą; liczba z kropką w polskim Excelu bywa tekstem — narzędzie zamienia kropkę na przecinek.
- Tekst, który wygląda jak liczba (np. numer rysunku `0123`), wpisz z apostrofem na początku: `'0123`.
