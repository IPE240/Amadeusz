# Godziny: liczone, czekające i nakładka z kartami KP

Godziny pracy pochodzą z importu systemu vaibro (ostatni import 2026-10-02, miesiące do 2026-09). Wcześniej, do lipca 2023, część pracy zapisywano w kartach pracy KP. W oknie 2021-11 do 2023-07 te same godziny mogą występować w obu źródłach (nakładka).

- Godziny liczone (`hours_counted`) to wpisy, które wchodzą do sum: żywe, bez pary w kartach KP albo rozstrzygnięte jako liczone. To one odpowiadają na pytanie "ile przepracowano".
- Godziny czekające (`hours_pending`) to wpisy, dla których nakładka z kartami KP nie została jeszcze rozstrzygnięta. Nie wchodzą do sumy. Podawaj je osobno ("dodatkowo X h czeka na rozstrzygnięcie").
- `may_be_incomplete` = miesiąc do 2023-07, może być niepełny (część pracy tylko w kartach KP). Nie filtruj po tej fladze, najwyżej ostrzeż przy odpowiedziach obejmujących te lata.
- Miesiąc to pierwszy dzień miesiąca (`DATE '2026-08-01'`). Dane nie mają dni, tylko miesiące.
- Widoki: `timesheet.v_my_hours` (osoba x projekt x miesiąc x czynność; zawiera wszystkie osoby, więc zawsze filtr person_id; ma też zadania wewnętrzne biura, project_code NULL, entry_kind = 'internal') oraz `timesheet.v_hours_project_month` (projekt x miesiąc x czynność, wszystkie osoby razem, tylko praca na projektach, od 2021-11).
- Nieobecności (urlopy, L4) nie występują w tych widokach.
- Suma godzin projektu = SUM(hours_counted) z `v_hours_project_month`; godziny jednej osoby na projekcie = SUM(hours_counted) z `v_my_hours` z filtrem osoby.
