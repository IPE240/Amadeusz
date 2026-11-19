# Okno Amadeusza: zakładki Czat, Code, Ustawienia

- **Czat** — zwykła rozmowa z modelem; pod odpowiedzią gdzie liczył (ile modelu na karcie), tokeny, szybkość, czas. Enter wysyła, Shift+Enter nowa linia, Esc albo Stop przerywa. „Pamiętaj rozmowę” domyślnie wyłączone.
- **Code** — asystent z umiejętnościami (tekla, cde, pliki, komputer, office, ocr, rfem, tablice, mkk, tlumaczenie, amadeusz…), który wykonuje polecenia albo odpowiada z wiedzy. Trzy poziomy uprawnień: Tylko czyta, Pyta o każdą zmianę, Bez pytania (poza kasowaniem). Z lewej panel rozmów.
- **Ustawienia** — sekcje zwijane kliknięciem w nagłówek: Gdzie liczyć, Modele w chmurze, Menedżer poświadczeń, Umiejętności, Profile, OCR, Udostępnianie, Kolejka, Czat.
Na górze: lista modeli (gwiazdka = ulubiony), Odśwież, przełącznik „Odblokuj dostęp do internetu”, „Zwolnij pamięć GPU”, wykres temperatury karty.

# Whiteboard: tablica, rysowanie, Capture, odpowiedź na tablicy

Zakładka **Whiteboard**: tablica z narzędziami jak w Lightshocie — Zaznacz (V), Pióro (P), Linia (L), Strzałka (A), Prostokąt (R), Marker (M), Tekst (T), Gumka (E), kolory, grubość, Cofnij (Ctrl+Z), Ponów (Ctrl+Y). Ctrl+V wkleja obrazek; rozmiar zmienia Zaznacz — proporcjonalnie. **Capture** chowa okno do przezroczystej ramki w miejscu tablicy; ramkę przesuwasz i zmieniasz, Zatwierdź robi zrzut jej wnętrza na tablicę, Zaniechaj wraca. Pod tablicą polecenie i **Zatwierdź** (Enter). Model dostaje opis tablicy (teksty dosłownie, rysunki ze współrzędnymi, OCR obrazów i odręcznych napisów), a model z obsługą obrazów także jej obraz. Odpowiedź jest z prawej jak w Code; rysunki Amadeusza leżą na osobnej warstwie (przycisk chowa i czyści). „z umiejętnościami z Code” dokłada umiejętności zaznaczone w Code.

# Brakujące umiejętności: szukanie i przycisk „Tak, włącz i ponów”

Gdy w zakładce Code do polecenia brakuje narzędzia, Amadeusz nie mówi „nie potrafię”: szuka wśród niezaznaczonych umiejętności (znajdz_umiejetnosc) i prosi o włączenie pasującej (wlacz_umiejetnosci). W rozmowie pojawia się pytanie z przyciskami **Tak, włącz i ponów** / **Nie**. Po zgodzie umiejętność dochodzi do zaznaczonych, a polecenie idzie jeszcze raz samo, z całą rozmową (najwyżej dwa ponowienia z rzędu). Pomijane są umiejętności ukryte, bez poświadczenia i niedozwolone dla wybranego modelu w chmurze. Tablica (Whiteboard) i podagenci tego nie mają.

# Wklejony obrazek: OCR i obraz dla modelu (Code, Czat)

Ctrl+V z obrazkiem w polu Code albo Czatu: OCR (Ustawienia → OCR) wstawia tekst do pola, a gdy wybrany model widzi obrazy (gemma4, qwen3.5, ministral, Claude, GPT, Gemini; Bielik — nie), obraz idzie też do modelu — miniatura nad polem, „×” go usuwa. Model bez obsługi obrazów dostaje sam tekst z OCR.

# Zasobnik (tray), zamykanie, jedna kopia programu

Zamknięcie okna (×) chowa Amadeusza do zasobnika — program, brama i kolejka działają dalej. Kliknięcie ikony wraca do okna; prawy przycisk → **Zakończ Amadeusza** naprawdę wyłącza. Ikona: głowa Mozarta, biała w spoczynku, niebieska przy pracy, łuk temperatury karty 30–70°C, w rogu liczba czekających zapytań. Drugie uruchomienie skrótu pokazuje działające okno zamiast drugiej kopii.

# Zwolnij pamięć GPU, temperatura karty, model w pamięci

**Zwolnij pamięć GPU** wyrzuca model z karty tego komputera od razu (np. przed pracą w Tekli). Bez tego Ollama zwalnia pamięć sama po 5 minutach bezczynności. Wykres na pasku tytułu pokazuje temperaturę karty komputera, który liczy.

# Pokaż tokeny i czasy, Pokaż kroki modelu (Diagnostyka)

**Ustawienia → Czat → Diagnostyka**: „Pokaż tokeny i czasy przy wpisach w zakładce Code” — przy każdym wpisie dokładne liczby z Ollamy (zasady i narzędzia, pytanie, ile napisał model, wyniki narzędzi wracające do modelu, Σ wejście | praca | odpowiedź) i czasy (pisanie pytania, ładowanie, myślenie, pisanie, narzędzie, kolejka i sieć). „Pokaż kroki modelu” — każdy krok pętli jako wpis „Krok N”. Jedno polecenie to najwyżej `max_krokow` kroków (domyślnie 12).

# Rozmowy: panel z lewej, grupy = foldery, kosz, kolejność

Zakładka Code: każde polecenie zapisuje rozmowę; kliknięcie w panelu ją wznawia. Grupa = folder roboczy („+ Nowa grupa (folder)…”), rozmowy leżą w jego podfolderze `.amadeusz\`; domyślna grupa „Niezgrupowane” to `Dokumenty\Amadeusz`. Prawy klik: zmiana nazwy, przeniesienie, Usuń (kosz `.amadeusz\kosz\` na 7 dni). Kolejność: najnowsze polecenie (Enter) na górze. Czat ma własny panel: `Dokumenty\Amadeusz\Czat\`.

# Internet i modele w chmurze (Claude, GPT, Gemini)

„Odblokuj dostęp do internetu” (przełącznik u góry, nadrzędny): czat i zakładka Code mogą szukać w DuckDuckGo i czytać strony; adresy sieci biura są dla modelu zablokowane. **Ustawienia → Modele w chmurze**: własny klucz API Anthropic, OpenAI albo Google (płatne za zużycie; konto claude.ai/ChatGPT nie wystarczy), modele trafiają na listę w swoim kolorze. Umiejętność pozwala na chmurę polem `chmura` w profil.json; cde — tylko Claude; bez pola — tylko modele lokalne.

# Dlaczego przełącznik internetu sam się wyłączył (umiejętność bez zgody na internet)

Internet to nie osobna umiejętność. Przełącznik „Odblokuj dostęp do internetu” jest nadrzędny, a każda umiejętność ma w profil.json zgodę `"internet": true` albo nie. Gdy wybierzesz umiejętność bez zgody (cde, amadeusz, office, ocr, tablice, tlumaczenie), program sam wyłącza przełącznik i pisze o tym w rozmowie. Możesz go świadomie włączyć z powrotem — wtedy internet działa także z tą umiejętnością, a nagłówek pokazuje „internet: tak — włączony mimo braku zgody umiejętności …”. Przełącznik jest jeden dla Czatu i Code. Zgodę mają m.in. komputer, pliki, tekla, rfem, mkk. Decyzja użytkownika z 2026-11-19.

# Menedżer poświadczeń: bazy, FTP, SMB, hasła

**Ustawienia → Menedżer poświadczeń**: wiersz = wpis (typ PostgreSQL / FTP / SMB, IP z portem, nazwa bazy, user, hasło, person_id). Zapis w Menedżerze poświadczeń Windows (wpisy `Amadeusz:postgres:<baza>`), zaszyfrowany kontem Windows, nigdy w plikach programu ani w repozytorium. Umiejętność z polem `poswiadczenie` (np. cde) jest widoczna dopiero, gdy jest jej wpis.
