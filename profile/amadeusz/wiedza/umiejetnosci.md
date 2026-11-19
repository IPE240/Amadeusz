# Umiejętności Amadeusza: lista (tekla, cde, pliki, komputer, office, ocr, rfem…)

- **tekla** — polecenia w modelu Tekli przez most TeklaBridge; **cde** — pytania do bazy CDE (SQL tylko do odczytu, widoczna z wpisem w Menedżerze poświadczeń);
- **pliki** — foldery, kopiowanie, przenoszenie, bez kasowania; **komputer** — PowerShell, programy, czytanie i szukanie plików, każda zmiana za zgodą;
- **office** — Word, Excel, Outlook (szkic maila), PowerPoint, PDF; **ocr** — tekst ze skanów i zdjęć; **rfem** — modele RFEM 5 (.rf5) i otwarty RFEM;
- wiedza bez poleceń: **mkk** (Hala, Robot, RFEM, IDEA), **tablice** (Żyburtowicz, DAST, SZS C5), **tlumaczenie**, **cs_*** (Check Structures);
- **amadeusz** — sam Amadeusz: wiedza, odczyt stanu i ustawień, przełączanie umiejętności (modele lokalne i Claude);
- **formatowanie** — same zasady tabel i wykresów, domyślnie w każdym profilu.

# Profil (zestaw umiejętności) a umiejętność; kilka umiejętności naraz

Umiejętność = folder `profile\<nazwa>\`. Profil = nazwany zestaw umiejętności (lista obok „Umiejętności” w zakładce Code), np. Konstruktor = tekla + mkk + pliki, Biuro = office + tlumaczenie + pliki. Edycja: Ustawienia → Profile. Kilka umiejętności naraz: model dostaje zasady, narzędzia i wiedzę wszystkich; chmura tylko, gdy pozwalają wszystkie; internet — przełącznik u góry nadrzędny (wybór umiejętności bez zgody go wyłącza, można świadomie włączyć z powrotem). Każda umiejętność powiększa polecenie systemowe — przy trzech i więcej ustaw kontekst 32768.

# Kontekst zakładki Code: lista „Kontekst” nad rozmową (inny niż w Czacie)

Zakładka Code ma własny kontekst: lista „Kontekst” obok „Uprawnień” — „jak w Czacie” (kontekst z Ustawienia → Czat, ale co najmniej 16384), 8192, 16384 albo 32768; w ustawienia.json pole codeContextLength (0 = jak w Czacie). Nagłówek nad rozmową pokazuje, z jakim kontekstem naprawdę liczy. Gdy liczy inny komputer — najwyżej tyle, ile on przyjmuje („kontekst do” w jego Ustawieniach → Udostępnianie). Zmiana działa od następnego polecenia, rozmowa trwa dalej. 8192 to mało: zasady większych umiejętności (Tekla, RFEM, Office) mogą się nie zmieścić.

# Dodać nową umiejętność: folder, profil.json, system.md, wiedza

1. Skopiuj szablon `szablony\profil` z repozytorium do `profile\<nazwa>\` (obok programu albo w repozytorium).
2. `profil.json`: nazwa, opis, `zakonczenie` (model / auto / mieszane), `narzedzia_wbudowane` (np. szukaj_wiedzy, kalkulator), `max_krokow`, opcjonalnie `internet`, `chmura`.
3. `system.md` — rola i zasady (do ok. 400 słów); `karta.md` — krótka ściąga; `narzedzia.json` — polecenia (`{"narzedzia": []}` dla samej wiedzy); `przyklady.jsonl` — przykłady.
4. `wiedza\*.md` — dłuższa wiedza w sekcjach do ok. 900 znaków (nagłówek ze słowami z pytań).
5. Uruchom program ponownie — umiejętność pojawi się na liście. Opis formatu: `docs\profile.md`.

# Wiedza z repozytoriów: Źródła wiedzy, zrodla.json, Aktualizuj wiedzę

**Ustawienia → Umiejętności → Źródła wiedzy…**: adres repozytorium (jak do git clone) i wzorce plików, np. `README.md; docs/**/*.md`. Pliki trafiają do `wiedza\zrodla\<repozytorium>\` (nie edytować — nadpisze je aktualizacja) i odświeżają się same co 6 godzin albo przyciskiem „Aktualizuj wiedzę”. Potrzebny git z dostępem do repozytorium. Własne notatki (.md, .txt) wrzuca się do `profile\<nazwa>\wiedza\`. Glosariusz: plik z parami terminów w wierszach (`expansion joint – kompensator`).
