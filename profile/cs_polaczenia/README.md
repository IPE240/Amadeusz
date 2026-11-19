# Umiejętność `cs_polaczenia`: połączenia w CS-check

Część zestawu umiejętności z repozytorium CS (podział i decyzje: `profile\cs_program\README.md`). Zakres: śruby i otwory,
strefy i typy połączeń, wspólne krawędzie i przydzielanie spoin, spoiny rzeczywiste ze źródeł, sztywność i katalog połączeń,
IDEA StatiCa (REST API, siły z RFEM, dopasowanie węzłów). To odpowiedź na „przydzielanie spoin” z zamówienia użytkownika.

## Pliki
| Plik | Co to |
|---|---|
| `profil.json` | profil wiedzy, bez `chmura`, bez internetu |
| `system.md` | rola i 7 zasad, w tym rozróżnienie spoiny przewidzianej od rzeczywistej i granica „CS nie liczy nośności” |
| `karta.md` | ściąga: śruby, strefy i numeracja SP/P/GP, typy połączeń z progami, krawędzie i spoiny, źródła i pewność spoin, sztywność, IDEA, mapa „pytanie → plik” |
| `przyklady.jsonl` | 10 przykładów (9 wyszukiwań, 1 odmowa liczenia nośności) |
| `testy.jsonl` | 6 testów CL01–CL06 |
| `zrodla.json` | 25 wzorców: glossary/polaczenia i spoiny, README bolt, connection_set, strefa_polaczenia, shared_edges, spoiny, idea_match, how-to IDEA_API i RFEM_DO_IDEA_SILY, pitfalls rfem_idea_sily i bolt_spoiny_polaczenia, 6 wykładów, 2 plany łuków, tools/spoiny |
| `wiedza/polaczenia_cs.md` | 9 sekcji streszczenia w gicie |

## Uwagi
- Nośność i CBFEM to IDEA StatiCa; wzory EC3 dla typowych połączeń ma Modelarz węzłów (umiejętność `mkk`). `system.md` kieruje tam pytania o nośność.
- `handbook/pitfalls/rfem_idea_sily.md` i `bolt_spoiny_polaczenia.md` to jedyne pliki pułapek pobierane przez zestaw CS (pozostałe dotyczą programowania).
