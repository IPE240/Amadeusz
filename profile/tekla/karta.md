# Karta wiedzy: Tekla Structures 2019.1, konstrukcje stalowe i żelbetowe

## Układ współrzędnych i poziomy
- Tekla pracuje w milimetrach; oś Z jest skierowana w górę. Poziom (rzędna) = wartość Z.
- Użytkownicy mówią poziomy w metrach: „3,53” = +3,530 m = 3530 mm. W poleceniach katalogu poziomy (`_m`) podajesz w metrach, a współrzędne X/Y (`_mm`) w milimetrach.
- Punkt „6000, 0” to X=6000 mm, Y=0. Poziom 0 to zwykle górna powierzchnia fundamentu / posadzki; ujemny poziom (np. -0,5) leży poniżej.

## Pojęcia
- **Słup**: pionowy pręt. Dół = podstawa (stopa), góra = głowica. „Poziom dołu/góry słupa” to rzędna osi na tym końcu.
- **Belka**, **rygiel**: poziomy pręt między słupami. **Płatew**: belka dachowa. **Stężenie**: pręt ukośny (wstawiane poleceniem belka z różnymi poziomami końców).
- **Strop**, **płyta**: płyta betonowa (w katalogu typ `plyta`). **Blacha**: płaska stalowa płyta PL (typ `blacha`).
- **Podstawa słupa**: blacha podstawy pod słupem, w niej kotwy (pręty zakotwione w fundamencie), czasem ostroga. Pod blachą jest podlewka (warstwa zaprawy, zwykle 30-50 mm; w katalogu jej nie ma).
  - Kotwy: fundamentowe (wklejane lub zabetonowane), chemiczne (na żywicę), średnice M16, M20, M24, M30, M36; długość zakotwienia zwykle 400-1000 mm. Typowo 4 kotwy w narożnikach.
  - Ostroga: kawałek profilu (np. HEA100, IPE120, kątownik) przyspawany pod blachą podstawy i wchodzący w fundament; przenosi siły poziome (ścinanie).
- **Głowica słupa**: blacha czołowa na górze słupa (oparcie belki, płyty, połączenie ze słupem wyżej). Blacha głowicy leży NAD słupem.
- Blacha podstawy w katalogu leży POD słupem (górna powierzchnia blachy na poziomie dołu słupa), kotwy wystają ok. 60 mm nad blachę.

## Materiały
- Stal: S235JR, S275JR, S355J2 (najczęściej), S460. Beton: C20/25, C25/30, C30/37, C35/45, C40/50. Śruby: klasy 8.8, 10.9 (kotwy zwykle 5.6 lub 8.8).
- Domyślnie w katalogu: stal S235JR, beton C25/30 (stropy) - użytkownik może podać inny gatunek.

## Profile w bibliotece Tekli (zapis bez spacji)
- Dwuteowniki: HEA200, HEB300, HEM300, IPE300, IPN200. Ceowniki: UPE200, UPN200 (w środowisku użytkownika nie ma nazw „C…”).
- Kątowniki: L100*10 (ramiona*grubość), L100*50*8. Płaskowniki jako pręty: PL20*300 (grubość*szerokość).
- Rury: w środowisku użytkownika sprawdzono, że okrągłe to PD<średnica>*<grubość> (np. PD457*6), prostokątne/kwadratowe P<b>*<h>*<t> (np. P200*200*6); nazw SHS/RHS tam nie ma. W innych środowiskach bywa RHS200*100*8, SHS200*8, CHS219.1*8 - nie zgaduj, zapytaj lub sprawdź w wyniku (UWAGA o katalogu).
- Pręt okrągły pełny: D24 (tak są wstawiane kotwy).
- Blachy stalowe w modelu: profil PL<grubość> (np. PL20); wymiar w planie podajesz osobno.

## Klasy (class)
- Klasa to numer 1-14 przypisany elementowi, w Tekli odpowiada kolorowi wyświetlania. Nie ma znaczenia konstrukcyjnego, służy do porządkowania i filtrowania.

## Co robi katalog
- Wstawianie: wstaw_slup, wstaw_belke, wstaw_strop, wstaw_blache, wstaw_podstawe_slupa, wstaw_glowice_slupa. Każde zwraca `id=...`.
- Zmiany: zmien_profil, zmien_material, zmien_klase, zmien_nazwe, przesun_element, kopiuj_element, ustaw_poziom_dolu_slupa, ustaw_poziom_gory_slupa, obroc_element, usun_element.
- Selekcja i odczyt: wyselekcjonuj (zastępuje selekcję), dodaj_do_selekcji, wyczysc_selekcje, zaznacz_element, odczytaj_wlasciwosci, policz_elementy, pokaz_selekcje.
- Polecenia zmieniające pojedynczy element biorą `id`. Masowych zmian nie ma: przy takiej prośbie powiedz to i zaproponuj selekcję oraz zmiany pojedynczo po id.

## Filtry selekcji i liczenia
- `typ`: slup, belka, plyta, blacha, wszystko. Pominięty = wszystko.
- `profil`: dokładny (HEB300) albo z gwiazdką na końcu (HEB*) = wszystkie zaczynające się tak samo.
- `poziom_m` + `poziom_dotyczy` (gora, dol, srodek) + `tolerancja_m`: dla słupa góra/dół to końce osi; dla belki poziom osi; dla płyty górna/dolna powierzchnia bryły. Pominięte `poziom_dotyczy` = gora. „Strop na poziomie 3,53” = typ plyta, poziom_m 3.53 (górna powierzchnia).
- Tolerancja domyślnie 2 mm; gdy użytkownik mówi „z tolerancją 1 cm”, podaj 0.01.

## Typowe pułapki
- Poziomy w metrach, wymiary w mm: „strop grubości 20 cm” = grubosc_mm 200, „na poziomie 3,5” = poziom_gora_m 3.5.
- „Od zera” w kontekście słupa to poziom_dol_m 0.
- Zmiana poziomu słupa NIE przesuwa jego blachy podstawy, głowicy ani kotew; po zmianie poziomu trzeba je poprawić lub wstawić od nowa.
- Obrót profilu (`obrot_stopni`, `kat_stopni`) obraca profil wokół własnej osi; nie jest obrotem elementu w planie. Ustawienie FRONT nie gwarantuje, że środnik jest pionowo - jeśli użytkownik chce konkretnej orientacji, powiedz, że trzeba to sprawdzić w modelu.
- Nieznany profil Tekla często przyjmuje bez błędu, a element wychodzi bez przekroju; szukaj w wyniku napisu „UWAGA: profilu … nie ma w katalogu”.
- Parametry profili parametrycznych (P200*200*6, PL20*300, PD457*6) nie występują w katalogu jako gotowe pozycje, więc uwaga o katalogu może być fałszywym alarmem.
- id to liczba całkowita z wyniku („OK id=1001”). Nie zmyślaj id. Po usunięciu elementu jego id przestaje istnieć.
- Selekcja w Tekli jest globalna: wyselekcjonuj ją zastępuje, dodaj_do_selekcji dopisuje.
