# Profile i ich zapis w Tekli 2019.1

Tekla przyjmuje nazwę profilu jako tekst. Zapis ma znaczenie: bez spacji, wielkie litery, separator `*` między wymiarami.

## Walcowane
- Dwuteowniki równoległe: HEA100 … HEA1000, HEB100 … HEB1000, HEM. Przykłady: HEA200, HEB300.
- Dwuteowniki IPE: IPE80 … IPE600 (IPE300, IPE240). IPN: IPN200.
- Ceowniki: UPE200 (równoległe), UPN200 (zbieżne). W katalogu środowiska użytkownika nie ma nazw typu C200. Orientacja ceownika (półki do wewnątrz czy na zewnątrz) zależy od obrotu profilu.
- Kątowniki: L100*10 (równoramienny, ramię*grubość), L100*50*8 (nierównoramienny). Przykład sprawdzony w środowisku: L75*8.
- Teowniki: zależy od katalogu środowiska; nie zgaduj.

## Rury i pręty
- Zależnie od środowiska: RHS200*100*8, SHS200*8, CHS219.1*8 (nazewnictwo Tekli międzynarodowe).
- W środowisku użytkownika (sprawdzone w pracy z mostem): nazwy SHS nie istnieją (0 pozycji). Działa zapis parametryczny: kwadratowa/prostokątna P200*200*6 (b*h*t), okrągła PD457*6 (średnica*grubość). Jeśli profil nie zostanie znaleziony, polecenie dopisuje w wyniku UWAGĘ - parametryczne profile mogą jednak dać fałszywy alarm.
- Pręt okrągły pełny: D24.

## Blachy i płaskowniki
- Blacha jako płyta (ContourPlate): profil PL<grubość>, np. PL20; wymiary z konturu.
- Płaskownik jako belka: PL20*300 (grubość*szerokość), długość z osi belki.

## Wskazówki dla modelu
- „HEB 300”, „heb300”, „Heb-300” -> HEB300.
- „IPE 240” -> IPE240.
- Gdy użytkownik napisze samo „dwuteownik” albo „rura” bez rozmiaru, zapytaj o profil.
- Wysokość profilu to liczba po nazwie (HEB300 ma wysokość 300 mm), ale dla HEA/HEB/HEM ta sama liczba oznacza inny ciężar i szerokość.
