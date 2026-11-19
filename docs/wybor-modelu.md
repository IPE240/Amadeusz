# Wybór modelu

**Domyślny model to Bielik 11B v3** (`SpeakLeash/bielik-11b-v3.0-instruct:Q4_K_M`, ok. 6,7 GB). `instaluj.bat`
pyta, które modele pobrać, i zaznacza zalecany według pamięci karty graficznej (lista niżej, „Przy instalacji”). Wśród polskich modeli najlepiej odpowiada na pytania do bazy CDE, a polecenia na komputerze
wykonuje bez błędów.

## Wyniki

Pomiary z listopada 2026 na RTX 4060 (8 GB) przy kontekście 16 384 tokenów, w którym Bielik 11B mieści się na
karcie w ok. 60%. Profile i testy leżą w `profile\<nazwa>\testy.jsonl`, a pełne wyniki w `docs\badania\` i `wyniki\`.

| Model | Tekla: poprawne polecenia | s/krok | CDE: poprawny SQL | CDE: poprawna odpowiedź | s/krok |
|---|---|---|---|---|---|
| Bielik 11B v3 Q4_K_M | 81% | 9 | 62% | 60% (27/45) | 38 |
| Bielik-Minitron 7B v3 Q4_K_M | 83% | 3,7 | 47% | 53% (24/45) | 16 |
| Qwen3.5 9B Q4_K_M | 93% | 33 | 69% | 73% (33/45) | 40 |

Profil `komputer` (PowerShell, pliki, programy) sprawdzono na żywo zestawem poleceń: `cd` i utworzenie folderu,
5 najnowszych plików z podfolderami, zmiana nazw PDF-ów w podfolderze, skasowanie wskazanego pliku, liczenie
PDF-ów, wyszukanie pliku, odczyt pliku tekstowego, utworzenie folderu z kopią pliku, folder spoza dozwolonych.

| Model | Wynik | Czas na polecenie |
|---|---|---|
| Bielik 11B v3 | 9 z 9 | 6–18 s |
| Bielik-Minitron 7B v3 | 7 z 7 | 2–9 s |

Przed dopisaniem przykładów do profilu Minitron mylił się: przenosił plik zamiast go skasować i pomijał
`-Recurse`. Przykłady w `przyklady.jsonl` i ściąga w `karta.md` sterują małym modelem najmocniej.

## Kiedy co

- **Bielik 11B**: domyślnie. Polski, najlepszy z Bielików na pytaniach do bazy.
- **Bielik-Minitron 7B**: słabszy komputer albo gdy liczy się szybkość (2–3 razy szybszy). Wyraźnie słabszy w SQL.
  Instalacja: `instaluj.bat -Model SpeakLeash/bielik-minitron-7B-v3.0-instruct:Q4_K_M`.
- **Qwen3.5 9B**: najcelniejszy, ale nie polski i najwolniejszy. Prawdopodobnie liczy długo, bo ma włączone
  „myślenie”; do sprawdzenia z jego wyłączeniem.

## Przy instalacji

`instaluj.bat` bez parametrów pokazuje tę listę (`$katalog` w `instalator\instaluj.ps1`). Zalecany jest pierwszy model,
którego „karta” mieści się w pamięci karty graficznej (czytanej z rejestru sterownika, bo WMI kończy się na 4 GB):

| Nr | Model | Rozmiar | Karta | Do czego |
|---|---|---|---|---|
| 1 | Bielik 11B v3 Q4_K_M | 6,7 GB | 8 GB | domyślny w biurze |
| 2 | Qwen3.5 9B Q4_K_M | 6,6 GB | 8 GB | najcelniejszy w Code, tłumaczenia |
| 3 | Bielik-Minitron 7B v3 Q4_K_M | 4,5 GB | 6 GB | szybszy polski |
| 4 | Qwen3.5 4B Q4_K_M | 3,3 GB | 4 GB | słaby komputer (niesprawdzony w porównaniu) |
| 5 | Qwen3.5 2B Q4_K_M | 1,9 GB | bez karty | proste pytania w Czacie |
| 6 | Bielik 1.5B v3 Q8_0 | 1,7 GB | bez karty | polski, tylko Czat (bez narzędzi) |

Bielik 4.5B v3 jest w Ollamie tylko jako Q8_0 (5,1 GB) — większy od Minitrona 7B, więc go nie ma na liście.
Wybranych modeli może być kilka; pierwszy staje się modelem domyślnym programu, wszystkie trafiają do ulubionych.
Przy aktualizacji ustawienia zostają — instalator zmienia tylko model domyślny, gdy tego z ustawień nie ma w Ollamie.

## Niesprawdzone

Gemma 4 (e4b i 26B-A4B) i Qwen3.5 4B są pobrane, ale porównanie przerwało się przed nimi. Dokończenie zajmuje
ok. 1,5 h karty graficznej:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File narzedzia\porownaj_modele.ps1
```
