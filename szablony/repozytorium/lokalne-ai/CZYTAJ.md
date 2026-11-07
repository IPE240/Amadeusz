# lokalne-ai: opisy dla lokalnego asystenta

Folder dla lokalnych modeli (Bielik w Ollamie), które odpowiadają użytkownikom w programach biura.

| Ścieżka | Co to jest | Kto korzysta |
|---|---|---|
| `wiedza\*.md` | wiedza o programie <Nazwa programu>: funkcje, pojęcia, dane, typowe pytania | profile asystenta, które pobierają ją przez `zrodla.json` (wzorzec `lokalne-ai/wiedza/**/*.md`) |
| `profil\` | profil asystenta wbudowanego w ten program (`system.md`, `karta.md`, `narzedzia.json`, `przyklady.jsonl`, `testy.jsonl`) | sam program; przy budowaniu trafia do `profile\<nazwa>\` obok programu |

Folder `profil\` jest potrzebny tylko wtedy, gdy program ma własnego asystenta. Wzór pustego profilu: `szablony\profil`
w repozytorium Amadeusz.

Jak pisać notatki: `docs\wiedza-dla-modeli.md` w repozytorium Amadeusz. W skrócie:

- Po polsku. Jedna sekcja to jedno zagadnienie, do ok. 900 znaków.
- Nagłówek ze słowami, których ludzie używają w pytaniach.
- Konkret: liczby z jednostkami, nazwy jak w programie, przykładowe pytania.
- Bez danych osobowych i haseł.

Notatki aktualizuje zespół programu razem ze zmianą w kodzie.
