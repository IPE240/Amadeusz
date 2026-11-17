# Profil `office`: dokumenty Microsoft 365 (Word, Excel, Outlook, PowerPoint)

Asystent otwiera i edytuje dokumenty biura przez automatyzację COM zainstalowanego Microsoft 365: użytkownik pisze po polsku
(„co jest w notatka.docx?”, „dopisz na końcu raportu…”, „wpisz do B10 sumę B2:B9”, „zapisz jako PDF”, „przygotuj maila do…”),
model wybiera polecenie, a wykonawca `OfficeExecutor` z projektu `LokalneAI.Harness.Office` wykonuje je w Wordzie, Excelu,
PowerPoincie albo Outlooku. Dokument otwarty u użytkownika jest edytowany na miejscu; dokument zamknięty otwiera się
(do czytania niewidocznie, po edycji na wierzchu). Czytanie idzie bez pytania, każdą zmianę zatwierdza użytkownik, a szkic
maila nigdy nie jest wysyłany. Ścieżki tylko w dozwolonych folderach (pole nad rozmową, wspólne z profilami `pliki` i `komputer`).

## Polecenia

| Program | Polecenia |
|---|---|
| Word | `word_czytaj` (tekst z numerami akapitów, tabele jako `a \| b`), `word_dopisz` (koniec, początek albo po akapicie N; style `naglowek1..3`, `tytul`, `lista`, `lista_numerowana`; nowy plik powstaje sam), `word_zamien` (cały dokument z nagłówkami i stopkami) |
| Excel | `excel_czytaj` (tabela z literami kolumn; `arkusz`, `zakres`, `formuly`), `excel_zapisz` (liczba, tekst albo formuła; polskie nazwy funkcji, potem angielskie), `excel_wstaw_wiersze` (blok wartości, nowy skoroszyt powstaje sam) |
| PowerPoint | `powerpoint_czytaj` (tytuły, punkty, notatki), `powerpoint_dodaj_slajd` (układ „Tytuł i zawartość”) |
| Outlook | `outlook_szkic` (szkic z załącznikami z dozwolonych folderów, otwarty do sprawdzenia — NIE wysyła), `outlook_odebrane` (ostatnie wiadomości, filtr po temacie albo nadawcy), `outlook_czytaj` (treść wiadomości z listy) |
| wspólne | `office_otworz` (dokument na wierzchu; inne pliki domyślnym programem, nigdy programy), `office_zapisz_jako` (PDF i formaty Office’a), `szukaj_plikow`, `pokaz_zawartosc` (jak w `komputer`) |

## Pliki
| Plik | Co to |
|---|---|
| `profil.json` | `zakonczenie: mieszane` (po zmianie koniec tury, po czytaniu model odpowiada), `szukaj_wiedzy`, bez `chmura` (tylko modele lokalne), bez internetu |
| `system.md` | rola i 10 zasad (ok. 420 słów): najpierw czytaj, nie wysyłaj maili, nic nie kasuj, polskie formuły, krótkie odpowiedzi |
| `karta.md` | ściąga: które narzędzie do jakiej prośby, pułapki (rozszerzenia, numery akapitów, adresy komórek, daty) |
| `narzedzia.json` | 15 poleceń gospodarza |
| `przyklady.jsonl` | 23 przykłady, w tym dopytanie, odmowa wysłania maila i odmowa kasowania |
| `testy.jsonl` | 15 testów O01–O15 (`LokalneAI.Eval test`), w tym sesja dwukrokowa z Outlookiem |
| `wiedza/formuly_excel.md` | polskie nazwy funkcji, sumy po gatunkach, adresy, daty i błędy Excela |
| `wiedza/dokumenty_biura.md` | układ raportów, zestawień, maili i slajdów w biurze |

## Jak działa wykonawca (`LokalneAI.Harness.Office`)
- Późne wiązanie COM (`Type.InvokeMember` przez IDispatch), bez paczek Interop — działa na .NET Framework 4.8 i .NET 10,
  z każdą wersją Office’a, która ma COM (sprawdzone na Microsoft 365, wersja 16.0, polski interfejs).
- Każde polecenie to osobny wątek STA z limitem czasu (`OfficeOptions.Timeout`, 120 s) na wypadek okna dialogowego Office’a.
- `OfficeHost`: podłącza się do działającego programu (`GetActiveObject`) albo uruchamia nowy; dokument już otwarty
  (po `FullName`) bierze zamiast otwierać drugi raz; po czytaniu zamyka to, co sam otworzył, a program tylko wtedy, gdy sam
  go uruchomił, jest niewidoczny i pusty. Instancji, do której się tylko podłączył, nie zamyka nigdy (może należeć do innego
  programu automatyzującego Office w tle). Dwa procesy automatyzujące Office naraz (np. dwa przebiegi testów) mogą przez to
  zostawić ukrytą instancję — sprzątanie przez COM opisane w AGENTS.md.
- `ShowAfterEdit = false` (testy, praca w tle) zapisuje i zamyka bez pokazywania.
- `szukaj_plikow` i `pokaz_zawartosc` wykonuje `FileSystemExecutor` w środku, z tymi samymi dozwolonymi folderami.
- Okno (zakładka Code) tworzy wykonawcę dla profilu `office` z folderami z pola nad rozmową (`HarnessView.IsOfficeProfile`);
  folder rozmowy jest bieżący i zawsze dozwolony.

## Jak sprawdzić
```powershell
dotnet test LokalneAI.Harness.Tests -f net10.0 --filter "FullyQualifiedName~Office"     # decyzje bez Office’a + próby na żywym Wordzie, Excelu i PowerPoincie
.\LokalneAI.Eval\bin\Debug\net10.0\LokalneAI.Eval.exe rozmiar --profil profile\office --model qwen3.5:9b-q4_K_M --ctx 16384
.\LokalneAI.Eval\bin\Debug\net10.0\LokalneAI.Eval.exe test --profil profile\office --model qwen3.5:9b-q4_K_M --ctx 16384
.\LokalneAI.App\bin\Debug\net10.0-windows\LokalneAI.exe --harness office --foldery C:\folder_proby --autotest "co jest w notatka.docx?" --wynik C:\wynik
```

Testy na żywo (`OfficeLiveTests`) pracują w folderze tymczasowym z `ShowAfterEdit = false`; na komputerze bez Office’a
przechodzą bez sprawdzania. Outlooka testy nie ruszają (szkic otworzyłby okno w poczcie użytkownika) — `outlook_szkic`,
`outlook_odebrane` i `outlook_czytaj` sprawdza się ręcznie w oknie.

## Sprawdzone 2026-11-17
- `dotnet test LokalneAI.Harness.Tests`: 260 + 260 testów przechodzi (net10.0 i net48), w tym 14 testów Office’a na żywym
  Microsoft 365 (Word: nowy dokument ze stylami, dopisanie po akapicie, czytanie z numerami, zamiana bez wielkości liter, PDF;
  Excel: nowy skoroszyt z blokiem wartości i datą, `=SUMA`, `=SUM` jako zapas, czytanie wartości i formuł, zły arkusz, zła formuła;
  PowerPoint: dwa slajdy, czytanie, PDF; sesja z udawanym modelem: zmiana z pytaniem i koniec tury, czytanie bez pytania).
- `LokalneAI.Eval rozmiar` (qwen3.5:9b-q4_K_M): polecenie systemowe 5090 tokenów z pytaniem — mieści się w 16384.
- Kontrola okna bez klikania (`--harness office`, qwen3.5:9b-q4_K_M, liczone na ODS27): „napisz do Worda notatkę notatka.docx:
  tytuł …, punkty …” → dwa `word_dopisz` (nagłówek, lista), 9 s; „co jest w pliku notatka.docx?” → `szukaj_plikow`,
  `word_czytaj` i poprawna odpowiedź, 5 s; „zapisz notatka.docx jako PDF” → `office_zapisz_jako`, 5 s. Po kontroli dokument
  został otwarty w Wordzie (tak ma być w oknie; w testach `ShowAfterEdit = false`).
- `LokalneAI.Eval test` (qwen3.5:9b-q4_K_M, RTX 4060, ctx 16384, 15 testów, 16 kroków): pierwszy przebieg 9/16 ściśle,
  10/16 luźno, średnio 49 s na krok, 13 min. Jeden krok (O02) przepadł przez limit 300 s (model „myśli”; harness nie wysyła
  `think:false`). Pozostałe nietrafienia to jeden wzorzec: model najpierw wołał `szukaj_plikow` z pełną nazwą pliku, a dopiero
  potem właściwe polecenie — tak kazała pierwsza wersja zasady 2 w `system.md`. W oknie to działało, ale kosztowało wywołanie,
  więc zasada mówi teraz: pełna nazwa pliku = od razu polecenie, `szukaj_plikow` dopiero po „nie ma pliku” albo przy opisie
  bez nazwy. Powtórka siedmiu nietrafionych testów po zmianie (`--tylko O02,O03,O05,O06,O07,O09,O15`): 7/7 ściśle, średnio
  16 s na krok (O02 tym razem 60 s zamiast limitu) — razem z pierwszym przebiegiem wszystkie 16 kroków trafione.

## Czego nie sprawdzono
- Outlooka na żywo (`outlook_szkic`, `outlook_odebrane`, `outlook_czytaj`): kod przeszedł tylko kompilację i decyzje bez COM;
  pierwszą próbę w oknie robi użytkownik (szkic otworzy się w jego Outlooku).
- Dokumentu otwartego u użytkownika (edycja na miejscu w widocznym Wordzie/Excelu) — ścieżka `FindOpen` po `FullName`.
- Modeli w chmurze (profil ich nie dopuszcza) i Bielika (tylko qwen3.5:9b).

## Decyzje
- **Bez `chmura`** (tylko modele lokalne) i **`internet: false`**: model widzi treść dokumentów i maili biura (umowy, raporty,
  korespondencja). Dopisanie `"chmura": ["anthropic"]` albo `"internet": true` w `profil.json` to decyzja użytkownika.
- **COM, nie OpenXML**: użytkownik chce dokumenty *otwierać* i edytować tak, jak je widzi; COM daje też Outlooka i zapis do PDF
  silnikiem Office’a. Wymaga zainstalowanego Office’a na komputerze z asystentem (w biurze jest wszędzie).
- **Po edycji dokument zostaje otwarty na wierzchu** — użytkownik widzi zmianę i może ją cofnąć w programie (zapis już się odbył).
- **Szkic maila bez pytania o zgodę**, bo niczego nie zmienia i nie wysyła; użytkownik i tak widzi go przed wysłaniem.
  Adresatów model bierze od użytkownika albo z przeczytanej wiadomości (zasada 5 w `system.md`).
- **Nic nie kasuje** (plików, wiadomości, slajdów); `word_zamien` z pustym `zamien_na` usuwa tylko wskazany tekst.
- **`office_otworz` nie uruchamia programów** (`.exe`, `.bat`, `.ps1`…) — od tego jest `uruchom` z profilu `komputer`, z pytaniem.
- **Formuły**: `FormulaLocal` (polskie nazwy, średniki), a gdy Excel zwróci `#NAZWA?` — `Formula` (angielskie). Wynik mówi,
  co komórka pokazuje.
- **Wartości czytane** po polsku (`12,5`), daty `RRRR-MM-DD`, błędy jako `#BŁĄD(#NAZWA?)`; najwyżej 200 wierszy × 40 kolumn,
  dalsze przez `zakres`.
