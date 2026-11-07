# Lokalne modele dla „pół-harnessu” ODS — badanie (stan na 2026-11-06)

Metoda: przegląd rejestru ollama.com (tagi, rozmiary, kontekst — odczytane 2026-11-06), kart modeli na Hugging Face i prac na arXiv. Nic nie pobierano ani nie uruchamiano. Wszystko oznaczone **[niepotwierdzone]** to moja ocena lub pamięć, bez źródła w tym badaniu. Wartości VRAM to **moje szacunki** (rozmiar pliku + ok. 0,5–1,5 GB na KV-cache przy 8k i bufory), nie pomiary.

## 1. Streszczenie

- Nowa generacja (2026) wyprzedziła rodzinę, na której opieraliśmy się rok temu: w oficjalnym rejestrze Ollamy są teraz **Gemma 4** (e2b/e4b/12b/26b-a4b/31b, licencja Apache 2.0, natywne narzędzia), **Qwen3.5 / 3.6 / 3.8** (Apache 2.0 dla 3.5; tools + thinking), **Ministral 3**, **Granite 4.1/4.2**, **gpt-oss**. Bielik ma w Ollamie tylko konto społecznościowe `SpeakLeash/` (to konto wydawcy, ale nie biblioteka „library”).
- Dla polskiego: Bielik 11B v3 nadal prowadzi na Open PL LLM Leaderboard (65,93 vs Qwen3-14B 62,24, Gemma-3-12B 62,20). Nowy **Bielik-Minitron-7B v3.0** (7,35B, ok. 94,7% wyniku 11B: 62,46; BFCL bliski 11B) jest mniejszy i szybszy — **główny kandydat na PC 8 GB** (Q4_K_M 4,5 GB mieści się w całości na GPU).
- Dla narzędzi/JSON/SQL przewaga jest po stronie Qwen3.5 i Gemma 4 (BFCL-V4 9B = 66,1; Tau2: Gemma 4 12B 69,0, 31B 76,9). Dla Bielika brak wyniku BFCL-V4, ale na starszym BFCL non-live ma 96/88/82% (wynik dobry; „live” słabszy: 79% simple).
- **PC 8 GB (6 GB wolne):** punkt odniesienia Bielik 11B Q4_K_M + Minitron-7B + Qwen3.5-9B + Gemma 4 E4B + Qwen3.5-4B + jedna próba MoE (Gemma 4 26B-A4B, 18 GB, wymaga RAM, nie VRAM). Moja hipoteza do sprawdzenia: najlepszy kompromis polski/narzędzia to Minitron-7B (polski) albo Qwen3.5-9B / Gemma 4 E4B (narzędzia), a w potoku „Bielik pisze po polsku, inny model wybiera narzędzie” oba role mogą być różnymi modelami.
- **Serwer 1×24 GB:** Gemma 4 26B-A4B (QAT 16 GB) lub Qwen3.6 27B Q4_K_M (17 GB) jako model główny + opcjonalnie Bielik 11B Q8 (12 GB) przy `OLLAMA_MAX_LOADED_MODELS=2`. **Serwer 48 GB (2×24 lub 1×48):** Gemma 4 31B Q4_K_M (20 GB) lub Qwen3.6 27B Q8_0 (30 GB) i jeszcze miejsce na kontekst/równoległość.
- PLLuM: najnowsze wydania (12B/4B/8B/70B „2512”, Apache 2.0) istnieją na HF, ale **nie ma oficjalnych tagów w Ollamie** (tylko prywatne wrzutki użytkowników, ostatnie ok. rok temu) i brak publicznych liczb BFCL — odradzam na start.
- Wszystkie rekomendowane modele mają licencję Apache 2.0 lub zbliżoną (dozwolone użycie komercyjne) — szczegóły w pkt 2.

## 2. Tabela kandydatów

Oznaczenia: P = polski, N/SQL = narzędzia i SQL. VRAM@8k = szacunek własny. „—” = brak danych. Kontekst z kart tagów Ollamy.

| Model | Tag Ollamy | Rozmiar Q4 / Q8 | VRAM@8k (szac.) | Polski | Narzędzia / SQL | Licencja |
|---|---|---|---|---|---|---|
| Bielik 11B v3.0 Instruct | `SpeakLeash/bielik-11b-v3.0-instruct:Q4_K_M` (też Q5_K_M 7,9 / Q6_K 9,2 / `Q8_0` / `bf16` 22 GB); kontekst 32K; „tools” | 6,7 GB / 12 GB | ~8 GB (u nas 73% na GPU) | Bardzo dobry: Open PL LLM 65,93 (5-shot), PLCC 71,8 [1][2] | BFCL non-live 96/88/82%, live simple 79% [3]; nasze testy: SQL z nadmiarowymi filtrami, błędy arytmetyki | Apache 2.0 [niepotwierdzone dla 11B v3 w tym badaniu; potwierdzone dla Minitron] |
| Bielik-Minitron 7B v3.0 | `SpeakLeash/bielik-minitron-7B-v3.0-instruct:Q4_K_M` (Q5_K_M 5,3 / Q6_K 6,1 / `Q8_0` 7,9); 32K; „tools” | 4,5 GB / 7,9 GB | ~5,5–6 GB (mieści się na GPU) | Open PL LLM 62,46 (94,7% nauczyciela) [3][4] | BFCL non-live 94,5/92/85%, live simple 71,3%, live parallel 31% (słabo) [3] | Apache 2.0 (HF gated) [4] |
| Bielik 4.5B v3.0 | `SpeakLeash/bielik-4.5b-v3.0-instruct:Q8_0` (tylko Q8_0 5,1 GB i FP16 9,5 GB); kontekst tylko 8K; brak „tools” w tagu | — / 5,1 GB | ~6 GB | Bielik v3 Small: dobry jak na rozmiar [5] | brak tagu tools w Ollamie | Apache 2.0 [niepotwierdzone] |
| Bielik 1.5B v3.0 | `SpeakLeash/bielik-1.5b-v3.0-instruct` | małe | ~2 GB | Open PL 41,36 (> Qwen2.5-3B 41,23) [5] | zbyt słaby do narzędzi [niepotwierdzone] | Apache 2.0 [niepotwierdzone] |
| PLLuM 2512 (4B/8B/12B/70B) | brak oficjalnych tagów; tylko użytkownicy (np. `antoniprzybylik/pllum`, `PRIHLOP/PLLuM`, ~rok) | — | — | wg raportu z marca 2026 słabszy od Bielika [6] | brak danych (karta nie wspomina narzędzi) [7] | Apache 2.0, „commercial use allowed” [7] |
| Qwen3.5 4B | `qwen3.5:4b-q4_K_M` (3,3 GB) / `4b-q8_0` (5,2) | 3,3 / 5,2 GB | ~4–5 GB | 201 języków deklarowane [8]; polski nieweryfikowany w benchmarkach PL | patrz 9B | Apache 2.0 (karta 9B) [8] |
| Qwen3.5 9B | `qwen3.5:9b-q4_K_M` (6,6 GB) / `9b-q8_0` (10); kontekst 256K; vision, tools, thinking | 6,6 / 10 GB | ~8 GB (część na CPU) | MMMLU 81,2, MMLU-ProX(29 j.) 76,3, INCLUDE 75,6 [8]; PL leaderboard brak | BFCL-V4 66,1, Tau2 79,1 [8]; SQL — brak liczb dla tego modelu | Apache 2.0 [8] |
| Qwen3.5 27B | `qwen3.5:27b-q4_K_M` (17 GB) / `27b-q8_0` (30) | 17 / 30 GB | ~19 / ~32 GB | j.w. | Raschka: ≈ Gemma 4 31B w ogólnych testach [9] | Apache 2.0 |
| Qwen3.5 35B-A3B (MoE) | `qwen3.5:35b-a3b-q4_K_M` (22 GB) / `-q8_0` (39) | 22 / 39 GB | ~24 / ~41 GB | j.w. | j.w.; aktywne ~3B → szybki | Apache 2.0 |
| Qwen3.6 27B / 35B-A3B | `qwen3.6:27b-q4_K_M` (17), `qwen3.6:35b-a3b-q4_K_M` (24) | 17 / 30 oraz 24 / 39 | j.w. | — | tools, thinking (karta Ollamy) | [niepotwierdzone] |
| Qwen3.8 27B | `qwen3.8:27b` (18 GB; 256K) | 18 GB | ~20 GB | — | nowość 6 dni temu; tools; liczb brak [10] | [niepotwierdzone] |
| Qwen3 14B / 30B-A3B | `qwen3:14b-q4_K_M` (9,3) / `qwen3:30b-a3b-q4_K_M` (19); 40K | 9,3 / 16 GB (14b q8) | ~10 / ~20 GB | Open PL 62,24 (14B) [3] | tools; starsza generacja | Apache 2.0 [niepotwierdzone] |
| Gemma 4 E2B / E4B | `gemma4:e2b-it-q4_K_M` (4,6), `gemma4:e4b-it-q4_K_M` (6,6) / `e4b-it-q8_0` (9,0); `-it-qat` e4b 6,1 GB; 128K | 6,6 / 9,0 GB (E4B) | ~7–8 GB | MMMLU 76,6 (E4B); 140+ j. w pretreningu, 35+ „out of the box” [11][12] | natywne tools, Tau2 42,2 (E4B) [12] | Apache 2.0 [11][13] |
| Gemma 4 12B | `gemma4:12b-it-q4_K_M` (8,0) / `12b-it-qat` (7,2) / `12b-it-q8_0` (13); 256K | 8,0 / 13 GB | ~10 GB | MMMLU 83,4 [11] | Tau2 69,0, natywne function calling [11] | Apache 2.0 [11] |
| Gemma 4 26B-A4B (MoE) | `gemma4:26b-a4b-it-q4_K_M` (18) / `-it-qat` (16) / `-it-q8_0` (28); 256K | 18 / 28 GB | ~20 / ~30 GB | MMMLU 86,3 [12] | Tau2 68,2 [12] | Apache 2.0 |
| Gemma 4 31B | `gemma4:31b-it-q4_K_M` (20) / `31b-it-qat` (19) / `31b-it-q8_0` (34) | 20 / 34 GB | ~22 / ~36 GB | MMMLU 88,4 [12] | Tau2 76,9 [12] | Apache 2.0 |
| Ministral 3 8B / 14B | `ministral-3:8b-instruct-2512-q4_K_M` (6,0) / `-q8_0` (9,9); `ministral-3:14b-instruct-2512-q4_K_M` (9,1) / `-q8_0` (15); 256K; vision, tools | 6,0–9,1 / 9,9–15 | ~7 / ~11 GB | wg opisu „europejskie języki”; PL — brak liczb | tools; liczb brak | [niepotwierdzone] (Mistral zwykle Apache 2.0) |
| Mistral Small 3.2 24B | `mistral-small3.2:24b-instruct-2506-q4_K_M` (15) / `-q8_0` (26); 128K | 15 / 26 GB | ~17 / ~28 GB | Raport PL: Mistral najlepszy z europejskich, ale najsłabszy blok [6] | tools, vision | [niepotwierdzone] |
| gpt-oss 20B / 120B | `gpt-oss:20b` (14 GB), `gpt-oss:120b` (65 GB); 128K; tools, thinking | tylko MXFP4: 14 / 65 GB | ~16 / ~70 GB | PL — brak danych, słabszy według ogólnej wiedzy [niepotwierdzone] | tools, thinking | Apache 2.0 [niepotwierdzone] |
| Granite 4.1 / 4.2 | `granite4.1:8b`, `granite4.1:30b`, `granite4.2:3b/8b/30b` (rozmiary plików nieodczytane) | — | — | PL — brak danych | tools (4.1); nastawione na enterprise | Apache 2.0 [niepotwierdzone] |
| Phi-4 14B | `phi4` (14B) — bez tagu „tools” w rejestrze | ~9 GB [niepotwierdzone] | ~10 GB | Open PL 62,57 [3] | brak oficjalnego tools | MIT [niepotwierdzone] |
| DeepSeek-R1 destylaty | `deepseek-r1:8b/14b` | — | — | słaby PL [niepotwierdzone] | tools+thinking, ale „myślenie” zbędne i wolne | MIT [niepotwierdzone] |

Uwagi:
- Obsługa Ollamy: modele z badge „tools” w bibliotece mają szablon z narzędziami. `format` z JSON Schema działa w Ollamie dla każdego modelu (to ograniczenie gramatyki dekodowania, nie cecha modelu) [niepotwierdzone w tym badaniu, znana funkcja Ollamy]. Modele „thinking” (Qwen3.5, Gemma 4, gpt-oss) trzeba uruchamiać z `think:false`, bo inaczej generują `<think>`; Qwen3.5 nie wspiera `/nothink`, tylko parametr szablonu [8]. Dla SQL/JSON zalecana temperatura 0.
- Arytmetyka: żaden z tych modeli nie jest godny zaufania w sumowaniu; zostaje rachunek w bazie/kalkulatorze.
- BIRD/Spider dla małych modeli: nie znalazłem porównania Qwen3.5/Gemma 4/Bielik na BIRD. Jedyne źródło (preprint czerwiec 2026) dotyczy starszych modeli (Qwen2.5-Coder: 39→50 pkt BIRD dev 7B→duży) [14]. **Wniosek o SQL trzeba zrobić własnym testem na schemacie biura.**

## 3. Lista do lokalnego benchmarku (PC, 6 GB wolnego VRAM, 64 GB RAM)

Razem nowe pobrania ok. 39 GB (Bielik 11B już jest).

| # | Tag (`ollama pull ...`) | Rozmiar | Po co |
|---|---|---|---|
| 0 | `SpeakLeash/bielik-11b-v3.0-instruct:Q4_K_M` (jest) | 6,7 GB | punkt odniesienia |
| 1 | `SpeakLeash/bielik-minitron-7B-v3.0-instruct:Q4_K_M` | 4,5 GB | polski następca w mniejszym rozmiarze; w całości na GPU → pewnie szybciej niż 23 tok/s; BFCL ~11B |
| 2 | `qwen3.5:9b-q4_K_M` | 6,6 GB | najlepsze udokumentowane narzędzia (BFCL-V4 66,1) przy tym rozmiarze; polski do sprawdzenia |
| 3 | `gemma4:e4b-it-q4_K_M` | 6,6 GB | Apache 2.0, natywne tools, MMMLU 76,6; mocny polski w rodzinie Gemma historycznie [niepotwierdzone dla v4] |
| 4 | `qwen3.5:4b-q4_K_M` | 3,3 GB | test, jak nisko można zejść (szybki „router” narzędzi, mieści się w VRAM z zapasem) |
| 5 | `gemma4:26b-a4b-it-q4_K_M` | 18 GB | model MoE: tylko ~4B aktywnych, więc z 64 GB RAM może być używalny na tym PC; ten sam kandydat co na serwer — pokaże „sufit” jakości; prędkość nieznana |

Suma nowych: 4,5 + 6,6 + 6,6 + 3,3 + 18 = 39,0 GB.

Rezerwa, jeśli chcemy podmienić: `ministral-3:8b-instruct-2512-q4_K_M` (6,0 GB; szansa na dobry polski, brak liczb) albo `gemma4:12b-it-qat` (7,2 GB, Tau2 69,0, ale będzie częściowo w RAM).

Uzasadnienie: dwa modele polskie (11B referencyjny i Minitron), dwa najmocniejsze z udokumentowanymi narzędziami przy ~9B (Qwen3.5, Gemma 4 E4B), najmniejszy model do sprawdzenia dolnej granicy i jedno MoE. Pominąłem PLLuM (brak tagów oficjalnych), gpt-oss 20B (14 GB, polski bez danych), Phi-4 (brak tools) i DeepSeek-R1 (myślenie zbędne).

Proponowany zestaw testów: (a) 20 zapytań SQL na realnym schemacie (kontrola schematu vs alias, zbędne filtry), (b) 20 poleceń → JSON z `format` (schemat), (c) wybór narzędzia z listy 5–8 narzędzi, (d) 10 pytań konstrukcyjnych po polsku (podstawa słupa, kotwy, ostrogi, S235/S355, C25/30), (e) tok/s i % modelu na GPU (`ollama ps`), (f) stabilność przy `think:false`.

## 4. Serwer biurowy (kilka–kilkanaście osób)

Założenia: równoległość w Ollamie przez `OLLAMA_NUM_PARALLEL` (KV-cache rośnie liniowo z liczbą slotów — oszacowania poniżej własne, niezmierzone), `OLLAMA_MAX_LOADED_MODELS`, `OLLAMA_KEEP_ALIVE`. Przy 10+ użytkownikach rozważyć vLLM zamiast Ollamy [niepotwierdzone — oceniać po pomiarach]. Uwaga: wielu użytkowników wysyła krótkie zapytania, więc realna równoległość jest niższa niż liczba osób.

### Próg A: 1×24 GB VRAM (np. RTX 3090/4090) + ≥64 GB RAM
| Rola | Tag | Rozmiar | Uwagi |
|---|---|---|---|
| model główny (jakość) | `gemma4:26b-a4b-it-qat` (16 GB) lub `gemma4:26b-a4b-it-q4_K_M` (18) | 16–18 GB | MoE 4B aktywnych → szybki; ok. 6 GB zapasu na KV; ~3–4 sesje równoległe @8k [szac.] |
| alternatywa | `qwen3.6:27b-q4_K_M` (17) lub `qwen3.5:27b-q4_K_M` (17) | 17 GB | gęsty 27B, wolniejszy od MoE; 2–3 sesje @8k |
| model polski | `SpeakLeash/bielik-11b-v3.0-instruct:Q8_0` (12) albo `Q5_K_M` (7,9) | 8–12 GB | obok drugiego modelu tylko przy mniejszym głównym (np. `gemma4:12b-it-q4_K_M` 8 GB + Bielik Q5 7,9 GB) |
| lekki / szybki | `gemma4:12b-it-q4_K_M` (8) | 8 GB | ~6–8 sesji @8k [szac.] |
| MoE duży (za duży na VRAM) | `qwen3.5:35b-a3b-q4_K_M` (22) | 22 GB | ledwo; praktycznie 1–2 sesje, część na RAM |

### Próg B: 48 GB (2×24 GB lub 1×48 GB, np. RTX A6000/L40S)
| Rola | Tag | Rozmiar | Uwagi |
|---|---|---|---|
| model główny | `gemma4:31b-it-q4_K_M` (20) / `-qat` (19) | 20 GB | najlepsze Tau2 (76,9) i MMMLU (88,4) w tej rodzinie [12]; zostaje ~25 GB na KV → ok. 8–12 sesji @8k [szac.] |
| alternatywa jakościowa | `qwen3.6:27b-q8_0` (30) lub `gemma4:26b-a4b-it-q8_0` (28) | 28–30 GB | wyższa dokładność JSON/SQL (Q8), ok. 4–6 sesji |
| polski | `SpeakLeash/bielik-11b-v3.0-instruct:Q8_0` (12) | 12 GB | do generowania odpowiedzi po polsku, obok gemma4:31b (20 + 12 = 32 GB) |
| duży MoE | `qwen3.5:35b-a3b-q8_0` (39) / `qwen3.6:35b-a3b-q8_0` (39) | 39 GB | mieści się, ale mało na KV |
| poza zasięgiem | `gpt-oss:120b` (65 GB), `qwen3.5:122b-a10b` (81 GB) | — | wymagają ≥80 GB lub dużego offloadu do RAM |

Przy dwóch kartach po 24 GB Ollama dzieli model warstwami (pipeline), co nie przyspiesza pojedynczego zapytania; zaleta to większa pojemność. [niepotwierdzone — sprawdzić przy wdrożeniu]

## 5. Licencje i status

- Gemma 4: Apache 2.0 (karta HF modelu [11]; potwierdza też Raschka, że to zmiana wobec własnej licencji Gemma 3 [9]). Użycie komercyjne dozwolone.
- Qwen3.5: Apache 2.0 [8]. Użycie komercyjne dozwolone.
- Bielik-Minitron-7B v3.0: Apache 2.0 [4]. Bielik 11B v3 / 4.5B / 1.5B: licencję należy potwierdzić na kartach HF `speakleash/...` (nie udało się odczytać w tym badaniu; Bielik v2 był Apache 2.0 [niepotwierdzone]).
- PLLuM: Apache 2.0, kolekcja „commercial use allowed” [7]. Wcześniejsze wersje (np. Llama-PLLuM) mogą dziedziczyć licencję Llamy — sprawdzić przed użyciem wariantów „Llama-PLLuM”.
- Pozostałe (Ministral 3, Mistral Small 3.2, gpt-oss, Granite, Phi-4, Qwen3/3.6/3.8): nie potwierdzono licencji w badaniu; z pamięci zwykle Apache 2.0/MIT — **zweryfikować przed wdrożeniem**.

## 6. Czego nie udało się potwierdzić

- Brak wyników Gemma 4, Qwen3.5/3.6/3.8, Ministral 3 na Open PL LLM Leaderboard ani PLCC (strona codesota zwracała błąd 500; wyszukiwarka nie znalazła liczb). Ocena „polski” dla tych modeli opiera się na wynikach wielojęzycznych (MMMLU) i na poprzednikach (Gemma 3 12B: 62,2; Qwen3-14B: 62,2 na tym samym leaderboardzie vs Bielik 11B 65,9). **Polski trzeba sprawdzić własnym testem.**
- Brak publicznego porównania text-to-SQL (BIRD/Spider) dla Bielika, Qwen3.5, Gemma 4.
- Wydajność tok/s na RTX 4060 dla modeli MoE z offloadem do RAM — nieznana, do zmierzenia.
- Polish MT-Bench: nie znaleziono aktualnych wyników dla rozważanych modeli.
- Brak wyniku BFCL-V4 dla Bielika (jest tylko stare BFCL w pracy o Minitron).
- Dokładne rozmiary Granite 4.x i Phi-4 w Ollamie nieodczytane.

## 7. Źródła

1. Tagi Bielika 11B v3: https://ollama.com/SpeakLeash/bielik-11b-v3.0-instruct/tags (6,7/7,9/9,2/12/22 GB, 32K); wyszukiwanie: https://ollama.com/search?q=bielik
2. Bielik 11B v3 – Open PL 65,93 i PLCC 71,83: https://timesofai.pages.dev/2026/01/bielik-11b-v3-polish-llm (wtórne źródło; PLCC patrz też https://arxiv.org/pdf/2503.00995 i https://codesota.com/polish-llm)
3. Bielik-Minitron-7B: tabela Open PL i BFCL: https://arxiv.org/html/2603.11881 (część dokumentu >100k znaków nieprzeczytana)
4. Karta modelu Minitron-7B (Apache 2.0, 7,35B z 11,04B, ~90% jakości, do 50% szybciej): https://huggingface.co/speakleash/Bielik-Minitron-7B-v3.0-Instruct ; tagi Ollamy: https://ollama.com/SpeakLeash/bielik-minitron-7B-v3.0-instruct/tags
5. Bielik v3 Small (1.5B, 4.5B): https://arxiv.org/abs/2505.02550 ; tagi 4.5B: https://ollama.com/SpeakLeash/bielik-4.5b-v3.0-instruct/tags
6. Raport o generowaniu treści po polsku (9.03.2026, akt. 17.04.2026): https://jelesnianski.com/artificial-intelligence/llm-ranking-report-2026-on-content-generation-in-the-polish-language/
7. PLLuM: https://huggingface.co/CYFRAGOVPL , karta 12B: https://huggingface.co/CYFRAGOVPL/PLLuM-12B-instruct-2512 ; Ollama: https://ollama.com/search?q=pllum
8. Qwen3.5-9B (licencja, BFCL-V4 66,1, tryb thinking): https://huggingface.co/Qwen/Qwen3.5-9B ; tagi: https://ollama.com/library/qwen3.5/tags
9. Opinia Raschki o Gemma 4 vs Qwen3.5 27B i licencji: https://x.com/rasbt/status/2039780905619705902
10. https://ollama.com/library/qwen3.8 ; https://ollama.com/library/qwen3.6/tags
11. Gemma 4 12B (Apache 2.0, 140+ języków, Tau2 69,0): https://huggingface.co/google/gemma-4-12b-it
12. Strona Gemma 4 w Ollamie (MMMLU, Tau2 dla E2B/E4B/26B/31B): https://ollama.com/library/gemma4 ; tagi: https://ollama.com/library/gemma4/tags
13. Lista bibliotek Ollamy (tools, daty): https://ollama.com/library?sort=popular , https://ollama.com/search?c=tools
14. Text-to-SQL na BIRD, modele on-prem: https://arxiv.org/abs/2606.29733
15. Pozostałe tagi: https://ollama.com/library/ministral-3/tags , https://ollama.com/library/qwen3/tags , https://ollama.com/library/mistral-small3.2/tags , https://ollama.com/library/gpt-oss/tags , https://ollama.com/search?q=granite
16. Open PL LLM Leaderboard: https://huggingface.co/spaces/tomarni/open_pl_llm_leaderboard
