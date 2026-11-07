# Podstawy słupów stalowych, kotwy, ostroga, podlewka

## Układ podstawy (od góry)
1. Słup stalowy (dwuteownik HEA/HEB lub rura).
2. Blacha podstawy (PL 20-50 mm, wymiar większy od słupa o 100-200 mm w każdą stronę), przyspawana do słupa spoiną pachwinową.
3. Podlewka bezskurczowa pod blachą (30-50 mm), zapewnia pełne oparcie blachy na fundamencie.
4. Fundament żelbetowy z kotwami.

## Kotwy
- Typowo 4 kotwy w narożnikach blachy (rozstaw 60-80 mm od krawędzi blachy do osi kotwy); przy dużych momentach 6-8.
- Średnice: M16, M20, M24, M30, M36 (dla ram i silnie obciążonych słupów M24-M36).
- Fundamentowe (zabetonowane): z hakiem, kotwą płytkową lub nakrętką; długość zakotwienia ok. 25-40 średnic, w praktyce 500-1000 mm.
- Chemiczne (wklejane na żywicę): pręt gwintowany wklejany w nawiercony otwór; zakotwienie ok. 8-15 średnic (kilkaset mm).
- Otwory w blasze podstawy o 2-6 mm większe niż średnica kotwy (luz montażowy), podkładka i dwie nakrętki (poziomowanie i dokręcenie).
- W katalogu poleceń: `wstaw_podstawe_slupa` wstawia kotwy jako pręty okrągłe (profil D<średnica>) w narożnikach prostokąta o rozstawie `kotwy_rozstaw_x_mm` x `kotwy_rozstaw_y_mm`, schodzące `kotwy_dlugosc_mm` pod blachę i wystające 60 mm nad blachę. Nie są to śruby z gwintem ani komponent kotew Tekli.

## Ostroga
- Kawałek profilu (HEA100, IPE120, kątownik, kawałek ceownika) przyspawany do spodu blachy podstawy i zagłębiony w fundamencie (lub w osobnym gnieździe).
- Zadanie: przenieść siłę poprzeczną (ścinanie) bez obciążania kotew na ścinanie.
- Typowo długość 150-300 mm, osadzona na osi słupa.
- W katalogu: `ostroga` = tak, `ostroga_profil`, `ostroga_dlugosc_mm`.

## Co robi polecenie wstaw_podstawe_slupa
- Wymaga pionowego słupa (zwraca BŁĄD, gdy słup jest ukośny).
- Blacha leży POD słupem: jej górna powierzchnia jest na poziomie dołu słupa, środek blachy na osi słupa, boki równoległe do osi X i Y modelu.
- Wstawia spoinę słup-blacha (jeśli się uda; w przeciwnym razie dopisek w wyniku).
- Zwraca id blachy, id kotew i id ostrogi. Model powinien zapamiętać id blachy.
- Czego nie robi: nie tworzy otworów pod kotwy w blasze, nie wstawia podlewki, nie obraca blachy z osią słupa.

## Uwaga o poziomach
Jeśli użytkownik chce, żeby góra blachy podstawy była na poziomie 0, a słup zaczynał się wyżej, najpierw trzeba ustawić dół słupa na grubość blachy (np. 0.03 m) poleceniem ustaw_poziom_dolu_slupa. Domyślnie blacha jest wstawiana pod słupem, czyli poniżej poziomu dołu słupa.
