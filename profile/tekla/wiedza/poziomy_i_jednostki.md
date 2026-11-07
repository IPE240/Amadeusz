# Poziomy, jednostki i współrzędne

## Zasada
- W Tekli jednostką długości jest milimetr; Z rośnie w górę.
- Użytkownik mówi poziomy w METRACH z przecinkiem: „3,53”, „-0,5”, „zero”, „+4,20”. W poleceniach katalogu parametry z końcówką `_m` są w metrach z kropką (3.53). Szablon mnoży je przez 1000.
- Współrzędne X i Y oraz wszystkie wymiary (blacha, grubość, średnica kotwy, przesunięcie) są w mm i mają końcówkę `_mm`.
- Kąty w stopniach (`_stopni`).

## Przeliczenia
| Użytkownik mówi | Parametr |
|---|---|
| poziom 3,53 | poziom_m = 3.53 |
| od zera | poziom_dol_m = 0 |
| poziom -0,5 | poziom_dol_m = -0.5 |
| punkt 6000, 0 | x_mm = 6000, y_mm = 0 |
| punkt 6 m, 3 m | x_mm = 6000, y_mm = 3000 |
| grubość 20 cm | grubosc_mm = 200 |
| tolerancja 1 cm | tolerancja_m = 0.01 |
| przesuń o 20 cm w górę | dz_mm = 200 |
| o metr w dół | dz_mm = -1000 |

## Który poziom?
- Słup: poziom dołu i góry to Z końców osi słupa.
- Belka: poziom to Z osi (osi, nie górnej półki).
- Strop (płyta betonowa): poziom stropu to GÓRNA powierzchnia płyty (wykończona konstrukcja, bez posadzki). Płyta wstawiona poleceniem wstaw_strop leży poniżej tego poziomu.
- Blacha: `polozenie` = gora lub dol mówi, której powierzchni dotyczy poziom.
- Przy selekcji `poziom_dotyczy` wybiera gora, dol albo srodek elementu.

## Kierunki
- „W prawo”, „w lewo” zależą od widoku. Przy przesunięciach używaj osi: +X, -X, +Y, -Y, góra (+Z), dół (-Z). Jeśli użytkownik mówi „w prawo” bez widoku - zapytaj o oś.
- Układ współrzędnych roboczych (plane) w Tekli może być przesunięty; szablony zawsze pracują w układzie globalnym.
