# Polecenia mostu i ograniczenia

## Jak to działa
Każde polecenie z katalogu to szablon skryptu C# 5, który most TeklaBridge wykonuje na otwartym modelu Tekli. Wynik wraca jako tekst zaczynający się od OK albo BLAD; przy wstawianiu zawiera `id=<ID>`. Jeden skrypt wykonuje się naraz. Most nie działa bez uruchomionej Tekli z otwartym modelem.

## Co jest w katalogu (23 polecenia)
- Wstawianie: wstaw_slup, wstaw_belke, wstaw_strop, wstaw_blache, wstaw_podstawe_slupa, wstaw_glowice_slupa.
- Zmiany jednego elementu po id: zmien_profil, zmien_material, zmien_klase, zmien_nazwe, przesun_element, kopiuj_element, ustaw_poziom_dolu_slupa, ustaw_poziom_gory_slupa, obroc_element, usun_element.
- Selekcja: wyselekcjonuj (zastępuje), dodaj_do_selekcji, wyczysc_selekcje, zaznacz_element.
- Odczyt: odczytaj_wlasciwosci, policz_elementy, pokaz_selekcje.

## Czego NIE ma
- Rysunków, zestawień, numeracji, połączeń typu komponent (węzły belka-słup, styk), śrub, spoin jako osobnego polecenia, otworów, wycięć, siatki osi, faz, atrybutów użytkownika.
- Masowych zmian (np. „zmień profil wszystkich HEB300”). Jedyna droga: wyselekcjonuj, odczytaj id (polecenie wypisuje do 40 id), potem zmieniaj pojedynczo.
- Cofania. Zmiany są zatwierdzane w modelu; cofnięcie robi użytkownik w Tekli (Ctrl+Z).
- Wstawiania kształtów innych niż prostokąt (strop i blacha są prostokątne, boki równoległe do X i Y).
- Słupów ukośnych w poleceniach podstawy/głowicy i zmiany poziomu (mają wymóg pionowości).

## Zachowanie wyników
- BLAD: komunikat mówi, co poprawić (np. brak elementu o id, element nie jest pionowym słupem).
- Wstawienie z profilem spoza katalogu: element może powstać mimo to; wynik dopisuje UWAGE.
- Selekcja: po wyselekcjonuj zaznaczone elementy są widoczne w Tekli i można je zliczyć poleceniem policz_elementy z zakresem selekcja.
- Typ elementu jest wyliczany: pręt prawie pionowy (kąt do pionu < ok. 45 stopni) to slup, inny pręt to belka, płyta z betonu (materiał C…/B…) to plyta, pozostałe płyty to blacha. Polibelki i inne obiekty nie są obsługiwane.
- Duże modele: policz_elementy / wyselekcjonuj iterują po wszystkich belkach i płytach; przy ~10 000 elementów może to trwać kilka-kilkanaście sekund. Filtr poziomu płyt czyta bryłę (wolniejszy).

## Bezpieczeństwo
Polecenia zmieniające model (zmienia_model = true) wykonuj tylko po wyraźnym poleceniu użytkownika. Odczyty i selekcje są bezpieczne.
