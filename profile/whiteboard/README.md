# Umiejętność `whiteboard`: tablica w zakładce Whiteboard

Umiejętność działa tylko w zakładce **Whiteboard** okna Amadeusza i nie pojawia się na liście umiejętności zakładki Code
(`ProfilesFolder.Hidden`). Sesja tablicy to ta umiejętność plus umiejętności zaznaczone w zakładce Code.

## Co dostaje model

Program składa opis tablicy (`BoardSnapshot.Describe` w `LokalneAI.Harness.Whiteboard`) i dokleja go do polecenia:

- rozmiar tablicy w pikselach i układ współrzędnych ((0, 0) = lewy górny róg, y w dół);
- teksty wpisane przez użytkownika **dosłownie** (pola tekstowe nie są spłaszczane do obrazu, więc nie trzeba OCR);
- rysunki użytkownika: linie, strzałki i prostokąty ze współrzędnymi, pióro i marker jako obszary;
- wklejone obrazy z położeniem, rozmiarem i tekstem z OCR (Tesseract, raz po wklejeniu);
- odręczne napisy: OCR samych pociągnięć pióra, dołączany tylko przy rozsądnej pewności;
- wcześniejsze rysunki modelu (warstwa Amadeusza).

Model, który widzi obrazy (Ollama: `vision` w `/api/tags`; Claude, GPT, Gemini), dostaje też obraz całej tablicy (PNG,
najwyżej 1568 px na dłuższym boku).

## Polecenia

| Polecenie | Co robi |
|---|---|
| `rysuj` | kształty na warstwie Amadeusza: `linia`, `strzalka`, `prostokat`, `elipsa`, `lamana`, `tekst`; kolor z palety, domyślnie fioletowy; współrzędne poza tablicą są przycinane |
| `wyczysc_rysunki` | kasuje rysunki Amadeusza; rysunki użytkownika zostają |
| `kalkulator` | rachunki (wbudowane) |

Rysunki modelu są wektorowe (kształty WPF), na osobnej warstwie: przycisk „Rysunki Amadeusza” je chowa, a „Wyczyść
rysunki Amadeusza” kasuje. Model nie może zmienić ani skasować rysunków użytkownika.

Testy w `testy.jsonl` sprawdzają tylko czyszczenie i dopytanie: współrzędnych rysunku nie da się porównać dosłownie.
