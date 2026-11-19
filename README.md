# Amadeusz
ODS Harnes for local AI agents. Works without intenet connection on your own RTX 4060 8GB or better.

Roboczo program i biblioteki nazywają się **LokalneAI** (projekty `LokalneAI.*`, program `LokalneAI.exe`).

Lokalne modele AI (Bielik i inne, w [Ollamie](https://ollama.com)) w programach biura ODS. Nic nie wychodzi
do internetu: model liczy na karcie graficznej tego komputera albo serwera w biurze.

Są dwie części:

1. **Pół-harness** (`LokalneAI.Harness*`). Biblioteka do osadzenia w innych programach. Użytkownik pisze po polsku,
   model wybiera polecenia z katalogu (profil), biblioteka je sprawdza, a program-gospodarz wykonuje. Gotowe profile:
   - **tekla**: „wstaw słup HEB300 w 6000, 0 od zera do 3,53”, „zmień go na HEB320”, „wyselekcjonuj strop na poziomie 3,53”.
     Polecenia idą przez most TeklaBridge do Tekli 2019 albo do symulatora.
   - **cde**: „Ile przepracowałem godzin na projekcie WSP?”, „Ile ton stali S355 ma projekt 0546?”. Model pisze SQL
     tylko do odczytu, a sumy liczy baza.
   - **pliki**: „cd C:\ODS\Projekty, a potem utwórz folder Raporty”, „co jest w tym folderze?”. Umie tworzyć foldery,
     pokazywać zawartość, kopiować, przenosić i otwierać w Eksploratorze, tylko w dozwolonych folderach i bez kasowania.
   - **komputer**: to co `pliki`, a do tego polecenia PowerShella, uruchamianie programów, czytanie i wyszukiwanie
     plików: „ile jest PDF-ów z podfolderami?”, „w folderze R01 zmień R01 na R02 w nazwach”, „spakuj R02 do zipa”,
     „otwórz Notatnik”. Działa tylko w dozwolonych folderach. To, co tylko czyta, idzie od razu; każdą zmianę
     i uruchomienie programu użytkownik zatwierdza w okienku.
   - **mkk**: „jak w Hali dodać stężenia dachowe?”, „jaki znak ma siła osiowa w Robocie, a jaki w RFEM?”, „ile zapytań
     ma plan Free API RFEM 6?”. Profil samej wiedzy o programach Marka Kowolika (Hala, Modelarz węzłów) i o Robocie,
     RFEM 5/6, IDEA StatiCa, IFC, SAP2000: model szuka w dokumentacji pobranej z repozytorium MKK (przycisk
     „Aktualizuj wiedzę”) i odpowiada z podaniem pliku; niczego nie wykonuje.
   - **office**: „co jest w notatka.docx?”, „dopisz na końcu raportu akapit…”, „zamień wszędzie R01 na R02”, „wpisz do B10
     sumę B2:B9”, „zrób w Excelu zestawienie profili…”, „zapisz raport jako PDF”, „przygotuj maila do … z załącznikiem”,
     „dodaj slajd Harmonogram”. Word, Excel, Outlook i PowerPoint przez COM zainstalowanego Microsoft 365
     (`LokalneAI.Harness.Office`): dokument otwarty u użytkownika jest edytowany na miejscu, po edycji zostaje otwarty na wierzchu.
     Czytanie idzie od razu, każdą zmianę użytkownik zatwierdza, mail zostaje szkicem (nic nie wysyła), nic nie jest kasowane.
     Tylko modele lokalne i bez internetu, bo model widzi treść dokumentów ([profile/office/README.md](profile/office/README.md)).
   - **tlumaczenie**: „przetłumacz: Podstawy słupów utwierdzone, blacha podstawy PL40…”, „przetłumacz mail: Dzień dobry, w załączeniu…”.
     Profil samej wiedzy: model oddaje tłumaczenie na angielski brytyjski (rejestr formalny, Eurokody), a terminy biura bierze
     ze słownika profilu (ostroga = shear key, płatew = purlin, zestawienie stali = steel schedule…). Najlepszy lokalny model:
     qwen3.5:9b ([profile/tlumaczenie/README.md](profile/tlumaczenie/README.md)).
   - **ocr**: „zrób OCR pliku skan.jpg”, „przeczytaj protokol.pdf (skan)”, „zrób OCR wszystkich skanów w folderze skany”, „czy raport.pdf
     to skan czy dokument?”. Tekst ze skanów, zdjęć i PDF-ów bez warstwy tekstowej rozpoznaje Tesseract 5 skryptem (bez modelu),
     z ustawieniami z Ustawienia → OCR (języki, obrót, układ tabel, próg pewności, reguły ignorowania, dpi). Obrazek wklejony do pola
     rozmowy (Ctrl+V w Code albo w Czacie) jest rozpoznawany od razu, a tekst trafia do pola; model, który widzi obrazy, dostaje
     też sam obraz (niżej: „Wklejony obrazek”). Umiejętność `office` ma też polecenia PDF:
     `pdf_czytaj`, `pdf_info`, `pdf_polacz`, `pdf_wytnij`, `pdf_obroc` ([profile/ocr/README.md](profile/ocr/README.md)).
   - **rfem**: „co jest w modelu hala.rf5?”, „jaki przekrój i długość ma pręt 28?”, „jaka jest siła w pręcie 28?”, „siły w pręcie 29 od KO5”,
     „które pręty mają HEA 160?”, „jakie są kombinacje?”. Plik RFEM 5 (.rf5) jest czytany z dysku bez programu Dlubal
     (`LokalneAI.Harness.Rfem`): węzły, pręty z długością i przekrojem, materiały, przypadki (PO) i kombinacje (KO), a w pliku
     zapisanym po obliczeniu siły N, Vy, Vz, Mt, My, Mz i przemieszczenia dla każdej kombinacji. Z uruchomionym RFEM 5 czyta też
     otwarty model przez RF-COM: „co jest otwarte w RFEM?”, „jaki przekrój ma zaznaczony pręt?” (RFEM prosi o kliknięcie pręta),
     „siły w pręcie 28 od KO5 z otwartego modelu”, „reakcje w węźle 1”. Tylko do odczytu, bez chmury
     ([profile/rfem/README.md](profile/rfem/README.md)).
   - **tablice**: „jaki Wy ma IPE 200?”, „ile waży 12 m HEB 200?”, „nośność połączenia IH1.1 IPE 240 M16 S235 8.8”, „rozstaw śrub M20”,
     „nośność śruby M16 10.9”. Umiejętność samej wiedzy: charakterystyki przekrojów z Tablic Boguckiego i Żyburtowicza (OCR skanu,
     wiersze sprawdzone zależnościami A–Iy–Wy–iy), typowe połączenia doczołowe DAST (IH, S235/S355, 8.8/10.9) i tablice SZS C5
     do połączeń (śruby, rozstawy, sprężanie, spoiny, materiały). Dane w `wiedza\dane\*.json` ([profile/tablice/README.md](profile/tablice/README.md)).
   - **cs_program, cs_ifc, cs_konstrukcja, cs_polaczenia, cs_statyka, cs_rysunki, cs_robot** (zestaw Check Structures):
     „jak policzyć tylko spoiny dla kilku modeli?”, „co znaczy klasa_p4 brak_w_bazie?”, „która spoina wygrywa, gdy rysunek
     i model mówią co innego?”, „gdzie CS stawia podporę w łańcuchu słupów?”, „co montażysta czyta z arkusza?”, „czy
     GetActiveObject działa dla Robota?”. Siedem umiejętności samej wiedzy z repozytorium `CS-cheak_structures` (ponad 1000
     plików Markdown podzielonych tematycznie, każdy plik w jednej umiejętności); dokumentacja RFEM z tego repozytorium
     doszła do `rfem`. Profil domyślny „Check Structures” ([profile/cs_program/README.md](profile/cs_program/README.md)).
   - **amadeusz**: „na czym teraz liczę?”, „jaki port bramy mam ustawiony?”, „przełącz na teklę”, „pokaż ostatnie błędy”,
     „jak podpiąć skrypt w Pythonie?”, „jaki base_url dla biblioteki openai?”. Sam Amadeusz: wiedza o oknie, portach, bramie,
     podpinaniu programów i kontroli `--autotest`, a do tego odczyt stanu, ustawień (bez danych logowania), umiejętności
     i dzienników oraz przełączanie umiejętności (za zgodą). Niczego w sobie nie zmienia; tylko modele lokalne i Claude
     ([profile/amadeusz/README.md](profile/amadeusz/README.md)).

   Dokumentację dla modelu wkleja się do `profile\<nazwa>\wiedza\`. Repozytoria z dokumentacją dodaje się przyciskiem
   **Źródła wiedzy…** w oknie asystenta; pliki aktualizują się same ([docs/profile.md](docs/profile.md)).

   Jak osadzić: [docs/osadzanie.md](docs/osadzanie.md). Serwer modelu w biurze: [docs/serwer-modelu.md](docs/serwer-modelu.md).
   Kolejka i liczenie na innym komputerze: [docs/siec.md](docs/siec.md). **Sterowanie Amadeuszem i podpinanie innych
   programów** (brama HTTP, API Ollamy i OpenAI, `--autotest`, pliki ustawień): [docs/sterowanie.md](docs/sterowanie.md).
   Porty i zapora: [docs/porty.md](docs/porty.md). **Bezpieczeństwo** — czego Amadeusz nie robi sam (także za zgodą)
   i co zawsze pyta: [docs/bezpieczenstwo.md](docs/bezpieczenstwo.md).
   Format profilu: [docs/profile.md](docs/profile.md). Wybór modelu: [docs/wybor-modelu.md](docs/wybor-modelu.md).

   **Opisy dla AI:**
   - agenci kodujący (Claude, Cursor) czytają [AGENTS.md](AGENTS.md) i [docs/biblioteki-dla-agentow.md](docs/biblioteki-dla-agentow.md);
   - agent, który ma **użyć** Amadeusza (zadać pytanie modelowi, podpiąć program, uruchomić umiejętność bez klikania),
     czyta [docs/sterowanie.md](docs/sterowanie.md);
   - gdzie wkładać dokumentację dla lokalnych modeli i jak ją pisać: [docs/wiedza-dla-modeli.md](docs/wiedza-dla-modeli.md);
   - zasady dla programistów: [docs/zasady-opisow-dla-ai.md](docs/zasady-opisow-dla-ai.md);
   - szablony profilu i plików dla repozytoriów programów: [szablony](szablony).
2. **Okno** (`LokalneAI.App`) wygląda jak TransmittalMaster (kolory i style w `LokalneAI.App\Theme.xaml`).
   Karty są w granatowym pasku tytułu:
   - **Czat**: rozmowa z modelem i statystyki „gdzie liczył”.
   - **Code**: asystent wykonujący polecenia (umiejętności `tekla`, `cde`, `pliki`, `komputer`, `office`, `ocr`, `rfem`)
     albo odpowiadający z dokumentacji (`mkk`, `tlumaczenie`, `tablice`, `cs_*`, `amadeusz`). Umiejętności zaznacza się
     jak listę wyboru — kilka naraz (niżej). Ma trzy poziomy uprawnień:
     *Tylko czyta*, *Pyta o każdą zmianę* i *Bez pytania (poza kasowaniem)*. Z lewej **panel rozmów** (niżej).
   - **Ustawienia**: gdzie liczyć (ten komputer albo mocniejszy komputer w biurze), modele w chmurze (test),
     udostępnianie tego komputera innym z tej samej podsieci, podgląd kolejki i parametry czatu.

   Lista modeli na górze okna zawiera modele komputera, który liczy, i dodane w Ustawieniach **modele w chmurze**:
   Claude (pomarańczowy), GPT (turkusowy), Gemini (niebieski). Gwiazdka dodaje model do ulubionych, a ulubione są
   na górze listy. Zasady: [docs/plan-modele-w-chmurze.md](docs/plan-modele-w-chmurze.md); w profilu `cde` wolno
   tylko modele lokalne i Claude.

   W oknie działa **brama** (`LokalneAI.Gateway`, `http://127.0.0.1:11435`, API Ollamy i API zgodne z OpenAI). Zapytania
   czatu, zakładki Code, innych programów na tym PC i innych komputerów czekają przez nią w jednej kolejce, a zapytania
   z tego komputera mają pierwszeństwo. Inny program podpina się, zmieniając adres Ollamy z portu 11434 na 11435.
   Opis: [docs/siec.md](docs/siec.md), [docs/sterowanie.md](docs/sterowanie.md), porty: [docs/porty.md](docs/porty.md).
   Co mają inne aplikacje i co dalej:
   [docs/badania/funkcje-aplikacji.md](docs/badania/funkcje-aplikacji.md).

## Wymagania

- Windows 10 albo 11.
- Ollama. Instalacja w PowerShellu, bez praw administratora:
  ```powershell
  irm https://ollama.com/install.ps1 | iex
  ```
- Model (ok. 6,7 GB). Jeśli go brakuje, okno samo zaproponuje pobranie:
  ```powershell
  ollama pull SpeakLeash/bielik-11b-v3.0-instruct:Q4_K_M
  ```
- Do budowania .NET SDK 10. Paczka z instalatora (niżej) ma program self-contained, więc na innych PC .NET
  nie jest potrzebny.

## Budowanie i uruchomienie

```powershell
dotnet build LokalneAI.sln
.\LokalneAI.App\bin\Debug\net10.0-windows\LokalneAI.exe
```

Każde zbudowanie programu (Debug) odświeża też **`Amadeusz.exe`** w głównym folderze repozytorium. To jeden plik, do
działania potrzebuje .NET 10 Desktop Runtime i folderu `profile` obok. Robi to [narzedzia/odswiez_amadeusza.ps1](narzedzia/odswiez_amadeusza.ps1),
a z przełącznikiem `-Skrot` tworzy też skrót na pulpicie. Gdy okno jest otwarte, stary plik dostaje nazwę
`Amadeusz.stary.exe`, a nowa wersja startuje przy następnym uruchomieniu. Oba pliki są w `.gitignore`.

## Obsługa

- **Enter** wysyła pytanie, **Shift+Enter** wstawia nową linię, **Esc** albo **Stop** przerywa.
- Pod każdą odpowiedzią widać, gdzie liczył model (ile modelu jest w pamięci karty, a ile na procesorze),
  liczbę tokenów, szybkość i czasy.
- **Zwolnij pamięć GPU** wyrzuca model z karty tego komputera od razu, np. przed pracą w Tekli. Bez tego Ollama
  zwalnia pamięć sama po 5 minutach bezczynności.
- **Pamiętaj rozmowę** (pod czatem) jest domyślnie wyłączone, więc każde pytanie liczy się od zera.
- **Bezpieczeństwo** ([docs/bezpieczenstwo.md](docs/bezpieczenstwo.md)): strażnik w kodzie sprawdza każde polecenie modelu,
  zanim okno zapyta o zgodę. Nigdy, także po kliknięciu „Tak”, model nie zmieni samego Amadeusza (program, ustawienia,
  umiejętności, rozmowy), nie uruchomi go, nie otworzy zapory, portów ani pulpitu zdalnego, nie przeczyta haseł i kluczy, nie doda
  autostartu, nie wyłączy Defendera, nie wyśle danych z komputera i nie sięgnie do innych komputerów — wynik „ZABLOKOWANE”
  i ostrzeżenie w oknie, wpis w `%LOCALAPPDATA%\LokalneAI\bezpieczenstwo.log`. Instalacje, uruchamianie skryptów z plików,
  rejestr, usługi i zamykanie programów pytają zawsze, także w trybie „Bez pytania”.
- **Odblokuj dostęp do internetu** (przełącznik obok listy modeli, zapamiętany): czat i zakładka Code mogą szukać
  w internecie (DuckDuckGo, bez klucza) i czytać strony, a w odpowiedzi podają źródła. Czat z internetem odpowiada
  od razu całym tekstem i działa z każdym modelem. W zakładce Code — tylko w profilach z `"internet": true`
  (nie w `cde`). Adresy w sieci biura są dla modelu zablokowane.
- **Ustawienia** mają sekcje, które zwija się i rozwija kliknięciem w nagłówek; okno pamięta, które były rozwinięte,
  a zwinięta sekcja pokazuje po prawej krótki stan (np. „Claude: 2 na liście”). Sekcje:
  - gdzie liczyć: ten komputer albo inny, z przyciskiem „Szukaj w sieci”; niżej **komputery dla podagentów** (zakładka Code):
    przełącznik „Ten komputer” i lista innych komputerów („+”, „Sprawdź”, ×) — [docs/siec.md](docs/siec.md);
  - modele w chmurze (test): klucz API każdego dostawcy (zaszyfrowany kontem Windows, „Załóż klucz…” otwiera
    konsolę dostawcy), lista jego modeli z polami „na liście” i dodawanie modelu po id. Konto claude.ai, ChatGPT
    albo Gemini nie wystarczy — programy łączą się kluczem API rozliczanym za zużycie;
  - menedżer poświadczeń: prosta lista, jeden wiersz = jeden wpis (typ PostgreSQL / FTP / SMB, IP z portem, nazwa
    bazy, user, hasło, person_id, × usuwa), „+” dodaje wiersz, „Wczytaj z pliku…” bierze plik JSON połączenia. Wszystko
    jest w **Menedżerze poświadczeń Windows** (Poświadczenia ogólne `Amadeusz:postgres:<baza>` itd.), zaszyfrowane
    kontem Windows; nic w ustawieniach programu ani w repozytorium. Wiersz zapisuje się po wyjściu z niego, bazę
    PostgreSQL program od razu sprawdza (tylko odczyt). Profil z polem `poswiadczenie` (np. `cde`) widać w zakładce
    Code dopiero, gdy jest jego wpis — kto nie ma bazy, nie widzi profilu. Dane z wersji do 0.4 (ustawienia i
    `klucze.json`) program przenosi tu sam przy pierwszym uruchomieniu;
  - udostępnianie: przełącznik, port, limit kontekstu dla innych, otwarcie portu w zaporze;
  - kolejkę z przyciskiem „Odrzuć” dla zapytań innych komputerów i liczniki tokenów: ile model tego PC przeczytał
    i napisał dla każdego komputera (dokładnie, z Ollamy), z przyciskiem „Resetuj”;
  - polecenie systemowe czatu, temperaturę (Bielik: 0,1), długość kontekstu, zapis historii i adres Ollamy.
- **Rozmowy w zakładce Code** (panel z lewej, jak w Claude czy ChatGPT): każde polecenie zapisuje rozmowę, a kliknięcie
  w panelu ją wznawia — z wpisami w oknie, zaznaczonymi profilami i historią dla modelu. Model, profile i ustawienia
  można zmienić w trakcie: następne polecenie idzie z nowymi, a rozmowa trwa dalej.
  - **Grupa = folder roboczy** („+ Nowa grupa (folder)…”). Rozmowy grupy leżą **wyłącznie** w jej podfolderze
    `.amadeusz\` (`<folder>\.amadeusz\<data>_<id>.json`, tworzonym przy pierwszym poleceniu, z `.gitignore`, żeby
    rozmowy — także z danymi z bazy CDE — nie trafiły do repozytorium). Ustawienia programu pamiętają tylko listę folderów.
  - Folder grupy jest dla profili `pliki` i `komputer` zawsze **dozwolony i bieżący na start** — „utwórz folder
    Raporty” tworzy go w folderze rozmowy.
  - Domyślna grupa to **Niezgrupowane**: `Dokumenty\Amadeusz\.amadeusz\` (prawy klik → „Zmień folder…”).
  - Prawy klik na rozmowie: zmiana nazwy, przeniesienie do innej grupy (plik przechodzi do jej `.amadeusz\`),
    „Usuń” albo × przy rozmowie (pokazuje się po najechaniu) — rozmowa idzie do `.amadeusz\kosz\` i leży tam
    7 dni (można ją przywrócić, przenosząc plik z powrotem), potem program ją kasuje. Na grupie: otwórz folder,
    zmień nazwę, usuń z listy (pliki zostają i wrócą po ponownym dodaniu folderu).
  - Kolejność: najnowsze **polecenie** (Enter) na górze (pole `LastPrompt` w pliku). Kliknięcie, zmiana nazwy
    i przeniesienie do grupy nie zmieniają kolejności; pliki sprzed tej zmiany sortują się po dacie zapisu.
- **Tokeny i czasy** (Ustawienia → Czat → Diagnostyka, „Pokaż tokeny i czasy przy wpisach w zakładce Code”): przy każdym
  wpisie dokładne liczby z Ollamy — przy poleceniu pisanie pytania i skład promptu (zasady i narzędzia, pytanie), przy kroku
  modelu ile napisał, myślenie (czytanie promptu) i pisanie, przy narzędziu czas i ile wyniku wróciło do modelu, pod turą
  Σ wejście | praca | odpowiedź, ile tokenów model przeczytał łącznie, kontekst i czasy (ładowanie, kolejka i sieć od 1 s).
  W szerokim oknie z prawej strony wpisu, w wąskim pod nim; objaśnienie po najechaniu. Przed odpowiedzią przy „Model
  pracuje…” jest szacunek (≈). Przy włączonym jedno krótkie zapytanie na sesję liczy tokeny samych zasad i narzędzi.
- **Kroki modelu** (przełącznik „Pokaż kroki modelu” nad rozmową w Code albo Ustawienia → Czat → Diagnostyka): jedno
  polecenie to pętla — model zwraca krok (wywołania narzędzi, odpowiedź albo pytanie), program wykonuje narzędzia i odsyła
  wyniki, aż do odpowiedzi albo limitu kroków (`max_krokow` w profilu, domyślnie 12; „Model pracuje (krok 3 z 12)…”).
  Z włączonym przełącznikiem każdy krok widać w rozmowie jako wpis „Krok N: wywołania → …” (surowy JSON w szczegółach),
  także w przebiegu podagentów. Wpisy zapisują się zawsze — przełącznik to tylko widok, działa też na starsze rozmowy.
- **Brakujące umiejętności** (Code): gdy do polecenia nie ma narzędzia w zaznaczonych umiejętnościach, Amadeusz nie odpowiada
  „nie potrafię”, tylko przeszukuje pozostałe umiejętności (`znajdz_umiejetnosc`: nazwy, opisy, polecenia, karta) i prosi
  o włączenie pasującej — w rozmowie pojawia się pytanie z przyciskami **Tak, włącz i ponów** / **Nie**. Po zgodzie umiejętność
  dochodzi do zaznaczonych, a polecenie idzie jeszcze raz samo, z całą rozmową (najwyżej dwa ponowienia z rzędu). Katalog
  pomija umiejętności ukryte, bez poświadczenia i te, których wybrany model w chmurze nie może liczyć. Gdy model mimo to
  odpowie „nie mam narzędzia”, program sam przeszukuje umiejętności i podsuwa mu wynik, a za drugim razem sam pokazuje
  przycisk zgody na najlepiej pasującą.
- **Podagenci** (Code): model może zlecić części pracy osobnym asystentom (`zlec_podagentowi`): każdy ma własną sesję
  i model, nie widzi rozmowy (dostaje tylko zadanie i dane), a kilka zleceń z jednego kroku liczy się równolegle na
  komputerach z Ustawień → Gdzie liczyć → Komputery dla podagentów (także „Ten komputer”). Program sam dobiera model do
  karty każdego komputera. W trakcie każdy podagent to kafelek nad polem polecenia (komputer, model, krok, sekundy,
  przycisk **Przerwij**); po skończeniu — rozwijany wpis w rozmowie z całym przebiegiem (drzewo: polecenie, kroki,
  wywołania, odpowiedź), zapisywany z rozmową. Stop przerywa wszystkich. Szczegóły: [docs/siec.md](docs/siec.md).
- **Wklejony obrazek** (Ctrl+V w polu Code albo Czatu): OCR wstawia rozpoznany tekst do pola, a gdy wybrany model widzi
  obrazy (Ollama zgłasza `vision`: gemma4, qwen3.5, ministral; modele w chmurze — tak; Bielik — nie), obraz idzie też do
  modelu, zmniejszony do 1568 px — miniatura nad polem, „×” go usuwa. Model dostaje wtedy tekst z OCR i obraz (odczyt
  i interpretacja); model bez obsługi obrazów — sam tekst.
- **Whiteboard** (zakładka między Code a Ustawieniami): tablica z narzędziami jak w Lightshocie — Zaznacz (V), Pióro (P),
  Linia (L), Strzałka (A), Prostokąt (R), Marker (M), Tekst (T), Gumka (E), kolory, grubość, Cofnij/Ponów (Ctrl+Z/Ctrl+Y),
  Delete usuwa zaznaczenie. Teksty to małe pola jednej czcionki bez formatowania (dwuklik w trybie Zaznacz je edytuje).
  Ctrl+V (albo „Wklej”) kładzie obrazek; rozmiar zmienia Zaznacz, zawsze proporcjonalnie. **Capture** chowa okno, a w miejscu
  tablicy zostaje przezroczysta ramka tego samego rozmiaru z przyciskami Zatwierdź i Zaniechaj — ramkę można przesuwać
  i zmieniać; Zatwierdź robi zrzut jej wnętrza na tablicę, po obu przyciskach okno wraca jak było. Pod tablicą pole polecenia
  i **Zatwierdź** (Enter). Model dostaje opis tablicy: teksty użytkownika **dosłownie** (nie spłaszczone do obrazu, więc bez
  OCR), linie, strzałki i prostokąty ze współrzędnymi, pióro i marker jako obszary, obrazy z OCR (raz po wklejeniu) i OCR
  odręcznych napisów (same pociągnięcia pióra; tylko przy rozsądnej pewności). Model, który widzi obrazy, dostaje też obraz
  całej tablicy. Odpowiedź jest z prawej jak w Code (kroki, wywołania), a rysunki Amadeusza (linie, strzałki, prostokąty,
  elipsy, łamane, napisy) leżą wektorowo na osobnej warstwie — „Rysunki Amadeusza” ją chowa, „Wyczyść rysunki Amadeusza”
  kasuje. Umiejętność `whiteboard` ([profile/whiteboard/README.md](profile/whiteboard/README.md)) nie pokazuje się w Code;
  „z umiejętnościami z Code” pod tablicą dokłada umiejętności zaznaczone w Code (te same uprawnienia). Tablica nie zapisuje
  się na dysk — po zamknięciu programu jest pusta.
- **Rozmowy w Czacie** (panel z lewej, jak w Code): `Dokumenty\Amadeusz\Czat\`, grupa = podfolder (`+ Nowa grupa…`),
  rozmowa zapisuje się po każdej odpowiedzi, kliknięcie ją wczytuje (z „Pamiętaj rozmowę” model zna jej wcześniejsze
  pytania). „Nowa rozmowa” czyści okno — poprzednia zostaje w panelu. × i „Usuń” → `Czat\kosz\` na 7 dni; usunięcie
  grupy przenosi jej rozmowy do kosza.
- **Ikona w zasobniku**: zamknięcie okna (×) chowa Amadeusza do zasobnika — program, brama i kolejka działają dalej;
  kliknięcie ikony wraca do okna, prawy przycisk → „Zakończ Amadeusza” naprawdę go wyłącza. Ikona: głowa Mozarta, biała
  w spoczynku, niebieska przy pracy, wokół łuk temperatury karty 30–70°C, w rogu liczba czekających zapytań. Sylwetka
  z `LokalneAI.App\Assets\mozart.png` (ciemna sylwetka na jasnym tle albo PNG z przezroczystością; bez pliku — zastępcza).
  Drugie uruchomienie skrótu pokazuje działające okno zamiast drugiej kopii.
- **Odpowiedzi w Markdownie** (Czat i Code): tabele z ramkami (liczby do prawej), listy, pogrubienia, kod, linki;
  wykresy z bloku ```` ```wykres ```` z JSON-em: słupkowe (pionowe, poziome, skumulowane, słupki w kolorach grup),
  liniowe (wypełnienie pod linią i między liniami, przerywane), kołowe i pierścieniowe; sortowanie, odwracanie osi,
  własne kolory i zakres osi, wartości po najechaniu myszą (pola: [profile/formatowanie/README.md](profile/formatowanie/README.md));
  prawy przycisk → „Kopiuj tabele i dane wykresów do Excela”. Pytania o zgodę („Utworzyć folder?”) są w rozmowie
  z przyciskami Tak/Nie, bez wyskakującego okna. Źródła wiedzy, folder umiejętności i aktualizacja dokumentacji:
  **Ustawienia → Umiejętności**. Umiejętność `formatowanie` (same zasady, bez narzędzi; domyślnie
  w każdym profilu) uczy model, kiedy dać tabelę — przy łączeniu nie jest „rolą” i nie zmienia zasad innych umiejętności.
- **Glosariusz**: plik z parami terminów w wierszach (`expansion joint – kompensator`) w `wiedza\` umiejętności albo
  wspólny dla biura na dysku (Źródła wiedzy… → Pliki i foldery na dysku). Program sam znajduje w tekście terminy
  z glosariusza (także odmienione) i podaje modelowi ich odpowiedniki.
- **Profil** (lista obok „Umiejętności”, jednokrotny wybór): nazwany zestaw umiejętności do trybu pracy, np.
  *Konstruktor* = `tekla` + `mkk` + `pliki`, *Biuro* = `office` + `tlumaczenie` + `pliki`. Wybór profilu tylko
  zaznacza jego umiejętności — potem można je zmieniać, a lista pokazuje wtedy „własny wybór”. Na dole listy
  umiejętności: „Zapisz jako profil…” (nowy albo zastąpienie o tej samej nazwie) i „Usuń profil”. Pełna edycja:
  **Ustawienia → Profile** — wiersz = profil (nazwa, pola umiejętności, ×), „+” i „Przywróć domyślne”; zmiany od razu
  widać w zakładce Code. Profile żyją w ustawieniach programu (`WorkProfiles`); na start są domyślne
  z `WorkProfileSetting.Defaults()`. Umiejętność, której folderu na danym komputerze nie ma, profil zachowuje („brak tutaj”). Umiejętności
  niedostępne na danym komputerze (np. `cde` bez poświadczenia) są pomijane, a profil bez żadnej — ukryty.
  W napisach okna „umiejętność” = folder z `profile\` (nazwa folderu i klas w kodzie jeszcze stara).
- **Kilka umiejętności naraz** (np. `tekla` + `komputer`): model dostaje polecenia systemowe, karty, narzędzia i przykłady
  wszystkich, a `szukaj_wiedzy` szuka w wiedzy każdego z nich. Obowiązują **najostrzejsze** zasady: chmura tylko
  u dostawcy dozwolonego we wszystkich (z `cde` — tylko Claude, z `mkk` — tylko modele lokalne), internet — przełącznik
  u góry jest nadrzędny: wybór umiejętności bez zgody na internet (np. `cde`) sam go wyłącza, a świadomie włączony z powrotem
  działa także z nią. `pliki` przy `komputer` wypadają (`komputer` ma wszystkie
  ich polecenia). Polecenie systemowe to suma profili (np. `tekla` ~8 tys. tokenów, `cde` ~4 tys.). Kontekst zakładki Code
  ustawia lista **„Kontekst”** nad rozmową (obok „Uprawnień”): „jak w Czacie” (kontekst z Ustawienia → Czat, ale co najmniej
  16384), 8192, 16384 albo 32768 — zapisuje się w `codeContextLength`; nagłówek pokazuje, z jakim kontekstem naprawdę liczy.
  Gdy liczy inny komputer — najwyżej tyle, ile on przyjmuje (jego Ustawienia → Udostępnianie). Przy trzech i więcej profilach
  ustaw 32768, a mały model gorzej wybiera narzędzie spośród wielu.
- Pasek na dole pokazuje, który komputer liczy (albo chmurę dostawcy, w jego kolorze), i stan kolejki.
- Model w chmurze w czacie odpowiada od razu całym tekstem (bez pisania na bieżąco). W zakładce Code pasek pod
  nagłówkiem mówi, dokąd idą dane, a gdy profil nie pozwala na wybrany model — ostrzega na czerwono.
- Gdy Ollama nie działa albo brakuje modelu, baner nad rozmową podpowiada, co zrobić
  (Uruchom Ollamę, Pobierz model, polecenie instalacji).

Odpowiedź jest pokazywana dokładnie tak, jak zwrócił ją model, łącznie ze znakami Markdowna (`**`).

## Gdzie są dane

| Plik | Zawartość |
|---|---|
| `%APPDATA%\LokalneAI\ustawienia.json` | ustawienia okna (zmieniać ręcznie tylko przy wyłączonym programie; pola: [docs/sterowanie.md](docs/sterowanie.md#pliki-i-ustawienia)) |
| `%APPDATA%\LokalneAI\tokeny.json` | liczniki tokenów bramy (Ustawienia → Kolejka) |
| `%APPDATA%\LokalneAI\klucze.json` | klucze API modeli w chmurze, zaszyfrowane kontem Windows |
| `<folder grupy>\.amadeusz\*.json` | rozmowy zakładki Code (grupa „Niezgrupowane”: `Dokumenty\Amadeusz`); kosz w `.amadeusz\kosz\` |
| `Dokumenty\Amadeusz\Czat\[<grupa>\]*.json` | rozmowy zakładki Czat; kosz w `Czat\kosz\` (kasowany po 7 dniach) |
| `%LOCALAPPDATA%\LokalneAI\wiedza_z_dysku\` | kopie plików wiedzy z dysku (np. wspólnego glosariusza) na czas, gdy dysk jest niedostępny |
| `%LOCALAPPDATA%\LokalneAI\historia.jsonl` | pytania i odpowiedzi ze statystykami, jeden JSON w linii; tylko na tym PC, zapis można wyłączyć |
| `%LOCALAPPDATA%\LokalneAI\bledy.log` | nieoczekiwane błędy programu |
| `%LOCALAPPDATA%\LokalneAI\bezpieczenstwo.log` | polecenia modelu zablokowane przez strażnika bezpieczeństwa (JSON w linii) |

## Testy

```powershell
dotnet test LokalneAI.sln
```

Testy rdzenia nie potrzebują Ollamy: zamiast niej odpowiada udawany serwer.

Kontrola całego okna bez klikania (okno otwiera się poza ekranem i nie zabiera fokusu):

```powershell
.\LokalneAI.App\bin\Debug\net10.0-windows\LokalneAI.exe --autotest "Ile to 7,5 + 8 + 6,5?" --wynik C:\folder
```

Program zada pytanie, zapisze w podanym folderze `okno.png` i `wynik.json`, po czym się zamknie.
Nie zmienia przy tym ustawień ani historii. Dodatkowe przełączniki:

- `--autotest -`: sam zrzut okna po starcie, bez pytania;
- `--url <adres>`: inny adres Ollamy;
- `--model <nazwa>`: inny model;
- `--zakladka czat|code|ustawienia|kolejka`: zakładka na zrzucie (`--rozwin` = Ustawienia, `kolejka` = Ustawienia
  z rozwiniętą kolejką i licznikami tokenów);
- `--harness <umiejętność>[+<umiejętność>…]`: kontrola zakładki Code, polecenia w `--autotest` rozdziela `||`; zapisuje
  `harness.png` i `harness.json`. Zwykłe zmiany zatwierdza sama („zawsze pytaj” strażnika odrzuca), dlatego profile `pliki` i `komputer` pracują w folderze z `--foldery`
  albo, bez niego, w pustym folderze tymczasowym (`%TEMP%\LokalneAI_kontrola`);
- `--cel <komputer>`: licz na innym komputerze (tylko w tym uruchomieniu);
- `--udostepnij`: brama przyjmuje zapytania „innych komputerów” tylko z 127.0.0.1 (próba bez otwierania portu w sieci);
- `--port <n>`: port bramy w kontroli (domyślnie dowolny wolny, żeby nie zająć portu otwartego okna);
- `--ustawienia <plik>`, `--klucze <plik>`: ustawienia i klucze API z innych plików (np. próba listy z modelami
  w chmurze bez ruszania ustawień użytkownika). Kontrola zapisuje też `lista.png`: rozwiniętą listę modeli.

Kod wyjścia: 0 to odpowiedź, 1 to błąd w odpowiedzi, 2 i 3 to wyjątek. Pełna lista przełączników, polecenia `#tokeny`,
`#profil:`, `#wznow`, `#zgoda` i pola plików wyników: [docs/sterowanie.md](docs/sterowanie.md#kontrola-bez-klikania---autotest).

## Układ

| Projekt | Co to jest | Platforma |
|---|---|---|
| `LokalneAI.Harness` | rdzeń pół-harnessu: sesja, schemat kroku, strażnicy, narzędzia wbudowane, klienci Ollamy i serwerów zgodnych z OpenAI, wykonawcy (symulator, TeklaBridge) | .NET Standard 2.0 |
| `LokalneAI.Harness.Postgres` | wykonawca SQL tylko do odczytu (profil `cde`) | .NET Standard 2.0, Npgsql 8 |
| `LokalneAI.Harness.Office` | wykonawca dokumentów Word, Excel, Outlook i PowerPoint przez COM zainstalowanego Microsoft 365 (profil `office`) | .NET Standard 2.0, bez paczek |
| `LokalneAI.Harness.Ocr` | OCR skryptem (Tesseract 5) i PDF (PDFium, PDFsharp 6.1) dla umiejętności `ocr` i poleceń PDF w `office` | .NET Standard 2.0, natywne dll x64 |
| `LokalneAI.Harness.Rfem` | czytnik modeli RFEM 5 (.rf5) bez programu Dlubal dla umiejętności `rfem`: model, siatka, siły w prętach; otwarty RFEM 5 przez RF-COM (interfejsy vtable wygenerowane z biblioteki typów) | .NET Standard 2.0, bez paczek |
| `LokalneAI.Harness.Wpf` | panel rozmowy z licznikiem kontekstu do osadzenia | .NET Framework 4.8 i .NET 10 |
| `LokalneAI.Harness.Tests` | testy xUnit, uruchamiane na .NET 10 i .NET Framework 4.8 | |
| `LokalneAI.Eval` | porównanie modeli na testach profili | .NET 10 |
| `LokalneAI.Przyklad.WinForms` | najprostsze osadzenie w WinForms (.NET Framework 4.8) | |
| `LokalneAI.Gateway` | brama: kolejka, liczenie na innym komputerze, udostępnianie w podsieci | .NET 10, ASP.NET Core (Kestrel) |
| `LokalneAI.Core`, `LokalneAI.App`, `LokalneAI.Core.Tests` | okno, jego klient Ollamy i testy (także bramy) | .NET 10 |
| `profile\tekla`, `cde`, `pliki`, `komputer`, `mkk`, `office`, `tlumaczenie`, `ocr`, `rfem`, `tablice`, `cs_program`, `cs_ifc`, `cs_konstrukcja`, `cs_polaczenia`, `cs_statyka`, `cs_rysunki`, `cs_robot`, `amadeusz`, `formatowanie` | wiedza, polecenia, przykłady i testy profili | |
| `szablony\` | szablon profilu i plików `AGENTS`, `CLAUDE`, `lokalne-ai\` dla repozytoriów programów | |
| `narzedzia\sprawdz_przyklady` | kompiluje i uruchamia przykłady z `docs\biblioteki-dla-agentow.md` | .NET 10 i .NET Framework 4.8, C# 7.3 |
| `docs\` | sterowanie i podpinanie, porty, osadzanie, opisy dla AI, wiedza dla modeli, serwer modelu, format profilu, wybór modelu, raporty z badań | |

## Instalator

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File narzedzia\zbuduj_instalator.ps1 -Wersja 0.7.0
```

Skrypt najpierw uruchamia testy, potem buduje `dist\Amadeusz-<wersja>.zip` (ok. 60 MB, program self-contained, więc
.NET nie jest potrzebny). Na komputerze użytkownika: rozpakować i uruchomić `instaluj.bat`. Z klonu repozytorium
wystarczy `instalator\instaluj.bat` (sam zbuduje program, potrzebny .NET SDK 10). Nie są potrzebne prawa
administratora. Skróty nazywają się „Amadeusz” (te ze starszych wersji, „LokalneAI”, instalator zamienia), a folder
programu i ustawień zostaje `LokalneAI`. Szczegóły w `instalator\CZYTAJ.txt`:

- `instaluj.bat`: Ollama i modele na tym komputerze — instalator pokazuje listę modeli z rozmiarami, zaznacza zalecany
  według pamięci karty graficznej i pobiera wybrane (kilka naraz; pierwszy = model domyślny programu);
- `instaluj.bat -SerwerModelu http://serwer-ai:11434`: model na serwerze w biurze;
- `instaluj.bat -Model <tag>[,<tag>…]` albo `-BezPytan` (zalecany): bez pytania o modele.

## Porównanie modeli

```powershell
dotnet build LokalneAI.sln
.\LokalneAI.Eval\bin\Debug\net10.0\LokalneAI.Eval.exe rozmiar --profil profile\tekla --model <tag>
.\LokalneAI.Eval\bin\Debug\net10.0\LokalneAI.Eval.exe test --profil profile\tekla --model <tag> --ctx 16384
.\LokalneAI.Eval\bin\Debug\net10.0\LokalneAI.Eval.exe test --profil profile\cde --model <tag> --ctx 16384 `
    --polaczenie <plik.json z host, port, dbname, user, password> --ja <person_id pytającego>
powershell -NoProfile -ExecutionPolicy Bypass -File narzedzia\porownaj_modele.ps1   # wszystkie modele i profile
```

`rozmiar` mierzy, ile tokenów zajmuje polecenie systemowe profilu. `test` przechodzi zestaw `testy.jsonl`.
`szukaj --profil <folder> --fraza "kotwy M24"` pokazuje, co model dostałby z narzędzia `szukaj_wiedzy` (bez modelu),
a `wiedza --profil <folder>` aktualizuje dokumentację z `zrodla.json`.
Dla profilu `cde` każde zapytanie wzorcowe liczy się na żywej bazie (tylko odczyt) i porównuje z wynikiem SQL modelu.
Pełne odpowiedzi lądują w `wyniki\`, który jest poza gitem (są tam godziny ludzi).

## Dalej

- Dokończenie porównania modeli (Gemma 4, Qwen3.5 4B, Qwen bez „myślenia”) — [docs/wybor-modelu.md](docs/wybor-modelu.md).
- Rola bazy tylko do odczytu dla asystenta (na każdą osobę) i sposób łączenia od użytkownika.
- Test profilu `tekla` na kopii prawdziwego modelu przez most TeklaBridge (szablony są tylko skompilowane).
- Próba bramy między dwoma komputerami w biurze (na jednym PC sprawdzona przez `127.0.0.1`).
- Pierwsza prawdziwa próba modeli w chmurze z kluczem użytkownika (Claude, GPT, Gemini) i odpowiedź na bieżąco
  w czacie — [docs/plan-modele-w-chmurze.md](docs/plan-modele-w-chmurze.md).
- Start z Windows i tryb bez okna (`--serwer`) dla komputerów, które udostępniają model; dalsze pomysły
  w [docs/badania/funkcje-aplikacji.md](docs/badania/funkcje-aplikacji.md).
