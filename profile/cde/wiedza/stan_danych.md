# Stan danych i importy

- Godziny: import z vaibro; ostatni import 2026-10-02, miesiące od 2021-11 do 2026-09. Bieżący miesiąc nie jest jeszcze w danych. Pytania o październik i listopad 2026 należy odesłać do stanu danych.
- Miesiące do 2023-07 mogą być niepełne (nakładka z kartami KP), godziny czekające występują tylko w tym oknie.
- Projekty: lista z BP (bazy projektów) i dopisywana w CDE; ok. 722 projekty.
- Biblioteka dokumentów: wczytana 2026-10-26, ok. 3199 dokumentów (obowiązujące rewizje), 203 teczki; stadium znane dla ok. 1230 dokumentów, dla reszty puste. Wykazy stali tylko w projekcie 0546.
- Dane osobowe: model nie czyta nazwisk ani godzin innych osób. Godziny osoby tylko przez v_my_hours z person_id pytającego.
- Źródło prawdy o stanie importu: `ingest.v_import_quality` (`started_at`, `source_kind` vaibro albo kp, `status`).
