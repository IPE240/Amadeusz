# Gdzie liczyć: liczenie na innym komputerze (mocniejsza karta)

**Ustawienia → Gdzie liczyć → Na innym komputerze w sieci**: wpisz nazwę (np. PC-ODS12) albo IP i kliknij **Połącz**, albo **Szukaj w sieci** i **Użyj**. Po połączeniu lista modeli u góry okna pokazuje modele tamtego komputera, a pasek na dole „Liczy: …”. Rozmowa zostaje na Twoim PC: brama za każdym razem wysyła tamtemu komputerowi całe zapytanie z kontekstem, a on tylko liczy i niczego nie zapisuje. Programy podpięte do Twojej bramy (127.0.0.1:11435) też liczą tam. Zakładka Code sama zmniejsza kontekst do limitu tamtego komputera.

# Udostępnianie tego komputera innym (przełącznik, zapora, limit kontekstu)

Na mocnym komputerze: (1) **Ustawienia → Udostępnianie** → zaznacz „Pozwalaj na przeliczanie zapytań z innych komputerów”; (2) kliknij **Otwórz port w zaporze…** i zatwierdź UAC (raz); (3) program musi działać — wystarczy w zasobniku. Brama wpuszcza tylko komputery z tej samej podsieci i tylko liczenie (bez pobierania i usuwania modeli). Limit kontekstu dla innych (domyślnie 16384) chroni pamięć karty: większe zapytanie dostaje błąd, nie obcięcie. `keep_alive` innych zastępuje wartość właściciela (5 min). Bez hasła i bez szyfrowania — nie wysyłaj na cudzy PC danych osobowych (np. godzin z CDE).

# Kolejka: kto liczy, kto czeka, pierwszeństwo, Odrzuć

**Ustawienia → Kolejka** pokazuje każde zapytanie: kto (ten komputer albo NAZWA-PC z adresem IP), program (Czat, Code: tekla, Podagent 2…), model i stan (czeka / liczy / na innym komputerze, sekundy). Ollama liczy jedno zapytanie naraz, reszta czeka w bramie. Zapytania z tego komputera mają pierwszeństwo; zapytanie innego komputera po 60 s czekania wraca do kolejności przyjścia. Z innych komputerów może czekać najwyżej 8 zapytań (dziewiąte: „kolejka pełna”). **Odrzuć** przerywa zapytanie innego komputera. Czat pokazuje „Czeka w kolejce — przed nim: …”.

# Liczniki tokenów w kolejce: który komputer ile tokenów, Resetuj

Pod kolejką: „Tokeny policzone na tym komputerze” — wiersz na każdy komputer, który pytał (i „ten komputer”): liczba zapytań, ile model przeczytał (prompt) i napisał, suma, ostatnio; przy kilku wierszach „Razem”. Liczby są dokładne, z odpowiedzi Ollamy (prompt_eval_count, eval_count). Zapytanie przekazane przez „Gdzie liczyć” liczy komputer, który je policzył. Sumy zostają po zamknięciu (`%APPDATA%\LokalneAI\tokeny.json`). **Resetuj** zeruje i liczy od teraz.

# Jak komputer się przedstawia: nazwa czy adres IP

Inny komputer widzi w kolejce i licznikach nazwę Windows pytającego PC i jego adres IP, np. „PC-ODS12 (10.0.0.5)”. Nazwę wysyła nagłówek `X-LokalneAI-Komputer`: ustawia go brama przy „Gdzie liczyć” i zakładka Code przy podagentach. Program podpięty wprost do cudzej bramy bez tego nagłówka widnieje jako sam adres IP. Nazwa to podpis, nie uwierzytelnienie — o dostępie decyduje adres z podsieci.

# Podagenci: komputery dla podagentów, zlec_podagentowi, Przerwij

Zakładka Code może zlecać części pracy podagentom (narzędzie `zlec_podagentowi`): osobne sesje z własnym modelem, liczone równolegle; podagent nie widzi rozmowy, dostaje tylko zadanie. Komputery: **Ustawienia → Gdzie liczyć → Komputery dla podagentów** — przełącznik „Ten komputer” i lista innych („+”, **Sprawdź**, ×). Tamte komputery muszą udostępniać model. Program sam wybiera najmniej obciążony komputer i model mieszczący się w jego karcie. W trakcie każdy podagent to kafelek nad polem polecenia z przyciskiem **Przerwij**; potem rozwijany wpis z przebiegiem.
