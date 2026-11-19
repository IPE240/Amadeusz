# Kontrola bez klikania: LokalneAI.exe --autotest (pytanie, zrzut, wynik)

`LokalneAI.exe --autotest "pytanie" --wynik C:\Temp\wynik` otwiera okno poza ekranem (bez fokusu), zadaje pytanie w Czacie, zapisuje `okno.png`, `lista.png` i `wynik.json`, po czym się zamyka. Nie zapisuje ustawień, historii ani liczników tokenów użytkownika. Działa obok otwartego okna, z własną bramą na wolnym porcie. Kilka pytań: `"pytanie 1||pytanie 2"`. `--autotest -` = sam zrzut okna po starcie. W PowerShellu dopisz `| Out-Null`, żeby poczekać na koniec; kod wyjścia w `$LASTEXITCODE`.

# Polecenie umiejętności bez klikania: --harness (zakładka Code)

```powershell
& "$env:LOCALAPPDATA\Programs\LokalneAI\LokalneAI.exe" --autotest "jaki Wy ma IPE 200?" --harness tablice --model qwen3.5:9b-q4_K_M --wynik C:\Temp\a | Out-Null
Get-Content C:\Temp\a\harness.json -Encoding UTF8
```
`--harness tekla+pliki` zaznacza kilka umiejętności. Polecenia w `--autotest` po kolei (rozdziela `||`). Kontrola **sama zatwierdza zwykłe zmiany** („zawsze pytaj” strażnika — instalacje, rejestr, zamykanie programów — odrzuca), dlatego umiejętności plikowe (pliki, komputer, office, ocr, rfem) pracują w folderze z `--foldery "C:\a;C:\b"`, a bez niego w pustym `%TEMP%\LokalneAI_kontrola`. Wynik: `harness.png` i `harness.json` (profil, liczy, kontekst, rozmowa, wpisy z polami rodzaj, tekst, szczegoly, ok, metryki).

# Przełączniki LokalneAI.exe: wynik, model, url, zakladka, cel, port, udostepnij

- `--wynik <folder>` — folder wyników (domyślnie `%TEMP%\LokalneAI-autotest`);
- `--model <tag>`, `--url <adres Ollamy>`;
- `--zakladka czat|code|whiteboard|ustawienia|kolejka` — zakładka na zrzucie (`whiteboard` — kontrola tablicy, niżej); `kolejka` = Ustawienia z rozwiniętą kolejką i licznikami tokenów; `--rozwin` = ustawienia;
- `--cel <komputer>` — licz na innym komputerze (tylko to uruchomienie);
- `--udostepnij` — brama przyjmuje „inne komputery” tylko z 127.0.0.1 (próba bez portu w sieci);
- `--port <n>` — port bramy kontroli;
- `--ustawienia <plik>`, `--klucze <plik>` — ustawienia i klucze API z innych plików;
- `--rozmowy <folder>` — zapisuj rozmowy Code w `<folder>\.amadeusz\`;
- `--polaczenie <plik.json>`, `--ja <person_id>` — baza dla umiejętności cde;
- `--foldery`, `--harness` — wyżej.

# Polecenia #tokeny, #profil, #wznow, #zgoda, #lista, #zrodla, #ikona

W `--autotest` przy `--harness`, rozdzielane `||` jak zwykłe polecenia:
- `#tokeny` — włącza „Pokaż tokeny i czasy” (pole `metryki` wpisów w harness.json);
- `#profil:Biuro` — wybiera profil (zestaw umiejętności);
- `#wznow` — otwiera najnowszą rozmowę z `--rozmowy`;
- `#zgoda` — nie zatwierdza sam: zrzut pytania o zgodę (`zgoda.png`), potem „Tak”;
- `#lista` — zrzut listy umiejętności (`umiejetnosci.png`);
- `#zrodla` — zrzut okna „Źródła wiedzy” (`zrodla.png`).
Przy Czacie: `#ikona` — podgląd ikony w zasobniku (`ikona.png`).

# Kontrola tablicy: --zakladka whiteboard, #demo, #obraz:, #capture

`LokalneAI.exe --autotest "#demo||zaznacz środek belki" --zakladka whiteboard --model gemma4:12b --wynik C:\Temp\t` — polecenia idą po kolei przez „Zatwierdź”. Specjalne:
- `#demo` — przykładowy szkic (belka z podporami, siła, pętla piórem, dwa teksty);
- `#obraz:C:\plik.png` — szkic i obraz z pliku (jak wklejony, z OCR);
- `#capture` — próba Capture: ramka 360×240 w lewym górnym rogu ekranu, zrzut na tablicę.
Wynik: `whiteboard.png` (okno), `tablica.png` (tablica jak dla modelu), `whiteboard.json` (`opis` wysłany modelowi, `rysunkiAmadeusza`, `wpisy`).

# Pliki wyników i kody wyjścia kontroli (0, 1, 2, 3)

- `wynik.json` (Czat): pytanie, odpowiedz, statystyki, blad, tokenyNaSekunde, udzialGpu, ollama, karta, liczy, model, modele, kolejka, brama, gdzie, baner, status.
- `harness.json` (Code): wpisy z rodzajem User, Call, Answer, Question, Warning, Error, Info, Confirm, Step, Agent.
- `wyjatek.txt` — gdy kontrola się wysypała.
Kody wyjścia: 0 — odpowiedź; 1 — błąd w odpowiedzi (Czat) albo wpis Error (Code); 2 — wyjątek kontroli (wyjatek.txt); 3 — nieoczekiwany błąd okna (`%LOCALAPPDATA%\LokalneAI\bledy.log`).

# LokalneAI.Eval: szukaj, rozmiar, test, wiedza (sprawdzanie umiejętności)

W repozytorium po `dotnet build`: `LokalneAI.Eval\bin\Debug\net10.0\LokalneAI.Eval.exe`
- `szukaj --profil profile\tablice --fraza "IPE 200"` — co model dostałby z szukaj_wiedzy (bez modelu);
- `rozmiar --profil profile\tekla --model <tag>` — ile tokenów zajmuje polecenie systemowe;
- `test --profil profile\tekla --model <tag> --ctx 16384` — zestaw testy.jsonl z prawdziwym modelem (Ollama 127.0.0.1:11434 albo `--url`); dla cde także `--polaczenie` i `--ja`;
- `wiedza --profil profile\mkk` — aktualizacja wiedzy z zrodla.json.
Wyniki testów idą do `wyniki\` (poza gitem).

# LokalneAI.Eval straznik i atak: sprawdzanie strażnika bezpieczeństwa

- `straznik --plik LokalneAI.Harness.Tests\Dane\straznik_korpus.jsonl` — tabela zgodności strażnika (zgodne, za łagodnie, za ostro); nic nie wykonuje;
- `atak --plik LokalneAI.Eval\Dane\bezpieczenstwo_scenariusze.jsonl --model <tag>` — wstrzyknięcia na prawdziwym modelu z wykonawcą-atrapą (nic się nie wykonuje, sieć udawana); `--wymus-czytanie` — treść ze wstrzyknięciem od razu w historii, `--bez-zasad` — bez sekcji „Bezpieczeństwo” w poleceniu systemowym (mierzy sam strażnik).
Zasady strażnika: docs\bezpieczenstwo.md w repozytorium.
