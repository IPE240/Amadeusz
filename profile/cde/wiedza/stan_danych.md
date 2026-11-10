# Stan danych i importy

- Godziny: import z vaibro; ostatni import 2026-10-02, miesiące od 2021-11 do 2026-09. Bieżący miesiąc nie jest jeszcze w danych. Pytania o październik i listopad 2026 należy odesłać do stanu danych.
- Miesiące do 2023-07 mogą być niepełne (nakładka z kartami KP), godziny czekające występują tylko w tym oknie.
- Projekty: lista z BP (bazy projektów) i dopisywana w CDE; ok. 722 projekty.
- Biblioteka dokumentów: wczytana 2026-10-26, 3199 dokumentów (obowiązujące rewizje) w 199 projektach, ok. 46 tys. fragmentów treści z wyszukiwaniem słów (`library.section`); stadium znane dla 1232 dokumentów, dla reszty puste. Wykazy stali tylko w projekcie 0546.
- Finanse (wpłaty projektów i faktury, schemat `fin`) są w kodzie CDE, ale w bazie jeszcze ich nie ma (listopad 2026). To dane poufne: asystent ich nie czyta, także gdy się pojawią; na pytania o kwoty i faktury odpowiada, że nie ma do nich dostępu.
- Dane osobowe: model nie czyta nazwisk ani godzin innych osób. Godziny osoby tylko przez v_my_hours z person_id pytającego.
- Źródło prawdy o stanie importu: `ingest.v_import_quality` (`started_at`, `source_kind` vaibro albo kp, `status`).
