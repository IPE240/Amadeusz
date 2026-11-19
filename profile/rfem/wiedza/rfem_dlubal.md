# RFEM 6 i RFEM 5 w CS-check: API II, obwiednie sił, RF-COM, lustro modelu, wyniki z pliku .rf6

## RFEM 6 przez API II (dlubal.api, gRPC, port 9000): rfem_setup, klucz ak-, plan Free, wersja klienta = wersja RFEM
`python tools/rfem_setup.py` robi całą konfigurację jednym poleceniem: wykrywa wersję RFEM (z URUCHOMIONEGO procesu, nie
z rejestru — na maszynie z trzema RFEM-ami pierwszy wpis rejestru to nie to, co działa), zakłada osobne środowisko
`tools/rfem_client/.venv`, instaluje pasującą wersję `dlubal.api` (wersja wskazana w ostrzeżeniu serwera, po odcięciu kropki
kończącej zdanie), przyjmuje klucz na ślepo i sprawdza połączenie (`--sprawdz` = sama diagnostyka). Klucz API II z Extranetu
Dlubala (API & Cloud → API II → My API Keys), postać `ak-…`, osobisty, zapisany w `tools/rfem_client/.dlubal_api_key` (poza
gitem); klucz z API I daje `UNAUTHENTICATED - API key is in incorrect format`. Plan Free: 1000 zapytań miesięcznie, jeden
aktywny użytkownik; połączenie + nazwa modelu = 2 zapytania, jeden rodzaj obiektów = 2, całe pobranie modelu (8 rodzajów) ≈ 19,
czyli ok. 50 pełnych pobrań; zużycie liczone w `tools/rfem_client/model_cache/api_usage.jsonl`. W RFEM niczego się nie włącza:
serwer API II nasłuchuje na porcie 9000 od startu (port 8081 = stary SOAP); wyjątek RFEM 6.16: port milczy, dopóki nie otworzy
się okna Opcji programu (nie restartować RFEM). Musi być otwarty model, nie sam ekran startowy (`UNAVAILABLE` / `deadline
exceeded`). Każdy projektant ma inną wersję RFEM, więc środowisko klienta jest per komputer; rozbieżność jest widoczna, nie
domyślana. Pułapka: `get_object_list(only_selected=True)` zwraca WSZYSTKO (filtr martwy w 6.15 / 2.15.2); pręt RFEM nie ma
węzłów, leży na linii, a co siódma linia w dużym modelu to łuk.

## Pobranie żywego modelu RFEM 6 do CS: rfem_pull --zywy --core --static, snapshoty, co jest pobierane, czego nie ma
`python tools/rfem_pull.py --zywy --core --static` pobiera z uruchomionego RFEM osiem rodzajów obiektów: węzły, linie, pręty,
powierzchnie, przekroje, materiały, zestawy linii i prętów (pełny zrzut w kolumnie `raw`, nic nie ginie). `--core` = model
widoczny w podglądzie 3D obok modeli z Tekli (w tych samych współrzędnych); `--static` = model obliczeniowy z wersjonowaniem
i porównywaniem wersji; `--sprawdz-tylko` = bez zapisu; `--lista` = co już jest; plik JSON zamiast żywego modelu też działa.
Odczyt jest bezpieczny: narzędzie (`tools/rfem_client/tool_cs_snapshot_pull.py`) nie ma żadnej operacji zapisu. Snapshoty
(`rfem.rfem_snapshot`) są niemutowalne: każde pobranie to nowy wpis, stąd historia modelu („co się zmieniło od wtorku”).
Snapshot pobrany w połowie (limit planu Free) nie wchodzi do bazy. Czego nie ma: podpór (CS nie ma na nie tabeli, więc model
z RFEM 6 nie przechodzi walidacji `static` — „model has no supports (mechanism)”), obciążeń (CS nie pobiera ani nie zapisuje;
kombinacje i sytuacje czyta `rfem_obwiednia.py --lista`), zapisu do RFEM (eksporter `static` to zaślepka). Adapter
`from_modeldb.py` robi migawkę z `model.db` pliku `.rf6` bez RFEM i bez API; `model_obl.py` = model obliczeniowy jako
pełnoprawny model (IFC pochodny); most `to_static.py`, ingest `to_core.py`, powierzchnie rfem → core.

## Obwiednie sił wewnętrznych z RFEM 6: rfem_obwiednia --sytuacja --przegub --pret, wartości odpowiadające, kontrola
`python tools/rfem_obwiednia.py --sytuacja DS20 --przegub 133` (albo `--pret 138 141`; `--lista` pokazuje sytuacje
obliczeniowe z liczbą kombinacji i przeguby prętowe). RFEM musi mieć otwarty, POLICZONY model; odczyt nic nie zapisuje.
Wynik ma zawsze trzy części: obwiednia (min i max każdej składowej N, Vy, Vz, Mt, My, Mz z miejscem i kombinacją miarodajną),
wartości odpowiadające (pełny komplet sześciu składowych z JEDNEJ kombinacji i jednego miejsca, bo ekstrema obwiedni nie
występują jednocześnie) i kontrola wobec obwiedni własnej RFEM (`ZGODNE`, `ROZBIEZNOSC … NIE UZYWAJ tych liczb`,
`NIEROZSTRZYGNIETA`; kod wyjścia 1 przy rozbieżności). Jednostki kN i kNm, osie lokalne pręta. Przegub to definicja użyta
w wielu miejscach (przegub 133 na 64 końcach prętów): obwiednia obejmuje wszystkie i podaje, gdzie wypadło ekstremum; nagłówek
pokazuje definicję przegubu (przegub ze sprężynami przenosi momenty). Nie mieszać `--przegub` z `--pret` (dwie tabele). Zapis
`--xlsx` / `--csv`. Koszt: `--lista` 4 zapytania, obwiednia 5–6 (filtr po stronie serwera). Druga droga: obwiednie z gotowego
eksportu XLSX bez żywego RFEM (`tools/rfem_obwiednia_przegubow_xlsx.py`, `obwiednia_przegubu.py` pytający o numer); trzecia:
wyniki wprost z pliku `.rf6` (`rf6_plik.py`, sekundy, bez API). Siły do węzłów IDEA: `tools/rfem_do_idea.py` (umiejętność
`cs_polaczenia`).

## RFEM 5 przez RF-COM: rfem5_setup, comtypes zamiast pywin32, biblioteka typów, LockLicense, licencja stalowa
RFEM 5 (5.27, 5.31) i RFEM 6 to dwa różne światy: RF-COM (COM lokalnie, dodatek w licencji, bez limitu zapytań, biblioteka
`comtypes`, czyta podpory, obciążenia i wyniki) kontra API II (gRPC, klucz, limit, `dlubal.api`). `python tools/rfem5_setup.py`
sprawdza pięć kroków (jest RFEM 5, działa, `comtypes`, COM oddaje sesję, licencja RF-COM) i wypisuje widziany model; `--sprawdz`
bez instalowania. Trzy pułapki: (1) `win32com.client.Dispatch("RFEM5.Application")` zawsze zwraca `E_NOINTERFACE` — RF-COM to
interfejs vtable z biblioteki typów, nie IDispatch; działa `comtypes` z biblioteką typów wskazaną wprost (wzorzec z Robota nie
przenosi się); (2) biblioteki typów nie ma pod CLSID — leży w `HKLM\SOFTWARE\Classes\TypeLib\…\RFEM5_64.dll`, szukana po
nazwie „Dlubal RFEM Type Library v5.x”; (3) `IsComLicensed()` kłamie — sprawdzianem jest `LockLicense()`; bez blokady każde
wywołanie wraca `CLASS_E_NOTLICENSED` (−2147221230). RF-COM jest płatnym dodatkiem przypisanym do części licencji (w biurze:
jest na „stalowej”, nie ma na „żelbetowej”), więc ten sam kod działa albo nie zależnie od otwartej sesji. Blokadę trzymać
krótko: przez czas `LockLicense()` GUI projektanta jest zablokowane; menedżer `rfem5_com.licencja(app)` zwalnia ją nawet przy
wyjątku (`try/finally`). Własny skrypt: `app, _ = rfem5_com.polacz()`; `with rfem5_com.licencja(app): rfem5_com.podsumuj_model(app)`.
Pobrania modelu RFEM 5 do bazy CS jeszcze nie ma (dla RFEM 6 jest); kąt obrotu pręta w radianach. Plik `.rf5` bez Dlubala
czyta sama umiejętność `rfem` (polecenia `rfem_*`).

## Zasada bezpieczeństwa przy zapisie do żywego RFEM: plan, --apply, --save (trzy bramki)
Żywy model w RFEM jest nieodwracalny: po zapisie nie ma „cofnij”, a plik `.rf6` zwykle nie jest w żadnym systemie wersji.
Protokół trzech bramek dla skryptów piszących do RFEM: (1) domyślnie skrypt tylko liczy i pokazuje plan (numery obiektów,
współrzędne), niczego nie dotyka; (2) `--apply` zmienia model, ale tylko w pamięci otwartej sesji — zły `--apply` to niezapisana
sesja, którą się zamyka; (3) `--save` to osobna, świadoma decyzja, nigdy automatyczna. Nie zapisywać modelu, którego planu się
nie przeczytało. Adapter API II z licznikiem zapytań i bezpieczeństwem zapisu: `tools/rfem_client/rfem_api.py`
(`with rfem_api.connect("moj_skrypt") as cx:`). Osobne środowisko `tools/rfem_client/.venv` istnieje tylko dlatego, że
`dlubal.api` jest przywiązany do wersji RFEM na danym komputerze; kod pochodzi z `rfem-robot-bridge` (kopia, może się rozjechać).
