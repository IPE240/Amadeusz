# Profil `mkk`: programy Marka Kowolika (Hala, Modelarz węzłów) i programy obliczeniowe

Profil samej wiedzy dla lokalnego modelu: użytkownik pyta po polsku o obsługę Hali i Modelarza węzłów albo o to, jak
naprawdę działają Robot 2017, RFEM 5 / 6, IDEA StatiCa 25.1, IFC (Tekla) i SAP2000. Model szuka odpowiedzi narzędziem
`szukaj_wiedzy` w dokumentacji z repozytorium `Optimal-Design-of-Structures/MKK` i odpowiada z podaniem pliku i sekcji.
Nie ma żadnych poleceń gospodarza (`narzedzia.json` z pustą tablicą), więc niczego nie wykonuje; okno nie pokazuje dla tego
profilu opcji Tekli (`HarnessProfile.HasHostTools`).

Źródło: repozytorium MKK (stan 2026-11-16, commit `884e230`), README, `programy/*.md` (ok. 230 KB ustaleń o programach),
README Hali i Modelarza, opisy modułów węzłów EC3 (`kod/hala/tools/ec3_wezly/*.md`). Repozytorium jest prywatne i poufne
(LICENCE), dlatego profil bez pola `chmura` pracuje tylko z modelami lokalnymi.

## Pliki
| Plik | Co to |
|---|---|
| `profil.json` | `zakonczenie: model`, narzędzia wbudowane `szukaj_wiedzy` i `kalkulator`, `internet: true`, bez `chmura` |
| `system.md` | rola i zasady (ok. 430 słów): zawsze najpierw `szukaj_wiedzy`, bez dopowiadania z pamięci, źródło na końcu, rozróżnianie wersji, poufność |
| `karta.md` | ściąga (ok. 590 słów): co to Hala i Modelarz, wersje i drogi dostępu do programów, znaki i jednostki, mapa „pytanie → plik” |
| `narzedzia.json` | pusta tablica: profil nie ma poleceń |
| `przyklady.jsonl` | 16 przykładów: 13 wyszukiwań, 2 dopytania (wersja RFEM; prośba o wykonanie = odmowa z instrukcją), 1 kalkulator |
| `testy.jsonl` | 8 testów M01–M08 (`LokalneAI.Eval test`), w tym sesja dwukrokowa i oczekiwane dopytanie |
| `zrodla.json` | repozytorium MKK, gałąź `main`: README, `programy/*.md`, README Hali i Modelarza, `kod/hala/tools/ec3_wezly/*.md`, `wezly_typowe/INTERFEJS.md`, `rfem_client/README.md` oraz `lokalne-ai/wiedza/**/*.md` na przyszłość |
| `wiedza/hala.md` | 11 sekcji o Hali (uruchomienie, plik `.mkonH`, okno, geometria, stężenia, obciążenia, eksport do Robota, własny MES i wyniki, wymiarowanie, baza przekrojów, decyzje) |
| `wiedza/modelarz_wezlow.md` | 8 sekcji o Modelarzu (uruchomienie, pliki, okno, operacje, części z Hali i biblioteka IDEA, głos i rysik, obliczenia, warsztat) |
| `wiedza/programy_zewnetrzne.md` | 15 sekcji: mapa repozytorium, drogi przenoszenia, osie i znaki, jednostki, Robot (COM, wymiarowanie, `.rtd`), RFEM 6 (API, `.rf6`), RFEM 5, IDEA (REST, wyniki), IFC, SAP2000 |
| `wiedza/zrodla/MKK/` | pełna dokumentacja pobrana z repozytorium (poza gitem) |

Notatki w `wiedza/*.md` są streszczeniem z repozytorium MKK, żeby profil działał także na komputerze bez dostępu do
repozytorium; pełne szczegóły (nazwy metod, tabele, liczby) model znajduje w `wiedza/zrodla/MKK/`. Pełna dokumentacja ma
sekcje dłuższe niż 900 znaków, więc model widzi początek sekcji — dlatego `karta.md` i notatki mówią, w którym pliku i w którym
paragrafie (§) doczytać.

## Jak sprawdzić
```powershell
.\LokalneAI.Eval\bin\Debug\net10.0\LokalneAI.Eval.exe wiedza --profil profile\mkk                      # pobranie dokumentacji z repozytorium
.\LokalneAI.Eval\bin\Debug\net10.0\LokalneAI.Eval.exe szukaj --profil profile\mkk --fraza "RFEM 6 limit zapytań"
.\LokalneAI.Eval\bin\Debug\net10.0\LokalneAI.Eval.exe rozmiar --profil profile\mkk --model qwen3.5:9b-q4_K_M
.\LokalneAI.Eval\bin\Debug\net10.0\LokalneAI.Eval.exe test --profil profile\mkk --model qwen3.5:9b-q4_K_M --ctx 16384
.\LokalneAI.App\bin\Debug\net10.0-windows\LokalneAI.exe --harness mkk --autotest "Jak w Hali dodać stężenia dachowe?" --wynik C:\folder
```

`LokalneAI.Eval test` ocenia tylko polecenia gospodarza, a `szukaj_wiedzy` jest wbudowane, więc w tym profilu test sprawdza
jedynie, że model odpowiedział (nie zawiódł) albo dopytał tam, gdzie powinien (M07). Jakość odpowiedzi ocenia się w oknie
(zakładka Code, profil `mkk`, „Pokaż kroki modelu”) albo w `harness.json` z kontroli bez klikania.

## Decyzje
- **Bez `chmura`** (tylko modele lokalne): dokumentacja MKK jest wewnętrzna i poufna. Jeśli użytkownik zdecyduje inaczej,
  wystarczy dopisać `"chmura": ["anthropic"]` w `profil.json` (zasada biura: dane poufne tylko do Claude).
- **`internet: true`**: dokumentacja MKK odsyła do publicznych stron producentów (Dlubal, Autodesk, IDEA StatiCa);
  `system.md` zakazuje wysyłania fragmentów wiedzy do wyszukiwarki i każe oznaczać źródło zewnętrzne.
- **Jeden profil dla obu programów i dla programów zewnętrznych**, bo tak jest zbudowane repozytorium MKK (jedno README,
  wspólne `programy/`). Gdyby Hala dostała własnego asystenta z poleceniami, powstanie osobny profil z `narzedzia.json`.
- **Wzorzec `lokalne-ai/wiedza/**/*.md`** w `zrodla.json` już jest: gdy Marek doda w MKK folder `lokalne-ai\wiedza\`
  (szablon: `szablony\repozytorium`), notatki trafią do profilu bez zmian w Amadeuszu.
- Duplikaty pominięte: `kod/modelarz_wezlow/modelarz/ec3_wezly/INTERFEJS.md` i `wezly_typowe/INTERFEJS.md` są kopiami
  plików z `kod/hala/tools/`.

## Sprawdzone 2026-11-17
- `dotnet test`: 63 + 238 + 238 testów przechodzi (w tym `ProfileFilesTests` z `mkk`, 0 narzędzi gospodarza).
- `LokalneAI.Eval wiedza`: 25 plików z MKK pobrane do `wiedza\zrodla\MKK\`; indeks ma 411 fragmentów.
- `LokalneAI.Eval rozmiar` (qwen3.5:9b-q4_K_M): polecenie systemowe 4161 tokenów, mieści się w kontekście 16384.
- Kontrola okna bez klikania (`--harness mkk`, qwen3.5:9b-q4_K_M): „Jak w Hali dodać stężenia dachowe?” → `szukaj_wiedzy`
  i poprawna odpowiedź z notatki (20 s); „Jaki znak ma siła osiowa w Robocie, a jaki w RFEM?” → poprawna odpowiedź z karty
  bez wyszukiwania (6 s); „Policz halę w Robocie.” → odmowa z instrukcją (9 s). Model nie podał źródła (plik, sekcja),
  choć `system.md` tego wymaga — do poprawy przykładami albo w rozmowie z użytkownikiem.

## Czego nie sprawdzono
- Odpowiedzi modeli w chmurze (profil ich nie dopuszcza).
- Pytań o treść modułów EC3 (`API_*.md` w `ec3_wezly`) na żywym modelu; indeks je obejmuje, ale przykładów w `przyklady.jsonl` nie ma.
