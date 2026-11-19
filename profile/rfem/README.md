# Umiejętność `rfem`: modele RFEM 5 (.rf5) bez programu Dlubal

Program czyta plik .rf5 z dysku (ZIP albo starszy kontener OLE) i oddaje modelowi gotowe tabele: węzły, linie, pręty
z typem, długością i przekrojem, przekroje z charakterystykami i materiałem, przypadki (PO) i kombinacje (KO, KW),
a w pliku zapisanym po obliczeniu — siły wewnętrzne w prętach (N, Vy, Vz, Mt, My, Mz) i przemieszczenia dla każdej
kombinacji. RFEM nie musi być zainstalowany. Wszystko tylko do odczytu, bez pytań o zgodę; dozwolone foldery jak dla `pliki`.

## Polecenia

| Polecenie | Co robi |
|---|---|
| `rfem_info` | podsumowanie: wersja, kontener, liczby węzłów/linii/prętów (z typami), przekroje z liczbą prętów, materiały, przypadki, kombinacje, czy są wyniki |
| `rfem_pret` | pręt: typ, linia, węzły ze współrzędnymi, długość, obrót, przekrój (A, Iy, Iz, It, wymiary) i materiał; przy wynikach ekstrema sił ze wszystkich kombinacji |
| `rfem_wezel` | współrzędne węzła, linie przez węzeł, pręty w węźle |
| `rfem_przekroje` | tabela przekrojów (A [cm²], Iy, Iz, It [cm⁴], pręty, łączna długość) i materiały (E, G, γ, norma) |
| `rfem_przypadki` | PO z opisami, KO z wyrażeniami (`1.35*PO1 + 1.5*PO11`), KW (obwiednie), które mają wyniki |
| `rfem_sily` | siły pręta [kN, kNm]: bez `obciazenie` ekstrema ze wszystkich kombinacji (z numerem KO i x), z `obciazenie=KO5` przebieg wzdłuż pręta; `co=przemieszczenia` → ugięcia [mm] |
| `rfem_szukaj_pretow` | pręty po przekroju (numer albo fragment nazwy), typie, węźle, zakresie długości |
| `szukaj_plikow`, `pokaz_zawartosc` | jak w `komputer` (np. `*.rf5`) |

Bez `sciezka` polecenia biorą jedyny plik .rf5 z folderu bieżącego (folderu rozmowy).

## Pliki
| Plik | Co to |
|---|---|
| `profil.json` | `zakonczenie: model` (wszystko czyta, model odpowiada), `szukaj_wiedzy` i `kalkulator`, bez `chmura`, bez internetu |
| `system.md` | rola i 14 zasad: 1–10 plik (najpierw narzędzie, pełna nazwa pliku = od razu polecenie, siła bez kombinacji = ekstrema, jednostki, PO/KO/KW, czego nie ma w odczycie, pręt widoczny w RFEM a nieobecny w odczycie = nie upierać się), 11 wiedza o RFEM 6 i RF-COM z CS, 12–14 otwarty RFEM |
| `karta.md` | ściąga: które narzędzie, jak czytać wynik (jednostki, znaki, x), czego nie ma |
| `narzedzia.json` | 16 poleceń: 9 na pliku .rf5 (z szukaj_plikow i pokaz_zawartosc) i 7 na otwartym RFEM 5 (`rfem_otwarty*`, `rfem_wskaz`) |
| `przyklady.jsonl` | 32 przykłady, w tym bez wołania (reakcje podporowe z pliku, pytanie bez numeru pręta) i z modelu z Tekli (element Tekli, siły od PO1) |
| `testy.jsonl` | 17 testów F01–F17 (`LokalneAI.Eval test`): F01–F10 plik, F11–F15 otwarty RFEM, F16 model z Tekli, F17 pręt widoczny w RFEM a nieobecny w odczycie |
| `wiedza/rfem5.md` | co program czyta z .rf5, jednostki i znaki, kiedy są wyniki, czego nie ma |

## Jak działa (`LokalneAI.Harness.Rfem`, bez paczek)
- `Rf5Archive`: kontener ZIP (`System.IO.Compression`) albo OLE (`CompoundFile`, własny czytnik CFB: sektory 512/4096 B,
  DIFAT, mini-strumienie). Cały plik czytany do pamięci — bez uchwytu, żeby RFEM mógł w tym czasie zapisywać.
- `Rf5Parser`: strumień modelu (`RFEM Input Data.bin` w ZIP, `RFEM_Data 2.002` w OLE). Każdy obiekt to rekord ze znacznikiem
  `OwnerParamGroup`; nagłówek przed nim: `[i32 numer][tekst 1][tekst 2][00×6][01×n]`, teksty `[u8 len]` albo `[FF][u16 len]`
  (komentarz; eksport z Tekli wpisuje tam 266-znakowe `<TeklaItemMetadata>` z GUID-em elementu → `Rf5Member.TeklaGuid`).
  Rozpoznawane układy: węzeł (3×f8 + układ współrzędnych), linia (lista `Numbers` węzłów, czasem z kątem f4), pręt
  (f4 obrót, i32 linia, typ `Beam`/`BeamRigid`/`TrussN`…, lista przekrojów). Tabele przekrojów, materiałów, przypadków,
  kombinacji obciążeń i kombinacji wyników mają własne stałe układy (opis w komentarzach kodu). Nazwy są w stronie kodowej 1250.
  Rekordy o nieznanym układzie (powierzchnie, podpory, przeguby, obciążenia) są liczone i pomijane (`Warnings`).
  Wyniki samego przypadku n liczonego razem z kombinacjami leżą pod `NCOMB = 10000 − n` w protokole — czytane jako PO n.
- `Rf5Mesh`: `Mesh/.MTO` (kolejność prętów w plikach wyników), `Mesh/.geo` (węzły, linie, MACR — zapas na długości).
- `Rf5Results`: `Results_Main_V_2/.HXN<nr>` (obliczenia nieliniowe) i `.HXL<nr>` (liniowe) — nagłówek int64 z offsetami
  bloków: pręty (liczba punktów, pierwszy punkt), punkty (f8 x), siły 6×f4 [N, Nm], przemieszczenia 6×f4. Który to przypadek
  albo kombinacja, mówi protokół solvera `.PRON<nr>`/`.PROL<nr>` (`NCOMB=`, `NCASE=`, `NCOEF=`) — etykieta „KO5 (1.35*PO1 + …)”
  jest z pliku, nie z założenia o kolejności.
- `RfemExecutor`: polecenia, pamięć podręczna wczytanych plików (odświeżana po zmianie daty albo rozmiaru), jednostki
  dla ludzi (m, kN, kNm, cm², cm⁴, mm, przecinek dziesiętny).

## Sprawdzone
- Testy jednostkowe (`RfemUnitTests`, 13) na danych syntetycznych w rozpoznanym układzie: rekordy modelu, przekroje, materiały,
  PO/KO/KW, siatka, protokół i plik sił, czytnik OLE, decyzje i odpowiedzi wykonawcy, pliki umiejętności.
- Testy na modelach biura (`RfemLiveTests`): ścieżki plików .rf5 w zmiennej `LOKALNEAI_RFEM_PROBKA` (rozdzielone `;`);
  bez zmiennej test przechodzi bez sprawdzania. Pliki klientów nie leżą w repozytorium.
- Rozpoznanie formatu: pięć modeli biura z RFEM 5.27 (ZIP i OLE, z wynikami i bez, prętowe i powierzchniowy). Znak N
  (ujemne = ściskanie) sprawdzony na ciężarze własnym słupa; kolejność składowych jak w RFEM 6 (MKK); znak Mz niesprawdzony.
- 2026-11-19, szósty model (F10, eksport z Tekli, 222 MB, 524 pręty, 240 KO z wynikami): parser widział 163 pręty i „pręta 14 nie ma”,
  bo każdy pręt miał w nagłówku 266-znakowe metadane Tekli z długością `FF`+u16, a wyniki samych przypadków leżały pod
  `NCOMB = 10000 − n`. Po poprawce (`ReadHeader`, `RfemHeaderTests`) wszystkie sześć modeli przechodzi `RfemLiveTests`.

## Ograniczenia
- Nie czyta powierzchni, podpór, przegubów, obciążeń, imperfekcji, wyników modułów ani kombinacji wyników (obwiedni).
- Nie czyta plików RFEM 6 (.rf6) ani RSTAB (.rs8 — ten sam kontener, inny strumień modelu).
- Układy rekordów są z inżynierii wstecznej: inna wersja RFEM 5 może zapisać inaczej — wtedy `Warnings` i liczby „0”.
  Dodając obsługę, porównaj plik z modelem w RFEM, nie zgaduj.

## Wiedza o RFEM z repozytorium CS (dopisane 2026-11-18)

Przy budowie zestawu umiejętności z repozytorium `CS-cheak_structures` (podział: `profile\cs_program\README.md`) dokumentacja
RFEM trafiła tutaj, żeby „Dlubal” był jedną umiejętnością: narzędzia `rfem_*` czytają plik `.rf5`, a `szukaj_wiedzy` odpowiada
o RFEM 6 (API II, `rfem_setup`, `rfem_pull`, `rfem_obwiednia`, pliki `.rf6`), RF-COM w RFEM 5 i lustro RFEM w CS.

| Plik | Co doszło |
|---|---|
| `zrodla.json` | docs/how-to/RFEM_API.md, RFEM_WYNIKI.md, RFEM5_COM.md, WYNIKI_3D_POMIAR_IGPU.md, tools/rfem_client/README.md, src/features/rfem_mirror/README.md z repozytorium CS (pobierane do `wiedza\zrodla\`, poza gitem) |
| `wiedza/rfem_dlubal.md` | 5 sekcji streszczenia w gicie |
| `system.md` | zasada 11: pytania o RFEM 6, RF-COM i zapis do RFEM idą przez `szukaj_wiedzy` |
| `karta.md` | sekcja „RFEM 6 i RF-COM w CS-check” |

Dokumentacja RFEM z repozytorium MKK (RFEM6_API, RFEM6_FORMAT_RF6, RFEM6_ZAPIS_RF6, RFEM5_COM) zostaje w `mkk`; obie umiejętności
można włączyć razem. Przykłady i testy nie zmienione (dotyczą poleceń na pliku).

## Otwarty RFEM 5 przez RF-COM (dopisane 2026-11-18)

Powód: pytanie „mam otwarty RFEM 5.27, jaki przekrój ma zaznaczony pręt?” nie miało odpowiedzi — umiejętność czytała tylko pliki .rf5.
Teraz te same tabele (i więcej: reakcje, wyniki bez zapisu pliku) idą z modelu otwartego w uruchomionym RFEM 5.

| Polecenie | Co robi |
|---|---|
| `rfem_otwarty` | co jest otwarte: wersja RFEM, nazwa i ścieżka modelu, liczby obiektów, przekroje z liczbą prętów, materiały, PO/KO/KW, czy są wyniki |
| `rfem_otwarty_pret` | pręt jak `rfem_pret` (typ, linia, węzły, długość, obrót, przeguby, przekrój z A, Iy, Iz, It i materiałem) |
| `rfem_otwarty_przekroje`, `rfem_otwarty_przypadki` | jak `rfem_przekroje`, `rfem_przypadki`; przy PO, KO i KW znacznik `[wyniki]` |
| `rfem_otwarty_sily` | siły z obliczeń: bez `obciazenie` ekstrema ze wszystkich kombinacji z wynikami (pętla po KO, limit `MaxLoadingsScanned`), z `obciazenie=KO5`/`PO1`/`KW1` przebieg (KW z kolumną „od”, która KO daje wartość); `co=przemieszczenia` ugięcia w osiach pręta |
| `rfem_otwarty_reakcje` | reakcje w węźle podporowym (osie globalne), z `obciazenie` wartości, bez — ekstrema |
| `rfem_wskaz` | `IView.PickObjects`: RFEM otwiera okno wyboru, użytkownik klika pręty (węzły, linie) i potwierdza OK; jeden pręt = pełny opis, kilka = lista |

Pliki umiejętności: `narzedzia.json` (+7 poleceń, razem 16), `system.md` (zasady 12–14: kiedy otwarty RFEM, „zaznaczony pręt” =
`rfem_wskaz`, błędy licencji), `karta.md` (sekcja „Otwarty RFEM 5”), `przyklady.jsonl` (+10, razem 30), `testy.jsonl` (F11–F15),
`wiedza/rfem5.md` (sekcja). Okno dopisuje do notatek sesji zdanie `RfemExecutor.LiveStatus()`: czy RFEM 5 jest zainstalowany
i uruchomiony, z tytułem okna (nazwą modelu), żeby model wiedział, czy pytać o plik, czy czytać otwarty RFEM.

### Jak działa (`LokalneAI.Harness.Rfem/Com`)
- RF-COM to interfejsy vtable po IUnknown, bez IDispatch — późne wiązanie jak w Office (`Type.InvokeMember`) ani `dynamic` nie działają
  (E_NOINTERFACE; to samo spotkało CS z pywin32). `Com/RfemCom.cs` jest **wygenerowany** z biblioteki typów „Dlubal RFEM Type Library v5.7”
  (zrzut kolejności metod przez comtypes na RFEM 5.27; generator w notatkach sesji, nie w repozytorium) jako `[ComImport]` interfejsy
  z dokładną liczbą miejsc w vtable: metody używane mają pełne sygnatury, reszta to zaślepki `_NNN_Nazwa`. Struktury rekordów
  (Member 128 B, CrossSection 144 B, MemberForces 88 B…) mają układ zmierzony na tej bibliotece; `RfemComUnitTests` sprawdza rozmiary,
  przesunięcia pól i liczbę miejsc.
- `RfemComLayout.Problem()` przed pierwszym użyciem ładuje zarejestrowaną bibliotekę typów (`LoadRegTypeLib`) i porównuje nazwy metod
  w próbce miejsc (`ITypeInfo.GetFuncDesc` → `oVft`); inna wersja RF-COM daje czytelny BŁĄD zamiast czytania złych bajtów.
- `RfemComSession`: `GetActiveObject("RFEM5.Application")` (podpięcie do otwartej sesji, nigdy nie uruchamia RFEM-a), `LockLicense()`
  (bez niego każde wywołanie wraca z CLASS_E_NOTLICENSED; `IsComLicensed()` kłamie — tak samo jak w CS), odczyt, `UnlockLicense()`
  w `Dispose`. Jedno polecenie = jedno połączenie w wątku STA (`RfemCom.RunSta`, limit `LiveTimeout` 120 s, dla `rfem_wskaz` `PickTimeout` 5 min).
- Tablice wyników (`SAFEARRAY` rekordów) czyta `Native.ReadRecords<T>` przez `SafeArrayAccessData` i `Marshal.PtrToStructure`,
  sprawdzając rozmiar elementu; `SafeArrayDestroy` zwalnia też BSTR-y w rekordach.
- CLR zamienia HRESULT-y RFEM-a na różne wyjątki („Obiekt nr 999999 nie został odnaleziony” to FileNotFoundException), więc łapie się
  `RfemCom.IsComFailure(e)`; w wyniku zostaje komunikat RFEM-a z kodem.

### Sprawdzone
- Sondy w Pythonie (comtypes) i testy `RfemComLiveTests` na .NET 10 i .NET Framework 4.8 na RFEM 5.27.01 z modelem biura (524 pręty,
  240 KO z wynikami): podsumowanie, pręt, przekroje, przypadki, siły od KO, ekstrema z 30 KO, przemieszczenia, reakcje, pręt, którego nie ma.
  Testy na żywo działają tylko ze zmienną `LOKALNEAI_RFEM_COM=1` i uruchomionym RFEM 5 (zajmują licencję na ułamek sekundy).
- `PickObjects` sprawdzony sondą: RFEM otwiera okno wyboru (pole z numerami, OK, Anulować) i czeka; Anuluj daje pustą listę.
  Kliknięcia w oknie wyboru nie dało się sprawdzić bez człowieka — pierwsza próba użytkownika pokaże, czy lista wraca w formacie „28,29”.

### Ograniczenia
- Zaznaczenia zrobionego wcześniej w RFEM nie da się odczytać (brak metody w RF-COM 5.7) — stąd `rfem_wskaz`.
- Wymaga licencji RF-COM w sesji RFEM i tego samego poziomu uprawnień (administrator / zwykły) dla RFEM-a i Amadeusza; przez czas
  polecenia okno RFEM jest zablokowane.
- Ekstrema „ze wszystkich” to pętla po kombinacjach (każda = 2 wywołania COM); przy 240 KO kilkanaście sekund; limit `MaxLoadingsScanned` (400).
- Nie czyta powierzchni, podpór (poza reakcjami), obciążeń ani wyników modułów (RF-STEEL); RFEM 6 to inna droga (API II, wiedza w tej umiejętności).
