# Czynności (rodzaje pracy)

Słownik `ref.activity` (21 czynności): mod model 3D (Tekla), obl obliczenia statyczne, rys dokumentacja rysunkowa, rew rewizje, spr sprawdzanie, org organizacja, ops opisy techniczne, ofe oferta, druk drukowanie i wysyłka, del delegacje i spotkania, eks ekspertyza, prg programowanie, utk ustawienia Tekli, kap katalog połączeń, sof testowanie programów, ark partner zewnętrzny, hwc prace nieokreślone, adm administracja, wdr wdrożenia, prom promocja, szk szkolenia.

Czynność jest w `activity_code` (NULL = nieznana, np. węzeł zakresu w vaibro albo wpisy sprzed 2019). W szablonie vaibro główne czynności to: 01 obliczenia statyczne (obl), 02 model 3D Tekla (mod), 03 dokumentacja rysunkowa (rys). W vaibro rewizje, sprawdzanie i opisy występują rzadko, więc porównania między okresami najlepiej robić na grupach (`activity_group`).

Grupy: modelowanie, obliczenia, rysunki, rewizje, sprawdzanie, koordynacja (org), dokumentacja (ops), ofertowanie, wydanie, teren, ekspertyza, rozwoj, administracja, promocja, nieokreslone. W widoku projektów jest nazwa (`activity_name`), w `v_my_hours` trzeba dołączyć `ref.activity`.
