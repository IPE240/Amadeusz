# Serwer modelu w biurze

Model może liczyć na mocnym komputerze w biurze, a laptopy wysyłają tylko tekst. Na laptopie działa wtedy
program z biblioteką `LokalneAI.Harness` (lekki), a cała praca karty graficznej odbywa się na serwerze.

**Najprościej i najbezpieczniej: brama okna LokalneAI** ([siec.md](siec.md)). Na mocnym komputerze zaznacza się
w oknie „Pozwalaj na przeliczanie zapytań z innych komputerów”, a na laptopie wybiera ten komputer
w „Gdzie liczyć”. Ollama zostaje wtedy na `127.0.0.1`. Brama przepuszcza tylko liczenie, tylko z podsieci, pokazuje
kolejkę i daje pierwszeństwo właścicielowi. Opis niżej (Ollama otwarta w sieci) dotyczy serwera bez okna albo
innych programów niż LokalneAI.

## Ollama na serwerze (najprościej)

1. Instalacja (PowerShell, bez praw administratora):
   ```powershell
   irm https://ollama.com/install.ps1 | iex
   ollama pull SpeakLeash/bielik-11b-v3.0-instruct:Q4_K_M
   ```
2. Zmienne środowiskowe użytkownika, pod którym działa Ollama (potem restart Ollamy):

   | Zmienna | Wartość | Po co |
   |---|---|---|
   | `OLLAMA_HOST` | `0.0.0.0:11434` | nasłuch w sieci (domyślnie tylko ten komputer) |
   | `OLLAMA_NUM_PARALLEL` | `2`–`4` | ile pytań naraz; każde to osobny kontekst w pamięci karty |
   | `OLLAMA_KEEP_ALIVE` | `30m` | jak długo model zostaje w pamięci po ostatnim pytaniu |
   | `OLLAMA_FLASH_ATTENTION` | `1` | mniej pamięci na kontekst |
   | `OLLAMA_KV_CACHE_TYPE` | `q8_0` | kontekst zajmuje ok. połowę mniej pamięci (wymaga flash attention) |

   ```powershell
   [Environment]::SetEnvironmentVariable("OLLAMA_HOST", "0.0.0.0:11434", "User")
   [Environment]::SetEnvironmentVariable("OLLAMA_NUM_PARALLEL", "2", "User")
   [Environment]::SetEnvironmentVariable("OLLAMA_KEEP_ALIVE", "30m", "User")
   ```
3. Zapora Windows (jako administrator), tylko sieć biura:
   ```powershell
   New-NetFirewallRule -DisplayName "Ollama LAN" -Direction Inbound -Protocol TCP -LocalPort 11434 -RemoteAddress 10.0.0.0/24 -Action Allow -Profile Private,Domain
   ```
4. W programach na laptopach: `OllamaOptions.Url = "http://<nazwa-lub-IP-serwera>:11434"`.
   W oknie testowym jest to pole „Adres Ollamy” w Ustawieniach.

**Bezpieczeństwo.** Ollama nie ma haseł. Każdy, kto dostanie się do portu, może pytać model i pobierać albo usuwać
modele przez API. Dlatego:
- otwieraj port tylko na podsieć biura (reguła wyżej), nigdy na internet;
- przy większej liczbie osób postaw przed Ollamą reverse proxy z kluczem (np. Caddy albo nginx z nagłówkiem
  `Authorization`) i używaj `OpenAiCompatibleLlmClient` z `ApiKey`.

## Ile pamięci karty

Pamięć to wagi modelu plus kontekst każdej równoległej rozmowy:

- Bielik 11B Q4_K_M: wagi ok. 6,7 GB.
- Kontekst 16 384 tokenów: ok. 3,2 GB przy zapisie f16 albo ok. 1,6 GB przy `q8_0`.

| Karta | Co się mieści (Bielik 11B Q4_K_M, kontekst 16k, q8_0) |
|---|---|
| 8 GB (RTX 4060) | 1 rozmowa, część modelu na procesorze (zmierzone: 60% na karcie, wolniej) |
| 16 GB | 1–2 rozmowy w całości na karcie |
| 24 GB (RTX 3090/4090) | ok. 4 rozmowy równolegle albo większy model |

Wartości dla 16 i 24 GB to szacunek z rozmiaru wag i kontekstu, a nie pomiar. Sprawdź je na miejscu poleceniem `ollama ps`:
kolumna PROCESSOR powinna pokazywać `100% GPU`.

## Inne serwery

- **llama.cpp `llama-server`**: sloty na równoległe rozmowy, endpoint `/tokenize` (dokładne liczenie tokenów)
  i API zgodne z OpenAI. Klient: `OpenAiCompatibleLlmClient`.
- **LM Studio / Bionic**: serwer API (w Bionicu port 1235, domyślnie wyłączony), też zgodny z OpenAI.
- **vLLM**: najszybszy przy wielu użytkownikach, ale wymaga Linuksa (albo WSL).

Szczegóły i źródła: [badania/harness.md](badania/harness.md).
