# Umiejętność `tablice`: tablice do projektowania konstrukcji stalowych

Profil samej wiedzy (bez poleceń gospodarza): model szuka w `wiedza\` przez `szukaj_wiedzy`, liczy `kalkulatorem` i odpowiada.
Trzy źródła, wszystkie z plików PDF użytkownika (skany OCR-owane skryptami, bez modelu):

| Źródło | Co jest w wiedzy | Pliki |
|---|---|---|
| Bogucki, Żyburtowicz, *Tablice do projektowania konstrukcji metalowych*, wyd. 7 (Arkady 2007) | rozdział 1, tablice 1.1–1.31: dwuteowniki (I, IPE, IPE AA, IPE o/v, HEB, HEC, HEA, HEM, HEAA, IPE 750, HL, UB, JIS, HE ciężkie, HD, HP), połówki 1/2 I i 1/2 IPE, ceowniki C, CE, UPE, UAP, kątowniki równo- i nierównoramienne, teowniki T, rury okrągłe, kwadratowe i prostokątne — 1227 przekrojów; te same przekroje rodzinami po kilkanaście w sekcji do porównań i rankingów | `zyburtowicz_*.md` (jedna sekcja na przekrój), `zyburtowicz_zestawienia.md` (sekcja = kilkanaście przekrojów rodziny), `dane\zyburtowicz_t*.json`, `zyburtowicz_uwagi.md` |
| DAST *Typisierte Anschlüsse* (Prüfbescheid II B 2-543-778, 2002) — połączenia doczołowe IH1–IH4 | 859 połączeń × 4 zestawy (S235/S355 × 8.8/10.9): nośności Mj,1,Rd, Mj,2,Rd, Vz,Rd wg DIN i EC 3, Mc,y,Rd, wymagany słup dla 100/80/60 %, wymiary blachy i śrub, spoiny, sztywności Sj,ini | `dast_<stal>_<śruby>_<rodzina>.md` (32 pliki), `dane\dast.json` |
| SZS C5 *Konstruktionstabellen* (2005/2012) | śruby SBS/SHV (wymiary, otwory, A, As), nośności Fv,Rd, Ft,Rd, Fs,Rd, Fb,Rd, rozstawy, sprężanie HV, otwory podłużne, połączenia dwoma kątownikami, śruby duże, spoiny pachwinowe (wzory, klasy jakości), nośności przekrojów IPE/HEA/HEB/HEM wg SIA 263, stale (fy, fu, grupy jakości), tolerancje walcowania, skróty SZS, wzory geometryczne | `szs_c5_*.md`, `dane\szs_c5_polaczenia.json`, `dane\szs_c5_nosnosci_przekrojow.json` |

## Pliki umiejętności
| Plik | Co to |
|---|---|
| `profil.json` | `zakonczenie: model`, `szukaj_wiedzy` + `kalkulator`, bez `chmura`, bez internetu |
| `system.md` | rola i 10 zasad: liczby tylko z wiedzy, nazwa przekroju jak w tablicach, jednostki i podstawa, osie, rachunki kalkulatorem, „wartości niepewne — OCR”, DIN/EC3 w DAST, SIA 263 w C5 |
| `karta.md` | ściąga: nazwy przekrojów do szukania, jednostki, typy IH, stany graniczne (EPB, BT, BFC, BWT), skróty C5 |
| `narzedzia.json` | pusta tablica (profil wiedzy) |
| `przyklady.jsonl` | 20 przykładów (szukanie po nazwie, połączenie DAST, rozstawy, sprężanie, spoina, brak wołania przy wymiarowaniu) |
| `testy.jsonl` | 10 testów B01–B10 (`LokalneAI.Eval test`) |
| `wiedza\CZYTAJ.md` | jak są zbudowane pliki wiedzy i jak je poprawiać |

## Jak zbudowano wiedzę (sesja 2026-11-18)
1. **Żyburtowicz** (skan 417 stron bez warstwy tekstowej): strony renderowane PDFium w 300 dpi, OCR Tesseract 5 (PSM 6, `pol+eng`,
   TSV z położeniami słów). Parser tablic (`zyb_parse.py` w sesji): kolumny z klastrów położeń x, schemat kolumn na tablicę,
   naprawa zgubionych przecinków z zakresów kolumn, kontrola zależności m = 0,785·A, i = √(I/A), Wy = 2Iy/h (±3 %), rury okrągłe
   policzone z D i t. Wiersze niespełniające kontroli obejrzano na wycinkach skanu i poprawiono ręcznie (`zyb_fix.json` — 51 poprawek w 20 tablicach,
   w tym 3 błędy druku książki: HE 450x368 iz, HE 550x393 Wy, UPE 400 iy). 33 wiersze zostały oznaczone „wartości niepewne — OCR”
   (głównie rury okrągłe, gdzie OCR nie dał się potwierdzić). Druga kontrola (`zyb_sanity.py`): grubości i promienie względem h i b
   (tw ≤ 0,5·b i 0,2·h, tf ≤ 0,25·h, t ≤ 0,35·a, R ≤ 0,4·b) — 38 zgubionych przecinków w tw, tf, R (I, IPE 100, HEB, HEC, HEA,
   HEAA, HP) poprawionych przez /10, odnotowanych w polu `sprawdzono` JSON-a.
   Zestawienia rodzin (`tablice_zestawienia.py` → `zyburtowicz_zestawienia.md`): 240 sekcji po kilkanaście przekrojów (≤ 900 znaków,
   bo tyle zwraca `szukaj_wiedzy`), żeby porównanie albo ranking całej rodziny wymagało jednego wywołania z `ile` = 4–8 zamiast
   trzech przekrojów na wywołanie.
2. **DAST** (697 stron, skan z gotową warstwą tekstową): warstwa tekstowa czytana `pdfplumber` (układ wierszy), drugi odczyt
   Tesseractem; wartość przyjęta, gdy zgadzają się co najmniej dwa z czterech odczytów (wiersz główny i wiersz 100 % z obu źródeł),
   inaczej w tekście stoi „UWAGA: odczyt OCR niepotwierdzony”. Profil belki z numeru połączenia (zakresy ze spisu treści) i głosowania
   między czterema zestawami stali/śrub. Stan: 3335 połączeń, 2880 z nośnościami (997 potwierdzonych w całości), 3001 z wymiarami
   (w większości z jednego odczytu). Wartości niepotwierdzone trzeba sprawdzić w katalogu przed użyciem.
3. **SZS C5** (PDF z dobrą warstwą tekstową): tablice połączeń i nośności przekrojów przepisane z warstwy tekstowej (ręcznie do JSON,
   nośności przekrojów skryptem), część opisowa (materiały, śruby, spoiny, tolerancje) streszczona po polsku.

Skrypty budujące leżały w folderze tymczasowym sesji; JSON-y w `wiedza\dane\` są źródłem prawdy — po poprawce w JSON trzeba
wygenerować `.md` ponownie (jedna sekcja `#` na przekrój albo połączenie, tak dzieli je `KnowledgeIndex`).

## Sprawdzone
- `ProfileFilesTests` (tablice, 0 poleceń), `Eval rozmiar`: polecenie systemowe ~3100 tokenów (Bielik 11B: 5231 tokenów wejścia z pytaniem) —
  przy kontekście 8192 zostaje mało miejsca na fragmenty wiedzy, zalecany kontekst 16384.
- Nie sprawdzono z prawdziwym modelem (`LokalneAI.Eval test --profil profile\tablice` wymaga Ollamy).

## Ograniczenia
- Tylko rozdział 1 Żyburtowicza (przekroje); bez rozdziałów o blachach, szynach, zestawach, wyrobach giętych, łącznikach.
- DAST: nośności i wymiary z OCR — wartości oznaczone jako niepotwierdzone mogą mieć przekręcone cyfry; brak rysunków typów.
- C5: wartości wg SIA 263 (γM2 = 1,25), nie wg PN-EN 1993-1-8 — do projektowania wg Eurokodu traktować jako orientacyjne.
- Książki są objęte prawem autorskim: wiedza służy pracy biura na własnym komputerze, bez chmury (brak pola `chmura`).
