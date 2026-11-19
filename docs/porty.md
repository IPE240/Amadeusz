# Porty i połączenia sieciowe Amadeusza

Dla administratorów, agentów AI i każdego, kto otwiera zaporę albo podpina inne programy. Jak sterować programem
i podpinać narzędzia: [sterowanie.md](sterowanie.md). Brama, kolejka i udostępnianie: [siec.md](siec.md).

## Porty, na których coś słucha (przychodzące)

| Port | Protokół | Kto słucha | Adres | Do czego | Gdzie się zmienia |
|---|---|---|---|---|---|
| **11434** | TCP (HTTP) | Ollama (osobny program) | `127.0.0.1` — tylko ten komputer | model językowy: API Ollamy i zgodne z OpenAI (`/v1/…`) | zmienna `OLLAMA_HOST` Ollamy; adres w Amadeuszu: Ustawienia → Czat → adres Ollamy (`ollamaUrl`) |
| **11435** | TCP (HTTP) | brama Amadeusza (`LokalneAI.Gateway`, Kestrel) | `127.0.0.1`; z włączonym udostępnianiem wszystkie karty sieciowe, ale wpuszcza tylko adresy z podsieci tego PC | jedno wejście do modelu dla czatu, zakładki Code, innych programów na tym PC i innych komputerów; kolejka, liczniki tokenów | Ustawienia → Udostępnianie → port (`gatewayPort`); ten sam na wszystkich komputerach biura |
| **11436** | UDP | brama Amadeusza (wyszukiwanie) | wszystkie karty, tylko gdy udostępnianie jest włączone; odpowiada tylko podsieci | „Szukaj w sieci”: pytanie `LOKALNEAI?1` rozgłoszeniem, odpowiedź z opisem bramy | stały (`GatewayOptions.DefaultDiscoveryPort`) |

- **Udostępnianie wyłączone** (domyślnie): z sieci nie widać żadnego portu Amadeusza ani Ollamy.
- **Udostępnianie włączone**: z podsieci widać tylko 11435 (TCP) i 11436 (UDP). Ollama zostaje na `127.0.0.1:11434`.
- **Zapora Windows:** przycisk *Ustawienia → Udostępnianie → Otwórz port w zaporze…* (raz, z UAC) zakłada dwie reguły
  przychodzące `LokalneAI - brama (TCP 11435)` i `LokalneAI - brama (UDP 11436, wyszukiwanie)`. Obie mają
  `RemoteAddress LocalSubnet` i profile Domain i Private, czyli w sieci publicznej (kawiarnia, hotel) nie działają.
- **Zmiana portu bramy:** tylko gdy 11435 jest zajęty. Wtedy trzeba ją zrobić na wszystkich komputerach albo przy łączeniu
  podawać `NAZWA-PC:port`. Po zmianie trzeba ponownie kliknąć „Otwórz port w zaporze…”. Port wyszukiwania się nie zmienia.
- **Serwer modelu w biurze** (sama Ollama bez Amadeusza, [serwer-modelu.md](serwer-modelu.md)): Ollama słucha tam na
  `0.0.0.0:11434` (`OLLAMA_HOST`), a komputery łączą się z `http://serwer-ai:11434`. Brama Amadeusza jest bezpieczniejsza
  (blokuje pobieranie i usuwanie modeli, ma kolejkę), dlatego zalecane jest udostępnianie przez Amadeusza.

### Porty w próbach i testach

| Port | Kto | Uwagi |
|---|---|---|
| dowolny wolny (`0`) | brama w kontroli bez klikania (`LokalneAI.exe --autotest …`) | nie zajmuje 11435 otwartego okna; `--port <n>` wymusza numer |
| `11445` | przykłady w dokumentacji: druga brama „innego komputera” na tym samym PC | tylko `127.0.0.1` (`--udostepnij`) |
| `41436` (UDP) | `GatewayTests` — wyszukiwanie | tylko w testach |

## Połączenia wychodzące (Amadeusz łączy się z…)

| Dokąd | Port | Kiedy |
|---|---|---|
| Ollama tego PC `127.0.0.1:11434` | TCP | zawsze, gdy liczy ten komputer |
| brama innego komputera `NAZWA-PC:11435` | TCP | *Gdzie liczyć → Na innym komputerze* i komputery dla podagentów |
| rozgłoszenie w podsieci, port 11436 | UDP | przycisk „Szukaj w sieci” |
| `api.anthropic.com`, `api.openai.com`, `generativelanguage.googleapis.com` | 443 (HTTPS) | tylko modele w chmurze dodane w Ustawieniach, z kluczem API, w umiejętnościach, które pozwalają (`chmura`) |
| `lite.duckduckgo.com` i czytane strony | 443/80 | tylko z przełącznikiem „Odblokuj dostęp do internetu” i w umiejętnościach z `"internet": true`; adresy prywatne są zablokowane |
| PostgreSQL bazy CDE | zwykle 5432 (adres i port z Menedżera poświadczeń) | umiejętność `cde`, tylko odczyt |
| `github.com` (git) | 443 | aktualizacja wiedzy z `zrodla.json` (Ustawienia → Umiejętności → Aktualizuj wiedzę) |
| `github.com` (dane OCR tessdata_fast) | 443 | pierwsze użycie OCR albo przycisk w Ustawieniach → OCR |
| `ollama.com` | 443 | tylko instalator i `ollama pull` (pobieranie modeli); brama Amadeusza pobierania nie przepuszcza |

## Bez portów

- **Most TeklaBridge** (umiejętność `tekla`) — folder `exchange` na dysku, bez sieci.
- **Office i RFEM 5** (umiejętności `office`, `rfem`) — COM na tym komputerze.
- **OCR** — biblioteki na tym komputerze (Tesseract, PDFium).
- **Drugie uruchomienie programu** — pokazuje okno działającej kopii przez nazwany mutex i zdarzenie Windows (bez portu).

## Jak sprawdzić, co słucha

```powershell
Get-NetTCPConnection -State Listen -LocalPort 11434,11435 | Select-Object LocalAddress, LocalPort, OwningProcess
Get-NetUDPEndpoint -LocalPort 11436 -ErrorAction SilentlyContinue
Invoke-RestMethod http://127.0.0.1:11435/lokalneai/info     # opis bramy: komputer, karta, kolejka, limit kontekstu
Get-NetFirewallRule -DisplayName 'LokalneAI - brama*' | Select-Object DisplayName, Enabled, Profile
```

`LocalAddress` `127.0.0.1` przy 11435 oznacza, że udostępnianie jest wyłączone, a `0.0.0.0` albo `::`, że jest włączone.
