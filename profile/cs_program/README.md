# Umiejętność `cs_program`: obsługa CS-check (Check Structures)

Pierwsza z zestawu umiejętności zbudowanych 2026-11-18 z repozytorium `Optimal-Design-of-Structures/CS-cheak_structures`
(prywatne; stan `740c693`, 8433 pliki, ponad 1000 plików Markdown). Repozytorium podzielono tematycznie na siedem
umiejętności samej wiedzy, a dokumentację RFEM dołożono do istniejącej umiejętności `rfem`:

| Umiejętność | Zakres | Główne źródła w repo CS |
|---|---|---|
| `cs_program` (ta) | instalacja, viewer, toolkit, teczki, wersje, serwisy, eksporty, `.csx`, koszty, agent, co CS umie | README, ODS.md, how-to/INSTALACJA i TROUBLESHOOTING, docs/help, handbook/ODS_MOZLIWOSCI, tools/README |
| `cs_ifc` | import IFC, geometria (bliźniak analityczny), jednostki, psety, katalog i nazwy profili, ocena IFC | docs/reference/IFC_GEOMETRY, PSET_SEMANTICS, UNITS, handbook/profile_naming, pret/blacha/export |
| `cs_konstrukcja` | klasyfikacja (klasa_p1..p4, role, typy zespołów), zespoły, osie i poziomy, płaszczyzny, obiekty i dylatacje | handbook/glossary, interpretacja, cs1–cs4, axes_levels, podzial_obiektow, wykłady |
| `cs_polaczenia` | śruby, strefy i typy połączeń, wspólne krawędzie i spoiny, spoiny ze źródeł, sztywność, IDEA StatiCa | glossary/polaczenia i spoiny, bolt, strefa_polaczenia, shared_edges, spoiny, how-to/IDEA_API, wykłady |
| `cs_statyka` | model obliczeniowy MES z IFC, magazyn wyników, obciążenia (śnieg, obudowa), porównanie modeli, wyniki 3D | mes, static, loads, model_compare, results3d, glossary/obliczenia, wykłady MES i obudowa |
| `cs_rysunki` | układy (rysunki płaszczyzn), arkusze zespołów, części, NC DSTV, nesting | plane_view, assembly_view, part_view, nc_dstv, wykłady o układach i rysunkach warsztatowych |
| `cs_robot` | Autodesk Robot przez COM, wytężenie z Robota na IFC, most MES → Robot, RFEM → Robot | docs/reference/ROBOT_COM_API, how-to/ROBOT_DO_IFC_WYTEZENIE, mes_robot, tools/rfem_to_robot |
| `rfem` (istniejąca) | plus: RFEM 6 API II, obwiednie, RFEM 5 RF-COM, lustro RFEM w CS | how-to/RFEM_API, RFEM_WYNIKI, RFEM5_COM, rfem_client, rfem_mirror |

Każdy plik repozytorium jest w dokładnie jednej umiejętności, więc profil złożony z kilku z nich (Ustawienia → Profile)
nie dubluje fragmentów w `szukaj_wiedzy`. Pominięto celowo: `docs/plans/` (469 plików historii planów i raportów),
`docs/DECYZJE*.md`, `handbook/ODS_dziennik/` (notatki sesji), `handbook/pitfalls/` (pułapki programistyczne, poza
`rfem_idea_sily.md`), `handbook/ai_pitfalls*.md`, `viewer/` i `projekty/<numer>/` (dane projektów).

## Pliki
| Plik | Co to |
|---|---|
| `profil.json` | `zakonczenie: model`, `szukaj_wiedzy` + `kalkulator`, bez `chmura` (repozytorium wewnętrzne), bez internetu |
| `system.md` | rola i 7 zasad: najpierw `szukaj_wiedzy`, bez dopowiadania nazw narzędzi i zakładek, polecenia w bloku kodu, trzy drogi obsługi (viewer, toolkit, Claude Code), źródło na końcu, bez nazw klientów |
| `karta.md` | ściąga: co to CS, uruchomienie, projekty i pliki, analiza, eksporty, mapa „pytanie → plik” |
| `narzedzia.json` | pusta tablica (profil wiedzy) |
| `przyklady.jsonl` | 10 przykładów (9 wyszukiwań, 1 odmowa z instrukcją) |
| `testy.jsonl` | 6 testów CP01–CP06 (`LokalneAI.Eval test` sprawdza tylko, że model odpowiedział albo dopytał) |
| `zrodla.json` | 45 wzorców plików z repozytorium CS oraz `lokalne-ai/wiedza/**/*.md` na przyszłość |
| `wiedza/obsluga_cs.md` | 12 sekcji streszczenia w gicie |

## Jak sprawdzić
```powershell
.\LokalneAI.Eval\bin\Debug\net10.0\LokalneAI.Eval.exe wiedza --profil profile\cs_program
.\LokalneAI.Eval\bin\Debug\net10.0\LokalneAI.Eval.exe szukaj --profil profile\cs_program --fraza "serwisy obliczeniowe Dolicz"
.\LokalneAI.Eval\bin\Debug\net10.0\LokalneAI.Eval.exe test --profil profile\cs_program --model gemma4:12b --ctx 16384
```

## Decyzje
- **Bez `chmura`, bez internetu**: repozytorium CS jest prywatne, a dokumentacja zawiera nazwy modeli i ustalenia biura.
- **Jedna umiejętność na temat, nie na folder repozytorium**: użytkownik pyta „jak zrobić zestawienie”, nie „co jest w docs/help”.
- **Profil domyślny „Check Structures”** (Ustawienia → Profile) łączy `cs_program`, `cs_ifc`, `cs_konstrukcja`, `cs_polaczenia`
  i `cs_statyka`; `cs_rysunki`, `cs_robot` i `rfem` dokłada się ręcznie, bo każda rola powiększa polecenie systemowe.
