# Dokumenty projektów w CDE

## Dokumenty, rewizje, stadia, kategorie: gdzie szukać

Biblioteka dokumentów (schemat `library`) to opisy techniczne, obliczenia, założenia i wytyczne z teczek projektów
(pliki Word przekonwertowane do tekstu). Jeden dokument ma kilka rewizji. Obowiązującą pokazuje
`library.v_document_current` (jeden wiersz = dokument), wszystkie rewizje `library.v_document_history`
(`rev_order` 1 = najstarsza, `is_current` = obowiązuje). Stadium (`stage_code`): PB, PW, PV, EP albo NULL — nieznane
dla ok. 60% dokumentów. Kategoria treści (`content_category`): realizacja, zalozenia, obliczenia, wytyczne,
dokumentacja albo NULL. Najwięcej dokumentów mają projekty 0621 EDO, 0837 SPW i 0757 ZAK2.
Przykładowe pytania: „jakie dokumenty PW ma projekt SPW?”, „ile rewizji miał opis techniczny?”.

## Szukanie w treści dokumentów: słowa, odmiana, kotwy, spawy, śruby

Treść jest pocięta na fragmenty pod nagłówkami: `library.section` (`heading_path`, `body`, `tsv`). Indeks `tsv`
(słownik `simple`) nie zna odmiany, synonimów ani literówek. Dlatego szukaj po początku słowa z `:*`:
`to_tsquery('simple', 'kotw:*')` znajdzie kotwa, kotwy, kotwami; dopełniacz „kotew” dopisz przez `|`:
`'kotw:* | kotew:*'`. Kilka słów: `|` (lub); `&` wymaga obu słów w jednym fragmencie i zwykle nic nie znajduje.
Przykład:
`SELECT d.project_code, d.title, s.heading_path, left(s.body, 400) AS fragment FROM library.section s
JOIN library.v_document_current d ON d.sha256 = s.sha256 WHERE d.project_code = '0757'
AND s.tsv @@ to_tsquery('simple', 'spaw:*') LIMIT 10`.

## Dokumenty: czego nie robić, pusty wynik szukania

- Nie czytaj całych treści (`text_md`): są za długie. Bierz fragmenty `library.section` z `LIMIT`.
- Fragmenty łącz zawsze przez `library.v_document_current` — inaczej trafią się stare rewizje.
- Nie podawaj autorów ani sprawdzających (`author_raw`, `checker_raw`, nazwiska w treści).
- Pusty wynik szukania nie znaczy, że tematu nie ma w projekcie: dokument mógł nie zostać wczytany albo używa innego
  słowa (np. „zakotwienie” zamiast „kotwa”: `'zakotw:* | kotw:*'`). Powiedz, jakich słów szukałeś.
