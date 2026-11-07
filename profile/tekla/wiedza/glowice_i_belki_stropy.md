# Głowice słupów, belki, stropy

## Głowica słupa
- Blacha czołowa na górze słupa (PL 15-30 mm, wymiar zbliżony do przekroju słupa plus 20-60 mm z każdej strony).
- Służy do oparcia belki lub płyty stropowej, do połączenia ze słupem wyższej kondygnacji (styk słupa) albo do zamknięcia słupa rurowego.
- Polecenie wstaw_glowice_slupa: blacha leży NAD słupem (dolna powierzchnia blachy na poziomie góry słupa), środek na osi słupa, spoina pachwinowa słup-blacha.

## Belki
- Rygiel: belka pozioma w ramie; płatew: belka dachowa; podciąg: belka główna; stężenie: pręt ukośny.
- Wstawiane poleceniem wstaw_belke przez dwa punkty (x1,y1,z1) i (x2,y2,z2); osie na środku profilu.
- Belka ukośna (stężenie): różne poziomy końców.
- Obrót profilu: parametr obrot_stopni przy wstawianiu lub polecenie obroc_element (obrót wokół osi belki).

## Strop betonowy
- Płyta prostokątna z betonu, wstawiana jako płyta konturowa. Górna powierzchnia na poziomie poziom_gora_m, grubość w dół.
- Typowe grubości: 120-250 mm; beton C25/30 lub C30/37.
- Strop stalowo-betonowy: belki stalowe pod płytą; płyta może leżeć na belkach (górna półka belki zwykle na poziomie dolnej powierzchni płyty) - uzgodnij poziomy z użytkownikiem.
- Selekcja: „strop na poziomie 3,53” = wyselekcjonuj typ plyta, poziom_m 3.53 (górna powierzchnia).

## Blacha
- wstaw_blache: prostokąt w planie, środek (x,y), polozenie gora/dol mówi, której powierzchni dotyczy poziom_m.
