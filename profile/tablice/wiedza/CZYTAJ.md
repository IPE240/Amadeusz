# Wiedza umiejętności „tablice”

Ten plik jest dla ludzi: model go nie widzi (wyszukiwarka pomija `CZYTAJ.md`).

Pliki `*.md` są generowane z danych w `dane\*.json` skryptem z sesji budowania (patrz `README.md` umiejętności):

- `zyburtowicz_*.md` — charakterystyki przekrojów z Tablic Boguckiego i Żyburtowicza (wyd. 7, Arkady 2007), rozdział 1
  (dwuteowniki, połówki, ceowniki, kątowniki, teowniki, rury); jedna sekcja `#` na przekrój, dane w `dane\zyburtowicz_t*.json`;
- `zyburtowicz_zestawienia.md` — te same przekroje rodzinami po kilkanaście w sekcji (osobno wymiary i masa, osobno I, W, i),
  każda sekcja poniżej 900 znaków (tyle zwraca `szukaj_wiedzy`), do porównań, rankingów i wykresów; generowane z tych samych JSON-ów;
- `dast_*.md` — typowe połączenia doczołowe DAST (Prüfbescheid II B 2-543-778, 2002): nośności i wymiary po jednym połączeniu
  na sekcję, dane w `dane\dast.json`;
- `szs_c5_*.md` — tablice SZS C5 do połączeń (śruby, rozstawy, nośności, sprężanie, spoiny) i notatki z części opisowej C5
  (materiały, oznaczenia, zasady wymiarowania wg SIA 263), dane w `dane\szs_c5_polaczenia.json`;
- `zyburtowicz_uwagi.md` — jak korzystać z tablic: układ osi, jednostki, aktualność norm.

Dopisując własne notatki, trzymaj się tej samej zasady: nagłówek `#` z nazwą przekroju albo połączenia tak, jak pyta inżynier
(„HEB 300”, „IH1.1 IPE 240 M16”), potem krótkie zdania z liczbami i jednostkami. Sekcja powinna mieć mniej niż 1500 znaków.
Dane z OCR skanu oznaczone „wartości niepewne” trzeba sprawdzić w książce i poprawić w JSON, a potem wygenerować `.md` ponownie.
