# Umiejętność `ocr`: tekst ze skanów, zdjęć i PDF-ów

OCR robi skrypt, nie model: Tesseract 5 (Apache 2.0, dane tessdata_fast) rozpoznaje obraz, PDFium renderuje strony PDF,
a model dostaje gotowy tekst z pewnością rozpoznania. Ustawienia (języki, obrót, układ, próg pewności, reguły ignorowania,
rozdzielczość, folder danych języków) są w oknie: Ustawienia → OCR. Obrazek wklejony do pola rozmowy (Ctrl+V w Code albo
w Czacie) jest rozpoznawany od razu — tekst z nagłówkiem `[obraz wklejony: … — OCR 93%, 48 słów]` trafia do pola, użytkownik
dopisuje polecenie i wysyła. Model dostaje tekst, nie obraz.

## Polecenia

| Polecenie | Co robi |
|---|---|
| `ocr_obraz` | PNG, JPG, TIFF, BMP, GIF, WebP → tekst; `jezyk` (pol+eng, deu…), `uklad` (auto, tabela, kolumna, luzny), `zapisz_txt` (plik `<nazwa>.ocr.txt` obok) |
| `ocr_pdf` | skany w PDF: strony renderowane w ustawionej rozdzielczości i rozpoznawane; `strony` (1-3,5,8-), najwyżej 20 stron naraz |
| `ocr_folder` | wszystkie obrazy i PDF-y w folderze (najwyżej 50) → teksty obok jako `.ocr.txt` (z pytaniem o zgodę) |
| `pdf_info` | liczba stron, format (A4, A3…), rozmiar, tytuł, ile stron ma warstwę tekstową (dokument) a ile to skany |
| `pdf_czytaj` | tekst stron z warstwy tekstowej; strony bez tekstu idą przez OCR same (`(OCR, 91% pewności)` w nagłówku strony) |
| `szukaj_plikow`, `pokaz_zawartosc` | jak w `komputer` |

Polecenia `pdf_polacz`, `pdf_wytnij` i `pdf_obroc` są w umiejętności `office` (ten sam wykonawca `OcrExecutor`).

## Pliki
| Plik | Co to |
|---|---|
| `profil.json` | `zakonczenie: mieszane` (po zapisie koniec tury, po czytaniu model odpowiada), `szukaj_wiedzy`, bez `chmura`, bez internetu |
| `system.md` | rola i 10 zasad: najpierw narzędzie, pełna nazwa pliku = od razu polecenie, PDF najpierw `pdf_info`, tekst wklejonego obrazka już jest w poleceniu, niska pewność = powiedz, tabele i wzory = model z obrazami |
| `karta.md` | ściąga: które narzędzie, układy, języki, jak czytać wynik |
| `narzedzia.json` | 7 poleceń |
| `przyklady.jsonl` | 15 przykładów, w tym wklejony obrazek bez wołania OCR, dopytanie o plik, odmowa przy wzorze |
| `testy.jsonl` | 10 testów R01–R10 (`LokalneAI.Eval test`) |
| `wiedza/ocr.md` | jak działa OCR w Amadeuszu, co robić przy słabym wyniku, co się dzieje po wklejeniu obrazka |

## Jak działa (`LokalneAI.Harness.Ocr`)
- `OcrEngine`: Tesseract LSTM na obrazie z pliku, PNG z pamięci albo stronach PDF. Obrót: wykrycie przez dane `osd`
  (poprawka = dopełnienie do 360° zgłoszonego obrotu) albo stały. Układ: PSM 3 (auto), 6 + `preserve_interword_spaces`
  (tabela), 4 (kolumna), 11 (luźny). Tekst składany z TSV (linie wg bloków i akapitów), słowa poniżej progu pewności
  pomijane, linie pasujące do reguł wyrzucane, wyrazy dzielone na końcu linii łączone.
- `PdfTools`: PDFium (Docnet.Core) czyta warstwę tekstową i renderuje strony (BGRA → PNG na białym tle przez `PngWriter`,
  bo PDFium daje przezroczyste tło); PDFsharp 6.1 (MIT) scala, wycina i obraca strony — zawsze do nowego pliku.
- `OcrLanguages`: dane języków pobierane z `https://github.com/tesseract-ocr/tessdata_fast` do
  `%LOCALAPPDATA%\LokalneAI\tessdata` (kilka MB na język; `osd` ok. 10 MB) przy pierwszym użyciu albo przyciskiem w ustawieniach.
- Natywne dll (x64): `tesseract50.dll`, `leptonica-1.82.0.dll`, `pdfium.dll` — kopiowane przez paczki NuGet do folderu programu.
- Okno: `OcrViewModel` (Ustawienia → OCR, `AppSettings.Ocr*`), `OcrViewModel.ToOptions` dla wykonawcy w zakładce Code,
  `HarnessPanel.ImagePasted` i `QuestionBox_Pasting` dla wklejonych obrazków (zapis do `.amadeusz\obrazy\` folderu rozmowy
  albo `Dokumenty\Amadeusz\Czat\obrazy\`).

## Jak sprawdzić
```powershell
dotnet test LokalneAI.Harness.Tests -f net10.0 --filter "FullyQualifiedName~Ocr"      # koder PNG, zakresy, filtry, decyzje + próby na Tesseracie i PDFium
.\LokalneAI.Eval\bin\Debug\net10.0\LokalneAI.Eval.exe test --profil profile\ocr --model qwen3.5:9b-q4_K_M --ctx 16384
.\LokalneAI.App\bin\Debug\net10.0-windows\LokalneAI.exe --harness ocr --foldery C:\folder_ze_skanami --autotest "zrób OCR pliku skan.png" --wynik C:\wynik
```
Próby na żywo (`OcrLiveTests`) tworzą PDF z polskim tekstem w PDFsharp (czcionka Arial z Windows), renderują go, rozpoznają,
obracają o 90° i sprawdzają wykrycie orientacji; bez danych języków w `%LOCALAPPDATA%\LokalneAI\tessdata` przechodzą bez sprawdzania.

## Sprawdzone 2026-11-18
- `OcrTests`: 28/28 na net10.0 i net48 — koder PNG (alfa na biel), zakresy stron, filtry i TSV, decyzje wykonawcy, pliki
  umiejętności; na żywo: PDF z PDFsharp (Arial) → PDFium → Tesseract 94% pewności, wykrycie obrotu 90° przez `osd`,
  `pdf_info`, `pdf_czytaj`, `ocr_pdf` z układem tabeli, `ocr_folder`, scalanie/wycinanie/obrót PDF.
- `LokalneAI.Eval rozmiar` (qwen3.5:9b): 3194 tokenów z pytaniem. `LokalneAI.Eval test`: 10/11 kroków (R01–R09 i R10/1 trafione;
  w R10 model po `pdf_info` od razu sam zrobił `ocr_pdf`, więc w drugim kroku nie miał już czego wołać — zachowanie dobre,
  test liczy to jako brak wywołania).
- Kontrola okna (`--harness ocr`, qwen3.5:9b, obraz 1800×700 z tekstem Arial 28): „zrób OCR pliku protokol.png i powiedz, kto
  jest wykonawcą i jaka jest masa stali” → `ocr_obraz` 94% pewności, 41 słów, 0,5 s; odpowiedź: Stal-Mont Sp. z o.o., 12 500 kg
  (5 s). Kontrola `--harness office`: `pdf_info` → „1 strona, dokument tekstowy”, `pdf_czytaj` → treść, `pdf_obroc` → nowy plik.

## Czego nie sprawdzono
- Wklejenia obrazka Ctrl+V w prawdziwym oknie (ścieżka `DataObject.Pasting` → `ImagePasted` → zapis i OCR); kontrola bez
  klikania nie ma schowka. Pierwszy test należy do użytkownika: zrzut ekranu (Win+Shift+S) → Ctrl+V w polu rozmowy.
- Przycisku „Pobierz brakujące” w oknie (kod `OcrLanguages.DownloadMissingAsync` sprawdzony przez pobranie `pol`, `eng`, `osd`
  ze skryptu). Dane leżą już w `%LOCALAPPDATA%\LokalneAI\tessdata` na tym komputerze.
- Skanów o słabej jakości (zdjęcia z telefonu) i pisma odręcznego — Tesseract radzi sobie z nimi słabo z założenia.

## Decyzje
- **Tesseract zamiast OCR Windows (WinRT)**: działa też w programach na .NET Framework 4.8, nie wymaga pakietu językowego
  Windows, ma OSD, tryby układu i pewność słów; dane języków pobierane osobno, żeby nie trzymać 20 MB w repozytorium.
- **AI do OCR tylko na życzenie**: zasada 6 w `system.md` — przy tabelach, wzorach i rysunkach model mówi, że to zadanie
  dla modelu z obsługą obrazów (zakładka Czat), nie wysyła obrazu sam.
- **Wklejony obrazek → tekst do pola, nie wysyłka od razu**: użytkownik widzi, co rozpoznano, dopisuje polecenie i decyduje.
- **Bez `chmura` i bez internetu**: skany to dokumenty biura.
- **Nic nie nadpisuje**: `pdf_*` zawsze do nowego pliku, `.ocr.txt` nadpisywany tylko po pytaniu z domyślnym „Nie”.
