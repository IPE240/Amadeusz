# Szablony

| Folder | Do czego | Jak użyć |
|---|---|---|
| `profil\` | nowy profil asystenta z dwoma przykładowymi poleceniami (`wstaw_slup`, `policz_elementy`) | skopiuj do `profile\<nazwa>\` (tu) albo do `lokalne-ai\profil\` w repozytorium programu; zmień nazwę i opis w `profil.json`, a przykładowe polecenia zastąp swoimi |
| `repozytorium\` | pliki dla repozytorium programu biura: `AGENTS.szablon.md`, `CLAUDE.szablon.md`, `lokalne-ai\` | skopiuj do katalogu głównego repozytorium, zmień nazwy na `AGENTS.md` i `CLAUDE.md` i wypełnij miejsca w `<…>` |

Szablony `AGENTS` i `CLAUDE` mają tu inną nazwę, żeby agenci AI nie brali ich za instrukcje dla tego folderu.

Szablon profilu wczytuje się bez błędów (pilnuje tego test `DokumentacjaTests`) i służy też projektowi
`narzedzia\sprawdz_przyklady`, więc po zmianie formatu profilu popraw go razem z `docs\profile.md`.

Zasady: `docs\zasady-opisow-dla-ai.md` (kiedy pisać opisy) i `docs\wiedza-dla-modeli.md` (gdzie i jak).
