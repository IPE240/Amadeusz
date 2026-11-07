# Harness dla lokalnych LLM w programach ODS - badanie (2026-11-06)

Status wiarygodności: [P] = potwierdzone w źródle w tej sesji (link); [W] = z wiedzy ogólnej/pamięci, NIE zweryfikowane teraz - sprawdzić przed decyzją.

## 1. Rekomendacja

1. Napisać własny, cienki harness (ok. 500-800 linii C#, netstandard2.0) i osadzać go w gospodarzach. Profile A i B to pętla o 2-4 krokach z walidacją - żaden duży framework nie daje wartości proporcjonalnej do zależności i ryzyka na .NET Framework 4.8.
2. Warstwę HTTP zrobić ręcznie (HttpClient + JSON) wobec Ollamy. Opcjonalnie OllamaSharp lub Microsoft.Extensions.AI.Abstractions jako cienka zależność, ale własny klient to ok. 150 linii i daje pełną kontrolę nad `format`, `options.num_ctx`, `keep_alive` i licznikami.
3. Harness działa w gospodarzu (biblioteka), bo to gospodarz ma katalog poleceń i wykonawcę (Tekla, DB). Serwer ma być "głupi": model + reverse proxy z kluczem. Słabe laptopy: tylko UI + lekki harness.
4. Python (CDE): ta sama idea, osobno - cienka pętla na `requests` + `jsonschema`/pydantic; ewentualnie pydantic-ai. Wspólny jest kontrakt (schemat JSON narzędzi), nie kod.
5. Serwer: dla 5-15 osób na jednym GPU zacząć na Ollamie (już działa, NUM_PARALLEL 3-4), licznik kontekstu oprzeć na metodzie niezależnej od cache (p. 4). Przejść na llama.cpp `llama-server`, gdy potrzebne `/tokenize`, jawne sloty lub wbudowany klucz API. vLLM odpada (brak natywnego Windows) [W].
6. Do wywołań narzędzi użyć `format` ze schematem (gramatyka), nie natywnych `tools` - pewniejsze dla małych modeli i niezależne od szablonu modelu. Schemat ma być prosty (p. 5).
7. MCP nie wprowadzać w gospodarzach .NET 4.8 (SDK C# celuje w nowsze runtime'y [W]); opisy narzędzi trzymać jednak w formacie zgodnym z JSON Schema/MCP.
8. Text-to-SQL (B): własny prompt z zawężonym schematem + przykłady + SELECT-only wymuszone rolą bazy + samopoprawa po błędzie. Vanna/WrenAI/DB-GPT nie osadzać (wzorce skopiować).

## 2. Tabela bibliotek

| Nazwa | Język/runtime | net48? | Structured output | Licencja | Uwagi |
|---|---|---|---|---|---|
| Microsoft.Extensions.AI (+Abstractions) | .NET, netstandard2.0 | Tak (pakiet Ollama miał netstandard2.0/net462 [P]) | `ChatOptions.ResponseFormat` zależnie od klienta | MIT [W] | Tylko abstrakcje. Pakiet Microsoft.Extensions.AI.Ollama jest wycofany, zalecany OllamaSharp [P] https://www.nuget.org/packages/Microsoft.Extensions.AI.Ollama/ |
| OllamaSharp | .NET, netstandard2.0+ [W] | Tak [W] | `format` ze schematem, IChatClient | MIT [W] | Najlepszy gotowy klient .NET dla Ollamy |
| Semantic Kernel | .NET | Ryzykownie (duży graf zależności) [W] | Przez connectory; function calling oparty o natywne tools | MIT [W] | Ciężki, wypierany przez Agent Framework; zbędny |
| Microsoft Agent Framework | .NET (net8+, część pakietów z grupą net472 [P]), Python | Niepewne, nieprzetestowane https://www.nuget.org/packages/Microsoft.Agents.AI.Hosting/ | Przez M.E.AI | MIT [W] | Młody; nadmiarowy (workflow, hosting) |
| LLamaSharp | .NET, llama.cpp in-process | Tak, ale natywne binaria [W] | Gramatyka GBNF | MIT [W] | Inferencja w procesie gospodarza - nie pasuje do serwera w LAN |
| pydantic-ai | Python | n/d | Tak (typy wyjścia, narzędzia) | MIT [W] | Najlżejszy sensowny gotowiec w Pythonie |
| smolagents | Python | n/d | CodeAgent lub tool-calling | Apache-2.0 [W] | Model pisze kod - nie do konstrukcji/DB; odradzam |
| LangChain/LangGraph | Python | n/d | with_structured_output | MIT [W] | Duże zależności, zmienne API |
| LlamaIndex | Python | n/d | Tak | MIT [W] | Nastawiony na RAG; zbędny |
| Vanna | Python | n/d | text-to-SQL z RAG po DDL i parach pytanie-SQL | MIT [W] | Dobry wzorzec do skopiowania |
| WrenAI | Python/TS, serwis | n/d | warstwa semantyczna (MDL) | AGPL-3.0 [W] | AGPL - nie osadzać |
| DB-GPT | Python | n/d | text-to-SQL, agenci | MIT [W] | Duża platforma; zbędna |
| MCP SDK C# | .NET 8+ [W] | Raczej nie | n/d (protokół narzędzi) | MIT/Apache [W] | Nie na net48 |
| MCP SDK Python | Python 3.10+ [W] | n/d | JSON Schema narzędzi | MIT [W] | Przydatne, jeśli narzędzia mają służyć też innym klientom |

Licencje i TFM oznaczone [W] zweryfikować przed użyciem; w tej sesji potwierdzono tylko wiersze z linkami [P].

## 3. Serwer modelu w LAN (5-15 użytkowników, jedno GPU)

**Ollama** (FAQ: https://docs.ollama.com/faq)
- `OLLAMA_HOST=0.0.0.0:11434` wystawia API w LAN; `OLLAMA_NUM_PARALLEL` (równoległe żądania na model), `OLLAMA_MAX_LOADED_MODELS`, `OLLAMA_KEEP_ALIVE` / pole `keep_alive` (domyślnie 5 min) [W].
- Pamięć KV rośnie z NUM_PARALLEL: alokowane jest `num_ctx x num_parallel` tokenów [W].
- Brak uwierzytelniania [W]. Zabezpieczenie: nasłuch na localhost + reverse proxy (Caddy/nginx) z `Authorization: Bearer <klucz>` i TLS; zapora Windows ograniczona do podsieci biura. `OLLAMA_ORIGINS` dotyczy tylko CORS i nie jest zabezpieczeniem.
- Żądania ponad NUM_PARALLEL czekają w kolejce (OLLAMA_MAX_QUEUE) [W]. Przy sporadycznych mini-sesjach 4 sloty wystarczą dla 10-15 osób.

**llama.cpp `llama-server`**: `--parallel N` (sloty; `--ctx-size` jest dzielony na sloty), `--cache-reuse`, endpointy `/tokenize`, `/detokenize`, `/apply-template`, `/slots`, API zgodne z OpenAI, `--api-key`, `json_schema`/`grammar` [W]. Zalety: dokładna tokenizacja, jawna kontrola KV, klucz bez proxy. Wady: ręczne zarządzanie GGUF, brak wielomodelowości jak w Ollamie. O reużyciu KV: https://github.com/ggml-org/llama.cpp/discussions/13606 [P - strona istnieje, treści nie czytano].

**vLLM**: Linux; na Windows tylko WSL2/Docker [W]. Najlepsza przepustowość (continuous batching), ale przesada dla 5-15 osób.

**LM Studio server**: wygodne, API OpenAI; brak uwierzytelniania, warunki licencji do sprawdzenia dla zastosowań komercyjnych [W]. Odradzam na serwer biurowy.

**Wybór**: Ollama z NUM_PARALLEL=4 na start; llama-server, gdy potrzebny dokładny licznik i kontrola.

**VRAM na równoległe sesje**
KV na token = 2 (K,V) x warstwy x n_kv_heads x head_dim x bajty (f16 = 2).
Przykład dla architektury typu Mistral 7B/Bielik 11B: 48 warstw, 8 głowic KV, head_dim 128 -> 196 608 B, czyli ok. 192 KiB/token [W; parametry sprawdzić w config.json modelu]. Kontekst 8k na sesję = ok. 1,5 GiB; 4 sloty x 8k = ok. 6 GiB. Kwantyzacja KV q8_0 (`OLLAMA_KV_CACHE_TYPE=q8_0`, wymaga `OLLAMA_FLASH_ATTENTION=1`) daje mniej więcej połowę [W]. Do tego wagi (Bielik 11B Q4_K_M ok. 6,5-7 GiB, Q8 ok. 12 GiB) i bufory ok. 1-1,5 GiB.
Wzór planowania: VRAM >= wagi + sloty x ctx x KV/token + ~1,5 GiB. Sesje A/B mieszczą się w 4-8k - nie ustawiać większego `num_ctx`, bo każdy slot płaci za całość.

## 4. Licznik kontekstu

**Potwierdzone / niepotwierdzone**
- Źródło wtórne: w Ollamie 0.33.3 `prompt_eval_duration` mierzy tylko tokeny niebuforowane, a `prompt_eval_count` nadal liczy cały prompt; natywne API ma pole `prompt_eval_cached_count` [P - notatki wydania zewnętrznego projektu, nie dokumentacja Ollamy]: https://github.com/LogneBudo/llmxray/releases/tag/v0.5.1
- Książka (nieoficjalna): `prompt_eval_count` to pełny rozmiar kontekstu żądania, nie liczba nowo policzonych tokenów; do oceny cache służy `prompt_eval_duration` [P]: https://leanpub.com/read/ollama/prompt-caching
- Z pamięci: historycznie przy trafieniu w cache pole bywało pomijane lub zaniżone (issue ollama/ollama #2068) [W - numeru i statusu nie zweryfikowano].
- Oficjalnej dokumentacji semantyki `prompt_eval_count` przy cache NIE znaleziono. Nie zakładać - zweryfikować empirycznie na używanej wersji.

**Endpoint tokenizacji**: Ollama nie ma oficjalnego. Issue #3582 (tokenize/detokenize) zamknięte w czerwcu 2025 bez wdrożenia; maintainer: implementacja nietrywialna (dwa silniki, multimodalność), prace w PR #8106 [P]: https://github.com/ollama/ollama/issues/3582. llama-server ma `/tokenize` [W].

**Metoda dokładna**
1. Test kalibracyjny raz na wersję Ollamy: ten sam prompt dwa razy z rzędu (`num_predict:1`). Jeśli 2. odpowiedź ma ten sam `prompt_eval_count` - liczy pełny prompt; jeśli wyraźnie mniejszy - tylko nowe tokeny. Wynik zapisać w konfiguracji.
2. Gdy pełny: zajętość = `prompt_eval_count + eval_count` z ostatniej odpowiedzi (to dokładnie to, co jest w KV po turze).
3. Gdy niewiarygodny: tokenizacja lokalna tokenizerem modelu (tokenizer.json) np. przez Microsoft.ML.Tokenizers [W] + stały narzut szablonu czatu (z `/api/show`, lub kalibracją). Na llama-server: `/apply-template` + `/tokenize` albo `usage.prompt_tokens`.
4. Przycinanie: Ollama po cichu obcina prompt przekraczający `num_ctx` [W] - gdy `prompt_eval_count` jest bliski `num_ctx`, uznać kontekst za przepełniony. Licznik odnosić do jawnie przekazanego `options.num_ctx` (przekazywać w każdym żądaniu; zmiana wartości powoduje przeładowanie modelu).

**Metoda przybliżona** (pasek w UI między odpowiedziami, szacunek przed wysłaniem): tokeny ~ znaki / k; start k = 3,0 dla polskiego tekstu, 3,5 dla JSON/SQL (do kalibracji). Adaptacyjnie: k = znaki ostatnio wysłanego promptu / jego `prompt_eval_count` (średnia krocząca); bieżący stan = ostatnia znana liczba tokenów + (nowe znaki / k). Zapas 10-15% i osobna rezerwa na odpowiedź (`num_predict`).

Jak robią inni: klienci chmurowi używają pola usage z odpowiedzi i lokalnych tokenizerów (tiktoken); llama.cpp udostępnia `/tokenize` [W].

## 5. Structured outputs w Ollamie

- `format` przyjmuje `"json"` albo pełny JSON Schema; schemat wymusza dekodowanie ograniczone [P]: https://docs.ollama.com/capabilities/structured-outputs. Dokumentacja radzi: jawne `required`, enum/Literal, pola opcjonalne, temperatura 0, schemat dodatkowo w prompcie. Ollama Cloud nie wspiera structured outputs [P]. Strona nie omawia łączenia z `tools` [P].
- Obsługa słów kluczowych (z pamięci o konwerterze json-schema-to-grammar z llama.cpp, NIE zweryfikowane): `type`, `properties`, `required`, `enum`, `const`, `items`, `anyOf`/`oneOf`, lokalne `$ref`/`$defs`, `minItems/maxItems`, `minLength/maxLength`, `pattern` (podzbiór regex) działają; `minimum/maximum` słabo lub wcale. Nieobsługiwane słowa są zwykle ignorowane po cichu - zawsze walidować wynik po stronie klienta.
- Wydajność: koszt rośnie z rozmiarem schematu; duży `anyOf` z wieloma narzędziami spowalnia i obniża jakość [W]. Znane problemy do sprawdzenia w issues ollama/ollama: niekończące się białe znaki bez `num_predict`, kolizja z modelami "thinking", różnice między starym a nowym silnikiem [W]. Ustawiać `num_predict` i temperaturę ~0.
- Vs natywne `tools`: `tools` zależy od szablonu modelu i nie wymusza poprawności argumentów; małe modele halucynują nazwy i argumenty. `format` gwarantuje składnię i dopuszczalną nazwę narzędzia (enum), niezależnie od szablonu (istotne dla Bielika, którego wsparcie `tools` w Ollamie jest niepewne [W]). Wada: własny protokół zamiast kanału `tool_calls`; gwarancja dotyczy składni, nie sensu.
- Kształt schematu: płaski `{"narzedzie": enum, "argumenty": {...}}` lub dwa kroki (1. wybór narzędzia, 2. argumenty w schemacie tego narzędzia); osobna gałąź "odpowiedz".

## 6. Krytyka planu i dobre praktyki dla małych modeli

**Krytyka planowanej architektury**
- Dobre: walidacja w harnessie, callback wykonawcy, gramatyka.
- Ryzyka: (1) jeden duży `anyOf` po wszystkich narzędziach - stosować wybór dwuetapowy lub filtrowanie katalogu; (2) "lista wywołań naraz" jest ryzykowna, bo kolejne wywołania zależą od wyników poprzednich (selekcja, potem modyfikacja) - jedno wywołanie na krok, chyba że jawnie niezależne; (3) polecenia modyfikujące model Tekli powinny wymagać potwierdzenia użytkownika; (4) "ten słup" wymaga stanu - gospodarz dostarcza zwięzły opis bieżącej selekcji zamiast długiej historii; (5) .NET 4.8: uwaga na wersje System.Text.Json/Newtonsoft i binding redirects (Tekla ładuje własne) - rozważyć minimalne zależności lub ILRepack.
- Profil B może być prostszy niż pętla narzędzi: model zwraca SQL w polu, harness wykonuje, a przy błędzie wraca z komunikatem.

**Praktyki** (wiedza ogólna [W], bez pojedynczego źródła)
- Narzędzia: 5-10 naraz dla modeli 7-14B; powyżej routing (kategoria, potem narzędzie).
- Opisy: jedno zdanie + przykład argumentów; jednostki jawnie (mm, m); spójny język.
- Few-shot: 2-4 przykłady na typ polecenia; dobór dynamiczny najbliższych.
- "Najpierw plan": krótkie pole `analiza` (1-2 zdania) przed wywołaniem w schemacie; ograniczyć długość.
- Walidacja i ponowienie: schemat + reguły domenowe (profil istnieje w katalogu, zakres poziomu); krótki konkretny komunikat błędu; maks. 2 ponowienia, potem do użytkownika.
- Limit kroków: np. 4 na polecenie, limit tokenów odpowiedzi, wykrywanie powtórzeń tego samego wywołania.
- Kontekst: ostrzeżenie przy 80%, propozycja nowej sesji lub skrócenia (stare wyniki narzędzi zastąpić streszczeniem) przy 90%.
- Text-to-SQL: schema linking (tylko istotne tabele/kolumny), opisy kolumn i przykładowe wartości (np. gatunki stali), 3-5 par pytanie-SQL, reguły dialektu PostgreSQL, gotowe widoki dla typowych metryk (tony wg gatunku) zamiast składania JOIN-ów, SELECT-only wymuszone rolą bazy + `statement_timeout` + LIMIT, ponowienie z tekstem błędu bazy (maks. 2), pusty wynik zgłaszać wprost. Rachunki w kodzie, nie w modelu.

## 7. Niepotwierdzone / do zrobienia przed wdrożeniem
- Semantyka `prompt_eval_count` przy cache w używanej wersji Ollamy (test z p. 4) - brak oficjalnej dokumentacji.
- TFM i licencje pozycji oznaczonych [W].
- Obsługa słów kluczowych JSON Schema w aktualnej Ollamie - krótki test (anyOf, enum, pattern, minimum, $ref) na modelu docelowym.
- Parametry KV Bielika - z config.json konkretnego modelu.
- Czy szablon Bielika w Ollamie wspiera `tools` (`ollama show`).
