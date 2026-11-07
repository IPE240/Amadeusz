# Co zwykle mają aplikacje z lokalnymi modelami i harnessy (badanie 2026-11-07)

Cel: sprawdzić, czego użytkownicy oczekują od okna z lokalnym modelem i od harnessu (agenta wykonującego polecenia),
oraz ułożyć kolejność prac dla LokalneAI.

Oznaczenia: **[P]** = potwierdzone w źródle w tej sesji (link w tabeli źródeł), **[W]** = wiedza ogólna,
niesprawdzona teraz.

## 1. Aplikacje z lokalnymi modelami (LM Studio, Ollama, Jan, Open WebUI, AnythingLLM, Msty)

| Funkcja | Gdzie jest | LokalneAI | Priorytet |
|---|---|---|---|
| czat ze strumieniem, Stop, statystyki szybkości | wszystkie [W] | jest (także „gdzie liczył”: % w VRAM) | — |
| pobieranie modeli z listy, rozmiar, „czy się zmieści” | LM Studio, Ollama, Jan [W] | pobieranie modelu domyślnego, lista pobranych | średni: usuwanie modelu i ocena „zmieści się w karcie” |
| wiele rozmów (lista z boku), wyszukiwanie, eksport | wszystkie [W] | jedna rozmowa; historia w `historia.jsonl` bez przeglądarki | **wysoki** |
| ponów odpowiedź, popraw pytanie | wszystkie [W] | brak | średni |
| Markdown: tabele, kod z „Kopiuj” | wszystkie [W] | surowy tekst | średni |
| dokumenty w czacie (przeciągnij PDF/DOCX, pytaj o treść) | AnythingLLM (wbudowany embedder i baza wektorowa), Open WebUI, Jan [P – źródło wtórne] | wiedza profili (BM25) tylko w zakładce Code | **wysoki** |
| serwer API dla innych programów | LM Studio, Jan (zgodny z OpenAI), Ollama [P] | brama: API Ollamy i `/v1/chat/completions` | zrobione |
| udostępnienie w sieci lokalnej | LM Studio: „Serve on Local Network”, opcjonalnie „Require Authentication” (token w `Authorization`) [P] | brama: przełącznik, tylko podsieć, bez hasła | niski: „kod biura” |
| modele innego komputera „jak lokalne” | LM Studio **LM Link**: VPN Tailscale, programy dalej pytają `localhost:1234`, wybór „preferowanego urządzenia”, połączone komputery nie widzą plików ani systemu [P] | „Gdzie liczyć” + „Szukaj w sieci” (tylko podsieć, bez VPN) | zrobione |
| ładowanie na żądanie i zwalnianie pamięci | LM Studio: „Just in Time Model Loading”, „Auto Unload Unused JIT Models” [P]; Ollama: `keep_alive` 5 min [P] | `keep_alive`, „Zwolnij pamięć GPU” | niski: „zwolnij, gdy startuje Tekla” |
| kolejka i równoległość | Ollama: `OLLAMA_NUM_PARALLEL` (domyślnie 1), `OLLAMA_MAX_QUEUE` (512, potem 503), „queued requests will be processed in order” [P] | kolejka bramy z pierwszeństwem tego PC i podglądem | zrobione |
| ikona w zasobniku, start z Windows | LM Studio, Ollama [W] | brak | **wysoki** dla komputerów, które udostępniają |
| narzędzia MCP | LM Studio, Jan, AnythingLLM [P – źródło wtórne] | własny katalog narzędzi w profilach | niski (MCP SDK nie działa na .NET 4.8) |
| konta i role | Open WebUI [W] | każdy ma swoje okno | niski |
| obraz, głos | część [W] | brak | niski |

## 2. Harnessy, czyli agenci wykonujący polecenia (Claude Code, Codex CLI, OpenCode, Cline)

| Funkcja | Gdzie jest | LokalneAI (zakładka Code) | Priorytet |
|---|---|---|---|
| tryby uprawnień | Claude Code: `default` (Manual), `acceptEdits`, `plan`, `auto` (klasyfikator), `dontAsk`, `bypassPermissions` [P]; Codex: piaskownica `read-only` / `workspace-write` / `danger-full-access` plus zasady zatwierdzania [P – źródło wtórne] | **Tylko czyta / Pyta o każdą zmianę / Bez pytania (poza kasowaniem)**; „bez pytania” nie jest zapisywane | zrobione |
| reguły allow/ask/deny i „nie pytaj więcej” | Claude Code: zgoda na polecenie zapamiętana dla repozytorium albo do końca sesji [P]; OpenCode: wygrywa ostatnia pasująca reguła, domyślnie „ask” [P – źródło wtórne] | `PowerShellPolicy`: czyta / pyta / odrzuca; bez zapamiętywania | średni: „Tak, i nie pytaj o to w tej sesji” |
| zakres folderów | Claude Code: katalog roboczy + dodatkowe [P] | „Dozwolone foldery” | zrobione |
| piaskownica systemowa | Codex domyślnie (Seatbelt, bubblewrap, piaskownica Windows); Claude Code opcjonalnie [P – źródło wtórne] | brak; granicą jest zgoda i konto Windows | średni |
| punkty przywracania, „cofnij” | Claude Code (checkpointy, rewind) [W] | brak | średni: dziennik zmian plików i cofanie przeniesień i zmian nazw |
| licznik i skracanie kontekstu | wszystkie [W] | licznik; skracanie przy 85% | zrobione |
| plan przed wykonaniem | Claude Code `plan` [P] | brak | niski (mały model) |
| hooki przed i po narzędziu | Claude Code (najwięcej typów), Codex [P – źródło wtórne] | `IConfirmingExecutor` w kodzie gospodarza | niski |
| plik instrukcji projektu | `AGENTS.md`, `CLAUDE.md` [W] | `system.md`, `karta.md`, `wiedza\` profilu | zrobione |
| zapis sesji, wznowienie, dziennik działań | wszystkie [W] | panel; brak zapisu sesji Code | średni: dziennik wywołań (co, kiedy, wynik, kto zatwierdził) |
| tryb bez okna | wszystkie [W] | `--autotest`, `LokalneAI.Eval` | zrobione |

## 3. Udostępnianie modelu w sieci: jak robią to inni

- **Ollama** domyślnie słucha na `127.0.0.1:11434`. `OLLAMA_HOST=0.0.0.0` otwiera ją dla całej sieci [P].
  API Ollamy nie ma uwierzytelniania [P]. Wystawione instancje są masowo skanowane: LeakIX liczy ok. 12 tys.
  w internecie, CSA ponad 175 tys. [P]. CVE-2026-7482 („Bleeding Llama”, wyciek pamięci bez logowania,
  poprawione w 0.17.1) i CVE-2024-37032 („Probllama”, RCE) dotyczą właśnie wystawionych instancji [P].
  Zalecenie z tych źródeł: Ollama na localhost, przed nią brama albo proxy z uwierzytelnianiem, zapora.
- **LM Studio**: „Serve on Local Network” (otwarty port) z opcjonalnym tokenem [P] albo LM Link
  (Tailscale, szyfrowane, odkrywanie przez ich serwer, „nie da się użyć z istniejącą siecią Tailscale”) [P].
- **Proxy przed wieloma Ollamami**:
  - Olla: priorytety i przełączanie przy awarii;
  - ollama_proxy_server (ParisNeo): najmniej zajęty serwer i token;
  - HOPS: losowy wybór;
  - nginx i HAProxy.

  [P – źródło wtórne]. Te narzędzia rozdzielają zapytania między serwery. Żadne nie daje pierwszeństwa właścicielowi
  karty ani przełącznika „udostępniaj” w oknie użytkownika.

**Wniosek dla LokalneAI:**

- Brama w oknie, przed Ollamą na `127.0.0.1`, przepuszcza tylko liczenie i tylko z podsieci. To jest
  odpowiednik „Serve on Local Network” z LM Studio z dodatkiem kolejki z pierwszeństwem.
- SSH odpada: konta, klucze i administrator na każdym PC, pełna powłoka zamiast liczenia, a kolejki i tak brak.
- VPN (jak LM Link) jest zbędny w jednej podsieci biura. Wróci do tematu, gdy ktoś będzie chciał liczyć z domu.

Szczegóły rozwiązania: [../siec.md](../siec.md).

## 4. Kolejność dalszych prac (propozycja)

1. **Komputery udostępniające:** ikona w zasobniku (zamknięcie okna nie zamyka bramy), start z Windows, tryb
   `--serwer` bez okna dla komputera tylko do liczenia.
2. **Czat:**
   - lista rozmów, wyszukiwanie i eksport;
   - Markdown z „Kopiuj”;
   - „Ponów” i „Popraw pytanie”.
3. **Dokumenty w czacie:** przeciągnij plik, a wyszukiwarka `KnowledgeIndex` (już jest w bibliotece) dostarczy
   modelowi fragmenty.
4. **Code:**
   - dziennik wywołań (audyt);
   - „Tak, i nie pytaj o to w tej sesji”;
   - cofanie przeniesień i zmian nazw.
5. **Modele:** usuwanie, rozmiar, ocena „zmieści się w karcie” (wagi + kontekst, wzór w [harness.md](harness.md)).
6. **Kod biura** (opcjonalne hasło bramy), jeśli udostępnianie wyjdzie poza zaufaną podsieć.

## Źródła

| Źródło | Co potwierdza |
|---|---|
| [Ollama FAQ](https://docs.ollama.com/faq) | `127.0.0.1:11434`, `OLLAMA_HOST`, `OLLAMA_NUM_PARALLEL` = 1, `OLLAMA_MAX_QUEUE` = 512 i 503, kolejność obsługi, `keep_alive` 5 min, kontekst domyślny 4096 |
| [LM Studio: ustawienia serwera](https://lmstudio.ai/docs/developer/core/server/settings) | Serve on Local Network, Require Authentication, JIT, Auto Unload |
| [LM Studio: LM Link FAQ](https://lmstudio.ai/docs/lmlink/basics/faq) | Tailscale, `localhost:1234` jak dotąd, preferowane urządzenie, brak dostępu do plików |
| [LM Link](https://lmstudio.ai/link) | opis produktu |
| [Claude Code: uprawnienia](https://code.claude.com/docs/en/permissions) | tryby uprawnień, reguły, zakres folderów, piaskownica |
| [LeakIX: 12 000 wystawionych Ollam](https://blog.leakix.net/2026/02/ollama-exposed/) | skala wystawionych instancji, brak uwierzytelniania |
| [CSA: Exposed by Default](https://labs.cloudsecurityalliance.org/research-rb/csa-research-note-self-hosted-ai-infrastructure-exposure-202/) | 175 tys. instancji, Probllama |
| [Indusface: CVE-2026-7482](https://www.indusface.com/blog/cve-2026-7482-bleeding-llama-vulnerability/) | Bleeding Llama, poprawka 0.17.1 |
| [Porównanie harnessów (Haseeb Qureshi)](https://gist.github.com/Haseeb-Qureshi/2213cc0487ea71d62572a645d7582518), [computingforgeeks](https://computingforgeeks.com/opencode-vs-claude-code-vs-cursor/) | źródła wtórne: piaskownice Codex i Claude Code, reguły OpenCode, hooki |
| [Olla](https://thushan.github.io/olla/usage), [ollama_proxy_server](https://www.sourcepulse.org/projects/1843846) | źródła wtórne: proxy przed wieloma Ollamami |
| [promptquorum: alternatywy](https://www.promptquorum.com/power-local-llm/local-llm-app-alternatives) | źródło wtórne: Jan, AnythingLLM, Open WebUI, Msty |
