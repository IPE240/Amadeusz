# Porty Amadeusza: 11434, 11435, 11436 — który do czego

- **11434 TCP — Ollama** (modele). Słucha tylko na `127.0.0.1`, z sieci niewidoczna. Adres w Amadeuszu: Ustawienia → Czat → adres Ollamy.
- **11435 TCP — brama Amadeusza** (port bramy). Jedno wejście do modelu dla czatu, zakładki Code, innych programów i innych komputerów. API Ollamy i API OpenAI (`/v1`). Bez udostępniania słucha tylko na `127.0.0.1`; z udostępnianiem na wszystkich kartach, ale wpuszcza tylko podsieć tego PC.
- **11436 UDP — wyszukiwanie komputerów** („Szukaj w sieci”). Działa tylko, gdy komputer udostępnia.

Port bramy zmienia się w Ustawienia → Udostępnianie → port, tylko gdy 11435 jest zajęty — wtedy na wszystkich komputerach albo przy łączeniu trzeba podać `NAZWA-PC:port`. Port wyszukiwania jest stały.

# Zapora Windows: reguły, otwarcie portu, sieć prywatna

Przycisk **Ustawienia → Udostępnianie → Otwórz port w zaporze…** (raz, okno administratora UAC) zakłada dwie reguły przychodzące: `LokalneAI - brama (TCP 11435)` i `LokalneAI - brama (UDP 11436, wyszukiwanie)`. Obie mają `RemoteAddress LocalSubnet` i działają tylko w sieci prywatnej i domenowej — w sieci publicznej nie. Po zmianie portu bramy trzeba kliknąć przycisk ponownie.

Sprawdzenie w PowerShellu:
```powershell
Get-NetFirewallRule -DisplayName 'LokalneAI - brama*' | Select-Object DisplayName, Enabled, Profile
Get-NetConnectionProfile    # NetworkCategory: Private albo DomainAuthenticated
```

# Czy port słucha: sprawdzenie w PowerShellu, 127.0.0.1 czy cała sieć

```powershell
Get-NetTCPConnection -State Listen -LocalPort 11434,11435 | Select-Object LocalAddress, LocalPort
Invoke-RestMethod http://127.0.0.1:11435/lokalneai/info
```
`LocalAddress` 127.0.0.1 przy 11435 = udostępnianie wyłączone; `::` albo 0.0.0.0 = włączone (z sieci tylko podsieć). Ollama zawsze powinna mieć 127.0.0.1:11434.

# Porty w próbach i kontroli bez klikania

Kontrola `LokalneAI.exe --autotest …` uruchamia własną bramę na dowolnym wolnym porcie (port 0), żeby nie zająć 11435 otwartego okna; `--port <n>` wymusza numer. W przykładach dokumentacji druga brama „innego komputera” na tym samym PC ma port 11445 i `--udostepnij` (tylko 127.0.0.1, bez otwierania portu w sieci). Testy wyszukiwania używają UDP 41436.

# Połączenia wychodzące: chmura, internet, baza CDE, GitHub

Amadeusz sam łączy się z: Ollamą tego PC (127.0.0.1:11434); bramą innego komputera (NAZWA-PC:11435) przy „Gdzie liczyć” i podagentach; rozgłoszeniem UDP 11436 przy „Szukaj w sieci”; HTTPS 443 do api.anthropic.com, api.openai.com, generativelanguage.googleapis.com — tylko modele w chmurze z kluczem i w umiejętnościach, które pozwalają; lite.duckduckgo.com i stronami — tylko z przełącznikiem „Odblokuj dostęp do internetu”; PostgreSQL bazy CDE (zwykle 5432, adres z Menedżera poświadczeń, tylko odczyt); github.com — aktualizacja wiedzy i dane OCR; ollama.com — tylko instalator i `ollama pull`.

# Bez portów: Tekla, Office, RFEM, OCR, drugie uruchomienie

Most TeklaBridge działa przez folder `exchange` na dysku. Word, Excel, Outlook, PowerPoint i RFEM 5 — przez COM na tym komputerze. OCR — biblioteki lokalne (Tesseract, PDFium). Drugie uruchomienie skrótu pokazuje okno działającej kopii przez mutex Windows, bez portu.

# Serwer modelu w biurze: Ollama na 0.0.0.0:11434 a brama Amadeusza

Sama Ollama na serwerze może słuchać w sieci (`OLLAMA_HOST=0.0.0.0:11434`), a komputery łączą się z `http://serwer-ai:11434` (instalator: `instaluj.bat -SerwerModelu http://serwer-ai:11434`). Ollama nie ma haseł ani blokad: każdy w sieci może pobierać i usuwać modele. Bezpieczniej udostępniać przez bramę Amadeusza (port 11435): blokuje pobieranie i usuwanie, ma kolejkę z pierwszeństwem właściciela i limit kontekstu dla innych.
