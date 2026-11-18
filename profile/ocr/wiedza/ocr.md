# Jak działa OCR w Amadeuszu: Tesseract, języki, obrót, układ, pewność

- Silnik: Tesseract 5 (skrypt, bez modelu językowego). Strony PDF renderuje PDFium w 300 dpi (Ustawienia → OCR → rozdzielczość).
- Języki: dane `pol`, `eng` i `osd` (orientacja) pobierają się do `%LOCALAPPDATA%\LokalneAI\tessdata` przy pierwszym użyciu
  albo przyciskiem w Ustawieniach → OCR. Inne języki (deu, ces, ukr, fra…) też, kilka MB każdy.
- Obrót: „automatyczny” wykrywa strony zeskanowane bokiem albo do góry nogami (potrzebny `osd`); przy bardzo małej ilości
  tekstu wykrycie się nie udaje i strona idzie bez obrotu. Można też ustawić stały obrót (90/180/270).
- Układ: automatyczny (akapity, kolumny) albo „tabela” (odstępy między kolumnami zostają), „kolumna”, „luźny tekst”.
- Pewność: Tesseract podaje pewność każdego słowa; próg w ustawieniach pomija słowa poniżej (np. 60%), a wynik mówi, ile pominięto.
- Reguły ignorowania: wyrażenia regularne, po jednym w linii; linia pasująca do reguły znika z wyniku (stopki „Strona 3 z 12”,
  nagłówki firmowe, znaki wodne).
- Dzielenie wyrazów: „kon-” na końcu linii i „strukcja” na początku następnej są łączone.

# Słaby wynik OCR: co zrobić, pewność poniżej 70%, polskie znaki, tabele

- Zdjęcie z telefonu: krzywe, ciemne albo rozmyte — poproś o skan 300 dpi albo zdjęcie prosto z góry przy dobrym świetle.
- Polskie znaki giną (ł→l, ż→z): sprawdź, czy w językach jest `pol` (sam `eng` ich nie zna).
- Tekst bokiem: obrót automatyczny albo stały 90/270.
- Tabela czytana jako jeden ciąg: `uklad=tabela` (wiersz = linia, kolumny rozdzielone odstępami).
- Pieczątki, podpisy, adnotacje odręczne: `uklad=luzny`; pismo odręczne Tesseract czyta źle — to zadanie dla modelu z obsługą obrazów.
- Wzory matematyczne i rysunki techniczne: OCR daje strzępy; użyj zakładki Czat z modelem obsługującym obrazy.
- Bardzo mały druk (tabelki rysunkowe): rozdzielczość 400–600 dpi w ustawieniach.

# Obrazek wklejony do pola rozmowy (Ctrl+V): co się dzieje

- Program zapisuje obraz jako PNG w folderze rozmowy (`.amadeusz\obrazy\`) i od razu robi OCR z ustawieniami z Ustawienia → OCR.
- Rozpoznany tekst trafia do pola rozmowy z nagłówkiem `[obraz wklejony: nazwa.png — OCR 93%, 48 słów]`; użytkownik dopisuje
  polecenie i wysyła. Model dostaje tekst, nie obraz — nie trzeba wołać `ocr_obraz`.
- W zakładce Czat działa tak samo: tekst OCR wkleja się do pytania.
