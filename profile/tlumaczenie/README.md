# Profil `tlumaczenie`: teksty biura na angielski

Profil samej wiedzy (bez poleceń gospodarza, `narzedzia.json` z pustą tablicą): użytkownik wkleja tekst po polsku — opis
techniczny, mail, pismo, notatkę ze spotkania, tytuł rysunku, uwagi do rewizji — a model oddaje tłumaczenie na angielski
brytyjski w rejestrze formalnym, z terminologią Eurokodów. Terminy biura (ostroga, płatew, stężenie połaciowe, zestawienie stali,
projekt warsztatowy, nadzór autorski…) bierze ze słownika profilu przez `szukaj_wiedzy`, bo żaden lokalny model nie zna ich
z pamięci. Na prośbę tłumaczy też z angielskiego na polski. Nie otwiera plików i nie wysyła maili: razem z profilem `office`
asystent może przeczytać dokument (`word_czytaj`) i zapisać tłumaczenie do nowego pliku (`word_dopisz`, końcówka `_EN`).

## Pliki
| Plik | Co to |
|---|---|
| `profil.json` | `zakonczenie: model`, `szukaj_wiedzy`, `max_krokow: 3`, temperatura 0,2; bez `chmura` (tylko modele lokalne), bez internetu |
| `system.md` | 10 zasad (ok. 480 słów): tłumacz dokładnie, nic nie dodawaj; brytyjski, formalny; co zostaje bez zmian; liczby i daty; maile; sam tekst bez komentarzy; pytanie, gdy brak tekstu |
| `karta.md` | ściąga (ok. 950 tokenów): najczęstsze terminy w sześciu grupach i zasady zapisu |
| `przyklady.jsonl` | 14 przykładów: 10 ze słownikiem (`szukaj_wiedzy`), 4 pytania (brak tekstu, plik, inny język, wysyłka) |
| `testy.jsonl` | 10 testów T01–T10 (`LokalneAI.Eval test`): opis techniczny, liczby, mail, notatka, tytuł rysunku, obliczenia, dwa pytania, sesja z poprawką rejestru, kierunek odwrotny |
| `wiedza/slownik_konstrukcje.md` | 7 sekcji: elementy, połączenia, obciążenia i analiza, materiały, fundamenty, dokumentacja, role |
| `wiedza/zwroty.md` | maile, pisma przewodnie, notatki, uwagi do rewizji, tytuły rysunków |
| `wiedza/styl_i_jednostki.md` | brytyjski a amerykański, liczby, daty, jednostki, normy, skróty |

## Który model (pomiar 2026-11-17)

Ten sam opis techniczny (słupy HEB300, podstawy utwierdzone, ostroga, stężenia, płatwie, zestawienie stali, strop nad parterem,
spoiny pachwinowe) i mail (załącznik, prośba o uwagi, rysunki warsztatowe) przetłumaczone przez modele z tego komputera
(RTX 4060, Ollama, `think: false`, temperatura 0,2, **bez** słownika profilu — sama wiedza modelu):

| Model | Terminy trafione / błędy | Mail | tok/s |
|---|---|---|---|
| **qwen3.5:9b-q4_K_M** | purlins, roof bracing, fillet welds, steel schedule, client, 21 November; błędy: „anchored” (utwierdzone), „cleat” (ostroga), „ground-floor slab” (strop nad parterem) | Dear Sir or Madam / Yours faithfully, shop drawings — najlepszy | 44 |
| Bielik 11B Q4 | fixed, purlins, roof bracing, fillet welds, floor over the ground floor, steel schedule; błędy: „web stiffener” (ostroga), „investor”, 21.11 | Good morning / Best regards, workshop drawings | 23 |
| gemma4:12b | roof braces, fillet welds, steel schedule, Dear Sir/Madam, shop drawings; błędy: „rafters” (płatwie!), „anchored”, „stiffener” (ostroga), ground floor slab | poprawny, 21.11 | 30 |
| qwen3:14b | fixed, purlins, bracings, fillet welds, floor above the ground floor; błędy: „stiffener”, „steel summary”, Good day / Kind regards | słaby rejestr | 13 |
| ministral-3:14b | fixed, purlins, fillet welds; błędy: „roof trusses” (stężenia), „stiffener”, pogrubienia Markdown mimo zakazu | Good day, review report | 14 |
| gemma4:e4b | purlins, floor above the ground floor; błędy: „roof trusses”, „gusseted welds”, zmienił format daty, Good morning / Yours sincerely | słaby | 124 |
| qwen3.5:4b | purlins, client, 21 November; błędy: „roof trusses”, „lap splice welds”, „inspection report” (raport z rewizji), „anchor beam” | poprawny rejestr | 72 |

Wnioski:
- **Ostrogi (shear key) nie znał żaden model** — dlatego słownik w `wiedza\` i zasada 2 w `system.md` („słownik ma pierwszeństwo”).
- **Najlepszy lokalny: qwen3.5:9b-q4_K_M** — najbardziej idiomatyczny angielski (client, shop drawings, Dear Sir or Madam, daty).
  Wymaga `think: false` w zapytaniu, inaczej potrafi „myśleć” kilka minut; `OllamaLlmClient` wysyła to pole.
- **Zapas: Bielik 11B** — najpewniej rozumie polskie niuanse techniczne (utwierdzone = fixed, strop nad parterem), słabszy rejestr
  w mailach (zasady 5 w `system.md` to prostują). Na komputerze z 12 GB VRAM (ODS27) kandydatem jest też `qwen3.5:35b-a3b`.
- **Małe modele (4B, e4b) nie nadają się**: mylą stężenia z wiązarami i rodzaje spoin, zmieniają daty.
- **Najlepsza jakość w ogóle: Claude w chmurze** (profil musiałby dostać `"chmura": ["anthropic"]`; decyzja użytkownika, bo teksty
  biura wychodzą wtedy z komputera).

## Sprawdzone 2026-11-17 (qwen3.5:9b-q4_K_M, RTX 4060, `think: false`)
- `LokalneAI.Eval test`: 10/10 testów (11 kroków), 1–12 s na krok. Tłumaczenia: „Fixed bases: base plate PL40 500×500 mm,
  4 anchor bolts M30 … and a shear key made from an IPE120 section”, „Purlins made of IPE200 at 2.5 m centres; steel mass
  12,500 kg”, „Column utilisation ratio 0.87 (ULS), beam deflection L/250 (SLS), lateral-torsional buckling checked in
  accordance with EN 1993-1-1”, mail z „Dear Sir or Madam … Yours faithfully”, dopytania T07 i T08 zgodne z zasadami.
- Kontrola okna (`--harness tlumaczenie`): opis techniczny poprawny w 9,5 s bez `szukaj_wiedzy` (karta wystarczyła); pierwszy
  mail zgubił zdanie z terminem i dopisał „[Your Name]” — po zaostrzeniu zasad 1, 5 i 6 oraz wzorze maila w karcie oba
  maile i notatka ze spotkania przetłumaczone w całości (5 s i 3 s), bez podpisu.
- `ProfileFilesTests` i `DokumentacjaTests` przechodzą (prompt ok. 3,4 tys. tokenów na qwen3.5:9b).

## Jak sprawdzić
```powershell
dotnet test LokalneAI.Harness.Tests -f net10.0 --filter "FullyQualifiedName~ProfileFilesTests"
.\LokalneAI.Eval\bin\Debug\net10.0\LokalneAI.Eval.exe rozmiar --profil profile\tlumaczenie --model qwen3.5:9b-q4_K_M --ctx 16384
.\LokalneAI.Eval\bin\Debug\net10.0\LokalneAI.Eval.exe test --profil profile\tlumaczenie --model qwen3.5:9b-q4_K_M --ctx 16384
.\LokalneAI.Eval\bin\Debug\net10.0\LokalneAI.Eval.exe szukaj --profil profile\tlumaczenie --fraza "ostroga blacha podstawy"
```
`Eval test` w profilu bez poleceń sprawdza tylko, że model odpowiedział albo dopytał tam, gdzie powinien (T07, T08); jakość
tłumaczenia ocenia się w oknie (zakładka Code, „Pokaż kroki modelu”) albo w pliku wyników.

## Decyzje
- **Profil samej wiedzy, bez poleceń**: tłumaczenie to tekst → tekst; pliki obsługuje `office` (oba profile można zaznaczyć naraz).
- **Bez `chmura` i bez internetu**: tłumaczone bywają umowy i korespondencja. Dopisanie `"chmura": ["anthropic"]` to decyzja użytkownika.
- **Przykłady ze `szukaj_wiedzy`** w każdym tłumaczeniu technicznym: jedno dodatkowe wywołanie (kilka sekund), za to jednolite
  terminy biura zamiast zgadywania modelu.
- **Zasady zapisu w karcie** (12,5 → 12.5; 10.11.2026 → 10 November 2026; PN-EN → EN), bo właśnie tu modele się rozjeżdżały.
