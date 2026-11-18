# OCR w biurze — ściąga

## Które narzędzie
- obraz (JPG, PNG, zdjęcie z telefonu, zrzut ekranu) → `ocr_obraz`.
- PDF ze skanera, „nie da się zaznaczyć tekstu” → `ocr_pdf` (strony porcjami: `1-5`).
- PDF z Worda/CAD, tekst się zaznacza → `pdf_czytaj` (szybko, bez OCR); gdy nie wiesz → `pdf_info` mówi, ile stron ma tekst.
- cały folder skanów → `ocr_folder` (teksty obok plików, `.ocr.txt`).
- obrazek wklejony do rozmowy → tekst jest już w poleceniu; nie wołaj OCR drugi raz.

## Układ (`uklad`)
- `auto` — pisma, raporty, notatki (domyślnie).
- `tabela` — zestawienia stali, harmonogramy, tabelki rysunkowe: odstępy między kolumnami zostają, więc wiersz tabeli to jedna linia.
- `kolumna` — jedna kolumna tekstu w różnych rozmiarach (plakaty, strony tytułowe).
- `luzny` — pieczątki, adnotacje na rysunkach, numery na zdjęciach.

## Język (`jezyk`)
kody Tesseracta: `pol`, `eng`, `deu`, `ces`, `ukr`; kilka naraz przez `+` (`pol+eng`). Brakujące dane języka program pobiera sam (kilka MB) albo mówi, że nie może.

## Jak czytać wynik
- Nagłówek: pewność w % (poniżej 70% = ostrożnie), liczba słów, czas, obrót strony (wykryty automatycznie).
- `--- strona N ---` rozdziela strony PDF. `(OCR, 91% pewności)` przy stronie w `pdf_czytaj` = skan czytany przez OCR.
- Polskie znaki czasem giną (ł→l, ę→e) przy słabym skanie: nie „poprawiaj” nazwisk ani numerów, zaznacz niepewność.
- Masa stali w zestawieniach: liczby czytaj z wiersza tabeli (`uklad=tabela`), przecinek dziesiętny zostaje jak w dokumencie.

Teczki projektów mają nazwy `NNNN_SKRÓT_Nazwa` (np. `1095_WSP`), skany zwykle w podfolderze `skany` albo `korespondencja`.
