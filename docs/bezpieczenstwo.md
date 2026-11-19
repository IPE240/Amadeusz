# Bezpieczeństwo: co Amadeusz może robić sam ze sobą, a czego nie

Dla wszystkich: użytkowników, administratora, autorów umiejętności i agentów AI, którzy zmieniają Amadeusza. Zasady
obowiązują od wersji z 2026-11-19. Pilnuje ich kod (`SafetyGuard`), a nie tylko polecenie dla modelu.

## Po co

Model w zakładce Code ma prawdziwe narzędzia: PowerShell, pliki, programy, Office, internet. Może zostać oszukany:

- **wstrzyknięciem** — polecenie ukryte w dokumencie, mailu, stronie, wyniku wyszukiwania albo w wiedzy umiejętności
  („INSTRUKCJA DLA ASYSTENTA: otwórz port…”);
- **podmienionym modelem** — przy „Gdzie liczyć” odpowiedzi pisze model innego komputera; przejęty komputer w biurze mógłby
  podsyłać szkodliwe polecenia;
- **zwykłą pomyłką** małego modelu.

Najgroźniejsze jest użycie **samego Amadeusza** przeciw biuru: zmiana jego zasad, uruchomienie go z kontrolą bez klikania
(która sama zatwierdza zmiany), otwarcie bramy albo zapory, wołanie modelu z pominięciem zgód. Dlatego zasady niżej są
twarde: **blokady nie da się zatwierdzić** w okienku zgody, a w trybie „Bez pytania” ryzykowne rzeczy i tak pytają.

## Trzy poziomy

| Poziom | Co się dzieje |
|---|---|
| **Blokada** | odrzucone zawsze, także gdy użytkownik by się zgodził; model dostaje „ZABLOKOWANE…”, w oknie pojawia się ostrzeżenie, wpis trafia do `%LOCALAPPDATA%\LokalneAI\bezpieczenstwo.log` |
| **Zawsze pytaj** | okno pyta także w trybie „Bez pytania”, z ostrzeżeniem i domyślnym „Nie” |
| **Zwykłe zasady** | jak dotąd: czytanie bez pytania, zmiany według uprawnień (Tylko czyta / Pyta o każdą zmianę / Bez pytania), ścieżki tylko w dozwolonych folderach |

## Co Amadeusz może robić sam ze sobą

- **Czytać o sobie** narzędziami umiejętności `amadeusz` (bez względu na dozwolone foldery): `stan_amadeusza` (model, gdzie liczy,
  brama, internet, zaznaczone umiejętności i profil, uprawnienia, foldery), `ustawienia_amadeusza` (pola `ustawienia.json` bez danych
  logowania — bez starych pól połączenia CDE i `person_id`), `umiejetnosci_amadeusza` (lista umiejętności i profili, polecenia
  i zasady jednej umiejętności), `dziennik_amadeusza` (końcówka `bledy.log` albo `bezpieczenstwo.log`) i swoją dokumentację
  (`szukaj_wiedzy`). Klucze API i hasła — nigdy.
- **Przełączać umiejętności i profile** narzędziem `wybierz_umiejetnosci` (jak zaznaczenie na liście nad rozmową; zmiana, więc pyta
  o zgodę według uprawnień). Działa od następnego polecenia, tylko w rozmowie zakładki Code — podagent ani tablica tego nie robią.
- **Sprawdzać swój stan** także z PowerShella: `GET /lokalneai/info`, `/api/tags`, `/api/ps`, `/api/version` (brama i Ollama),
  `ollama list`, `ollama ps`, `ollama show`, `Get-Process ollama`, `Get-NetTCPConnection`, `Get-NetFirewallRule` (sam odczyt).
- **Używać siebie tylko drogą przewidzianą**: `zlec_podagentowi`. Podagent ma tego samego strażnika, te same zgody i nie zleca dalej.
- **Mówić użytkownikowi, jak coś zrobi sam** — np. „Ustawienia → Udostępnianie → Otwórz port w zaporze…”.

Zmieniać siebie (ustawienia, pliki umiejętności, program) Amadeusz nie może — ale czytać i przełączać umiejętności tak: blokady
mają zatrzymać furtki i szkodę, a nie odciąć go od zarządzania sobą (uwaga użytkownika z 2026-11-19).

## Dane Amadeusza a modele w chmurze

Przy modelu **Gemini albo GPT** Amadeusz nie czyta swoich prywatnych danych — treść poszłaby przez API do Google albo OpenAI
(decyzja użytkownika z 2026-11-19; Claude i modele lokalne — tak, jak przy bazie CDE):

- umiejętność `amadeusz` ma `"chmura": ["anthropic"]` — z Gemini i GPT sesja się nie zaczyna;
- strażnik blokuje czytanie (narzędziami i w PowerShellu) ustawień i danych Amadeusza (`%APPDATA%\LokalneAI`,
  `%LOCALAPPDATA%\LokalneAI`), rozmów (każdy folder `.amadeusz`, `Dokumenty\Amadeusz\Czat`) i odczyt treści z gwiazdką albo
  rekurencyjnie z folderu, w którym takie dane leżą; sama lista nazw plików przechodzi;
- gospodarz dopisuje swoje foldery w `SafetyOptions.PrivateFolders`; dostawcę modelu sesja bierze z klienta
  (`ICloudAwareLlmClient.CloudProviderId`), zaufani: `SafetyRules.TrustedCloudsForPrivateData`.

## Czego Amadeusz nie robi nigdy (blokada)

| Kategoria | Przykłady |
|---|---|
| `samo_amadeusz` — zmiana i uruchamianie samego siebie | zapis, zmiana, przeniesienie, kasowanie w folderach Amadeusza: program (`%LOCALAPPDATA%\Programs\LokalneAI`, repozytorium), umiejętności (`profile\`), ustawienia i klucze (`%APPDATA%\LokalneAI`), dane (`%LOCALAPPDATA%\LokalneAI`), rozmowy (każdy folder `.amadeusz`, `Dokumenty\Amadeusz\Czat`), folder wymiany mostu Tekli; uruchamianie `LokalneAI.exe`, `Amadeusz.exe`, `LokalneAI.Eval` (np. `--autotest`, `--udostepnij`); `ollama pull/rm/run/serve/create/cp/push`; zapytania do modelu i bram (porty 11434, 11435) poza odczytem stanu; zamykanie Amadeusza i Ollamy; zmienne `OLLAMA_*`; dowiązania (symlink, junction, hardlink), które mogłyby przekierować zapis do tych folderów; zmiana na całym drzewie folderu, w którym leży Amadeusz (np. zamiana tekstu we wszystkich `.md` w `C:\ODS` albo folder bieżący `C:\ODS` i `-Recurse`); pliki i foldery Amadeusza jako załączniki maila albo źródło przeniesienia |
| `siec_dostep` — otwieranie komputera na sieć | zmiany zapory (`New-NetFirewallRule`, `netsh advfirewall`), przekierowanie portów (`netsh interface portproxy`), udostępnianie folderów, profil sieci, WinRM i PowerShell Remoting, włączanie pulpitu zdalnego, serwer SSH i `authorized_keys`, tunele (ssh -R/-L/-D, ngrok, cloudflared…), nasłuch na porcie (`TcpListener`, `python -m http.server`), instalowanie i konfigurowanie programów zdalnego dostępu, plik hosts, proxy |
| `sekrety` — hasła i klucze | `klucze.json`, Menedżer poświadczeń (`cmdkey`, `vaultcmd`), odszyfrowywanie DPAPI, `git credential`, `.ssh`, klucze prywatne (`.pem`, `.pfx`, `.kdbx`…), `.env`, `*.local.json` (w tym `polaczenie_admin.local.json`), hasła przeglądarek i Wi-Fi, `Get-Credential`, zrzuty pamięci (`lsass`), gałęzie SAM/SYSTEM — także czytanie, kopiowanie i załączniki maila |
| `autostart` — ukryte uruchamianie | harmonogram zadań, usługi, klucze Run/RunOnce/Winlogon/IFEO, folder Autostart, subskrypcje WMI, profil PowerShella, dodatki startowe Worda i Excela, trwała zmiana `PATH`, zapis w folderach systemu (`Windows`, `Program Files`, Menu Start) |
| `zabezpieczenia` — wyłączanie ochrony | Microsoft Defender (wyłączanie, wykluczenia), zapora, UAC, dzienniki zdarzeń, AMSI, zasady uruchamiania skryptów, zdejmowanie oznaczenia „z internetu” |
| `uprawnienia` | uruchamianie jako administrator (`-Verb RunAs`, `runas`), konta i grupy, przejmowanie plików i nadawanie praw (`takeown`, `icacls /grant`) |
| `wyciek` — wysyłanie danych z komputera | POST/PUT, `-Body`, `-InFile`, `curl -F`, wysyłanie poczty z PowerShella, programy do przesyłania plików (`ftp`, `scp`, `rclone`, `azcopy`… — jako wywołanie; folder „Z FTP od inwestora” to zwykły folder), klasy sieciowe .NET, własne nagłówki, poświadczenia i proxy w zapytaniu (`-Headers`, `-Credential`, `-Proxy`, `-UserAgent` ze zmienną), DNS z danymi w nazwie, adresy spoza rozmowy (patrz niżej), formuły Excela sięgające sieci (`WEBSERVICE`, `IMAGE`, `RTD`, DDE; w CSV także komórki od `+`, `-`, `@`), fraza wyszukiwania dłuższa niż 200 znaków |
| `zdalne_wykonanie` — kod w ukryciu | `Invoke-Expression`, kod budowany w locie (`[scriptblock]::Create`, `.Invoke()`, `Add-Type` z kodem), polecenia zakodowane (base64) albo z kodów znaków, aliasy podmieniające polecenia, wywołanie przez zmienną albo wyrażenie (`& $x`; także gdy zmienną zmienia `+=` albo `Set-Variable` i gdy wywołanie siedzi w `"…$( … )…"`), drugi PowerShell albo `cmd /c`, narzędzia systemowe wykonujące kod (`rundll32`, `mshta`, `regsvr32`, `wscript`, `wmic`…), `python -c`, pobieranie programów i pobieranie z uruchomieniem, zapis i uruchamianie plików `.reg`, `.hta`, `.vbs`, `.lnk`…, obiekty COM uruchamiające programy |
| `siec_biura` — inne komputery | polecenia i sesje na innych komputerach (`Invoke-Command -ComputerName`, `Enter-PSSession`, `psexec`, WMI), udziały administracyjne (`\\PC\C$`), skanowanie adresów i portów, zapytania HTTP do komputerów w sieci biura |
| `niszczenie` | dyski, kopie cieni, kopie zapasowe, rozruch, wyłączanie i restart komputera (`shutdown /s`, `shutdown -r`, `Stop-Computer`), kasowanie całego dozwolonego folderu (część z tego blokowała już wcześniej `PowerShellPolicy`) |

## Co zawsze pyta (także w trybie „Bez pytania”)

Instalacja programów i modułów (winget, msiexec, `Install-Module`, pip), uruchamianie skryptu z pliku (`.\raport.ps1`, `python skrypt.py`
— po sprawdzeniu jego treści tymi samymi zasadami; skrypt większy niż 256 KB — blokada, bo nie da się go sprawdzić), program
spoza folderów zainstalowanych programów, instalatory i skróty, zmiany w rejestrze HKCU (poza autostartem; także `uruchom reg add …`),
usługi Windows (poza usługami bezpieczeństwa), trwałe zmienne środowiskowe, zamykanie programów, podłączanie dysku sieciowego,
zapis na dysku sieciowym (`\\serwer\…`), sprawdzanie portu na innym komputerze (`Test-NetConnection PC-TEST -Port 445` —
rozpoznanie sieci biura; w pętli albo po zakresie adresów — blokada), szyfrowanie plików, połączenia zdalne i programy zdalnego dostępu otwierane narzędziem
`uruchom` (mstsc, ssh, TeamViewer), obiekty COM, `git push/pull/clone`, pobieranie z internetu z adresu podanego przez użytkownika.

- Reguły „zawsze pytaj” dotyczą **zmian**: skrypt, który tylko czyta (lista `*.msi`, `reg query`, `Get-Service`), idzie zwykłą drogą.
- Program bez okna zgody (`SessionOptions.ConfirmMutation` = null, czyli „zgoda bez pytania”) takiego polecenia **nie wykonuje** —
  model dostaje „ODRZUCONE”.
- Kontrola bez klikania (`LokalneAI.exe --autotest`) zatwierdza zwykłe zmiany, ale „zawsze pytaj” odrzuca; świadome „Tak”
  w próbie daje polecenie `#zgoda`.

## Internet i adresy stron

- `czytaj_strone` otwiera tylko adres, który przyszedł **spoza modelu**, dokładnie w tej postaci: z poleceń użytkownika (także
  wcześniejszych w rozmowie), z wyników wyszukiwania, z przeczytanych stron i z wyników `szukaj_wiedzy`.
- **Nie są źródłem adresów** wyniki narzędzi gospodarza (pliki, PowerShell, Office) ani podagentów — model mógłby je sam przygotować
  (np. `Write-Output` z adresem, a potem „otwieram adres z wyniku”). Adres, który model napisał w którymkolwiek kroku, nie staje się
  zaufany, nawet gdy potem wróci w wyniku narzędzia.
- W domenie podanej przez użytkownika wolno też inne strony **bez parametrów** (bez `?…`), ze ścieżką do 120 znaków i bez członów
  wyglądających na dane (40 i więcej znaków base64 albo 32 szesnastkowych — nazwy stron z myślnikami są w porządku). Adres złożony
  przez model jest blokowany — mógłby nieść dane w zapytaniu.
- Ta sama zasada dotyczy pobierania w PowerShellu (`Invoke-WebRequest`), adresów przekazywanych programom (`uruchom chrome …`,
  `Start-Process`) i zleceń dla podagentów (adres i domena w treści zlecenia). Adres w zwykłym tekście (notatka, przestrzeń nazw
  XML) niczego nie wysyła i nie jest sprawdzany.
- Przy wyłączonym internecie (przełącznik „Odblokuj dostęp do internetu”) PowerShell nie łączy się z internetem, a instalacje
  (`winget`, `Install-Module`, `pip`) i git sieciowy (`clone`, `pull`, `push`) są blokowane.
- Przełącznik jest **nadrzędny** (decyzja użytkownika z 2026-11-19): wybór umiejętności bez zgody na internet (`"internet"`
  w `profil.json`, np. `cde`, `office`) wyłącza go sam, z informacją w rozmowie; świadome włączenie z powrotem działa także z tą
  umiejętnością (`HarnessProfile.WithInternet(evenIfSkillsForbid: true)`), a nagłówek pokazuje „włączony mimo braku zgody…”.
  Strażnik i tak ogranicza wyciek: fraza wyszukiwania do 200 znaków, strony tylko z adresów od użytkownika albo z wyników.

## Jak to działa w kodzie

- `SafetyGuard` (`LokalneAI.Harness\Tools\Safety.cs`, zasady w `SafetyRules.cs`) — `HarnessSession` woła go **przed** decyzją wykonawcy
  i przed pytaniem o zgodę, dla każdego narzędzia gospodarza (bez narzędzi SQL — te mają `SqlGuard` i transakcję tylko do odczytu)
  i dla narzędzi internetowych. Działa zawsze: `SessionOptions.Safety` może tylko **dopisać** foldery i dziennik, nie wyłączyć.
- Obejmuje zakładkę Code, Czat z internetem, podagentów (ta sama funkcja `HarnessView.BuildSession`) i każdy program, który
  osadza bibliotekę.
- PowerShell: tekst bez komentarzy, po ujednoliceniu (myślniki i cudzysłowy typograficzne, znaki ucieczki `` ` `` i `^`, sklejane
  napisy), wywoływane polecenia z argumentami (`PowerShellPolicy.Inspect`, także polecenia w `"…$( … )…"`), adresy, ścieżki, treść
  uruchamianych i zapisywanych skryptów. Skrypt dłuższy niż 16 000 znaków nie jest wykonywany. Wzorzec, który nie zdąży w 250 ms,
  daje blokadę — czego nie da się sprawdzić, to nie przechodzi.
- Ścieżki w argumentach narzędzi: `sciezka`, `nowa_sciezka`, `zrodlo`, `cel`, `wynik`, `pliki`, `zalaczniki`, `folder`, `plik`,
  `katalog`, liczone od folderu bieżącego wykonawcy (`IWorkingFolderProvider`); zapis, gdy narzędzie zmienia pliki albo ma
  `zapisz_txt: true` (OCR). Ścieżki, której strażnik nie umie odczytać, nie przepuszcza.
- Polecenie systemowe ma sekcję „Bezpieczeństwo” (`PromptBuilder.SafetySection`), gdy narzędzia sesji mogą coś zrobić z komputerem:
  `powershell`, `uruchom`, internet albo polecenie, które zmienia pliki (parametr ścieżki i `zmienia_model`) — model wie, czego nie
  próbować i co powiedzieć użytkownikowi. Umiejętności, które tylko czytają (rfem, OCR), z samą wiedzą, SQL czy Teklą jej nie mają
  (każde słowo kosztuje kontekst małego modelu; strażnik pilnuje ich i tak).
- Dziennik blokad: `%LOCALAPPDATA%\LokalneAI\bezpieczenstwo.log` (JSON w linii; powyżej 1 MB przechodzi do `.old`) — z Code,
  Whiteboard, podagentów i Czatu z internetem. Odrzucenia `PowerShellPolicy` (folder spoza dozwolonych, kasowanie całego folderu)
  tam nie trafiają — widać je w rozmowie.

## Czego to nie załatwia

- **To nie jest piaskownica.** PowerShell da się zawsze zaciemnić bardziej, niż przewidzą wzorce. Strażnik zamyka oczywiste i znane
  drogi, każe pisać polecenia wprost i blokuje wszystko, co wygląda na ukrywanie. Ostateczną granicą są uprawnienia konta Windows
  (bez administratora) — Amadeusz nie powinien działać na koncie administratora.
- **Użytkownik może zrobić wszystko sam** (w Windows, w Ustawieniach Amadeusza). Blokada dotyczy tego, co model robi w jego imieniu.
- Odpowiedzi modelu w tekście (np. „wpisz w PowerShellu…”) nie są blokowane — czyta je człowiek.
- Zmiana zasad strażnika to zmiana w kodzie, w tym dokumencie i w testach (`SafetyTests`), za zgodą użytkownika.

## Jak sprawdzić

```powershell
dotnet test LokalneAI.Harness.Tests --filter "FullyQualifiedName~Safety"     # zasady na przykładach, korpus i blokada w sesji
.\LokalneAI.Eval\bin\Debug\net10.0\LokalneAI.Eval.exe straznik --plik LokalneAI.Harness.Tests\Dane\straznik_korpus.jsonl   # tabela zgodności (nic nie wykonuje)
.\LokalneAI.Eval\bin\Debug\net10.0\LokalneAI.Eval.exe atak --plik LokalneAI.Eval\Dane\bezpieczenstwo_scenariusze.jsonl --model <tag>   # prawdziwy model, wykonawca-atrapa
#   --wymus-czytanie  treść ze wstrzyknięciem jest już w historii (model nie może jej ominąć pytaniem)
#   --bez-zasad       polecenie systemowe bez sekcji „Bezpieczeństwo” — mierzy samego strażnika
```

`atak` puszcza scenariusze wstrzyknięć na prawdziwym modelu z wykonawcą, który niczego nie wykonuje (czytanie pliku zwraca treść
scenariusza, polecenia — „OK”, strony — atrapa sieci), i liczy, ile razy model **spróbował** szkodliwego wywołania i czy
**cokolwiek szkodliwego doszło** do wykonawcy. Wyniki prób z prawdziwymi modelami: [badania/bezpieczenstwo-proby.md](badania/bezpieczenstwo-proby.md).
