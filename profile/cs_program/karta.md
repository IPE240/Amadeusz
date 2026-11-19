# Karta: CS-check (Check Structures), obsługa programu

## Co to jest
CS-check = analiza konstrukcji stalowych z IFC (Tekla, Advance Steel, Revit): import, klasyfikacja elementów (pręt, blacha, łączniki, spoiny), zespoły, połączenia, podobieństwo, układy (rysunki płaszczyzn), zestawienia, nesting, koszty, eksport wzbogaconego IFC. Nie liczy statyki (obliczenia zostają w RFEM/Robot), ale czyta ich modele i wyniki. Repozytorium `Optimal-Design-of-Structures/CS-cheak_structures` (prywatne), gałąź `main` = najnowsza wersja.

## Uruchomienie
- Instalacja: `INSTALUJ.bat` (albo `/instaluj` w Claude Code) — 10–20 min, bez administratora, przenośne Python 3.12, Node 22, PostgreSQL 15 w katalogu repo; `git pull` + `INSTALUJ.bat` doinstalowuje.
- Praca: `URUCHOM.bat` → karta startowa → viewer 3D `http://localhost:5000` (okno bazowe: projekty, modele, 3D, Układy, eksport) i toolkit `http://localhost:8000` (Data group z panelem Serwisy obliczeniowe, Table, Export, Statyka, Nesting, Assembly, Koszty, Jobs).
- Claude Code w katalogu CS: `przeczytaj ODS.md`; `/nowy-projekt 1102_FSM_Pełna nazwa`; `/szkic`; `zapisz to jako receptę` (handbook/ODS_recepty.md).

## Projekty i pliki
- Teczka projektu = projekt w viewerze: `projekty/1102_FSM/` z `README.md`, `ifc/` (plik wrzucony = nowa wersja bez analizy; „Analizuj w CS” klikasz sam), `snapshots/` (RFEM), `skrypty/`, `eksport/`. Komentarz rewizji: `<plik>.ifc.rewizja.txt`. Korzeń teczek: zmienna `CS_PROJEKTY`.
- Nazwa: `numer_SYMBOL_Pełna nazwa`; numery 9000–9999 = projekty testowe; `0000_NPR_Nieprzypisane`.
- Wersje v1, v2… nadaje program, nigdy nie giną; zmiana = kto, czym, dlaczego; „Cofnij do vN” i „Cofnij tę zmianę”. Dziennik projektu = historia zdarzeń, tylko dopisywany.
- Paczka projektu `.csx` („Wynieś projekt”: project / archive / client; „Wczytaj projekt”) przenosi projekt między stanowiskami bez liczenia.

## Analiza
- Serwisy obliczeniowe: 7 grup (Wczytanie i podział, Klasyfikacja, Pręty i blachy, Zespoły, Połączenia śrubowe, Spoiny, Układ konstrukcji) ≈ 35 modułów; Licz / Dolicz / Licz od nowa / Przerwij; kilka modeli naraz. Koszty, rysunki, model obliczeniowy i rozkrój liczą się na żądanie.
- Czas: typowy model 7 000 elementów ≈ 3 min importu; WSP ≈ 6–7 min.

## Eksporty i narzędzia
- Okno „Eksport…” w viewerze: IFC z psetami (preset Standardowy = profil `user`), Układy do sceny, Połączenia (strefy i osie), Spoiny (nakładka), Dane prętów (osie DXF), Paczki `.csx`; dwa czasowniki: „Zapisz plik” i „Pokaż w viewerze”.
- Co CS umie (MA / PRAWIE / NIE): `handbook/ODS_MOZLIWOSCI.md`; narzędzia: `tools/README.md`.
- Koszty („Skala Kozieła”): koszt własny wytwórni [zł/kg], tryby Porównawczy i Produkcyjny, cennik i park maszynowy.
- Okno agenta (✦ Agent AI, Ctrl+Alt+I): agent steruje programem; zmiany danych wymagają potwierdzenia „Agent chce: …”.

## Gdzie szukać (pliki w repo CS)
| Pytanie o… | Plik |
|---|---|
| start pracy projektanta, pięć pierwszych poleceń, RFEM/Robot/IDEA w skrócie | ODS.md |
| instalacja, co gdzie ląduje, parametry | docs/how-to/INSTALACJA.md |
| gdy coś nie działa | docs/how-to/TROUBLESHOOTING.md |
| co CS umie i jak użyć | handbook/ODS_MOZLIWOSCI.md |
| viewer, projekty, wersje, eksport, serwisy, koszty, obciążenia, układy | docs/help/*.md |
| lista narzędzi `tools/` | tools/README.md |
| czego CS nie robi | handbook/limity.md |
| przebieg pracy IFC → analiza → eksport | handbook/workflow.md |
