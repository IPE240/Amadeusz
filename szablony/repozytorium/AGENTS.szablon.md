# AGENTS.md: <Nazwa programu>

Instrukcja dla agentów AI (Claude Fable, Claude Opus, Cursor i inni), którzy pracują w tym repozytorium.
Claude Code czyta ją przez `CLAUDE.md`. Wypełnij miejsca w `<…>` i usuń sekcje, które nie dotyczą programu.

## Co to jest

<Dwa, trzy zdania: co robi program, kto go używa, na czym działa, np. .NET Framework 4.8, Tekla 2019.1, baza CDE.>

## Mapa

| Ścieżka | Co to jest |
|---|---|
| `<Projekt>` | <jedno zdanie> |
| `lokalne-ai\` | opisy dla lokalnego asystenta (Bielik): `wiedza\` i ewentualnie `profil\` |

## Budowanie i testy

```powershell
<dokładne polecenia, które działają z katalogu repozytorium>
```

## Zasady, których nie wolno łamać

1. Nie testuj i nie skanuj dysku `A:` (udział sieciowy z danymi budowy). Pracuj na kopiach lokalnych.
2. Nie kasuj ani nie zastępuj istniejących plików bez pytania użytkownika.
3. Commit i push tylko na wyraźne polecenie użytkownika. W repozytorium nie może być haseł, nazwisk z bazy,
   godzin osób ani `person_id`.
4. <zasady tego programu z powodem, np. „nie pisz do folderu X, bo …”>

## Konwencje

- <język komentarzy i komunikatów, styl, gdzie dodawać nowe rzeczy>

## Pułapki

- <znane z doświadczenia, jedno zdanie każda>

## Opisy dla AI to część zmiany

Zasady: `docs/zasady-opisow-dla-ai.md` w repozytorium Amadeusz
(https://github.com/Optimal-Design-of-Structures/Amadeusz). W tym repozytorium oznacza to:

- **Zmiana budowania, testów albo zasad:** zaktualizuj ten plik.
- **Nowa albo zmieniona funkcja, o którą użytkownicy mogą zapytać asystenta:** notatka w `lokalne-ai\wiedza\`
  (po polsku; jedna sekcja to jedno zagadnienie, do ok. 900 znaków; nagłówek ze słowami z pytań).
- **Nowe polecenie asystenta:**
  - opis i parametry w `lokalne-ai\profil\narzedzia.json`;
  - przykłady w `przyklady.jsonl`, w tym dopytanie;
  - test w `testy.jsonl`;
  - kod w wykonawcy.

## Asystent w programie (biblioteka LokalneAI)

<Usuń, jeśli program nie ma asystenta.> Program osadza bibliotekę LokalneAI z repozytorium Amadeusz. Zanim ją
zmienisz albo użyjesz, przeczytaj `docs/biblioteki-dla-agentow.md` w tamtym repozytorium (w tym projekcie:
`<ścieżka submodułu>\docs\biblioteki-dla-agentow.md`).
