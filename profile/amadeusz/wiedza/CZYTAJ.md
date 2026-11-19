# Wiedza umiejętności „amadeusz”

Ten plik jest dla ludzi: model go nie widzi (wyszukiwarka pomija `CZYTAJ.md`).

Wiedza o samym Amadeuszu, przepisana z dokumentacji repozytorium na krótkie sekcje dla lokalnego modelu (jedna sekcja
`#`/`##` = jedno zagadnienie, do ok. 900 znaków, bo tyle `szukaj_wiedzy` oddaje modelowi):

- `porty.md` — porty, zapora, połączenia wychodzące (źródło: `docs\porty.md`);
- `brama_api.md` — trasy, nagłówki, `/lokalneai/info`, kody błędów, przykłady PowerShell, curl, Python, OpenAI (`docs\sterowanie.md`);
- `podpinanie.md` — inne programy i biblioteki: Open WebUI, Continue, LangChain, ollama-python, OpenAI SDK, .NET (`docs\sterowanie.md`);
- `kontrola_autotest.md` — `--autotest`, `--harness`, polecenia `#…`, pliki wyników, kody wyjścia, `LokalneAI.Eval` (`docs\sterowanie.md`, `README.md`);
- `siec.md` — Gdzie liczyć, udostępnianie, kolejka, liczniki tokenów, podagenci (`docs\siec.md`);
- `okno.md` — obsługa okna: zakładki, sekcje Ustawień, zasobnik, rozmowy, tokeny, internet, chmura (`README.md`);
- `pliki_i_ustawienia.md` — gdzie leżą dane, pola `ustawienia.json` (`docs\sterowanie.md`);
- `umiejetnosci.md` — lista umiejętności, profile, dodawanie nowej, `profil.json` (`docs\profile.md`);
- `instalacja.md` — instalator, wybór modeli, karta graficzna, kontekst (`instalator\CZYTAJ.txt`, `docs\wybor-modelu.md`);
- `problemy.md` — objawy i co sprawdzić;
- `bezpieczenstwo.md` — czego Amadeusz nie robi sam (blokady), co zawsze pyta, dziennik blokad, adresy stron (`docs\bezpieczenstwo.md`).

**Zmiana w programie (port, przełącznik, trasa bramy, przycisk) = zmiana w `docs\` i tutaj.** Test `AmadeuszWiedzaTests`
sprawdza, że każdy przełącznik `LokalneAI.exe` i każda trasa bramy są opisane w `docs\sterowanie.md` i w tej wiedzy.
