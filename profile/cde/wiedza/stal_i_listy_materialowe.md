# Stal i listy materiałowe

Konstrukcje stalowe mierzy się w tonach stali (t), osobno dla każdego gatunku (S235, S355, 16Mo3 itd.). Gatunki w bazie mają pełne oznaczenia (S235JR, S355J2, 16Mo3), więc szukaj przez ILIKE 'S355%'.

Źródło: wykazy stali (OMIS: lista materiałowa, lista elementów, lista zespołów) wczytane do biblioteki dokumentów (`library`). Masa pozycji = liczba sztuk x masa jednej sztuki.

Reguła poprawnego liczenia ton:
1. Używaj `library.v_steel_by_project`: bierze tylko wykazy liczone (bez dubli), tylko obowiązujące rewizje dokumentów i tylko najdokładniejsze stadium z danymi (EP > PV > PW > PB).
2. Nie sumuj stadiów: ta sama stal występuje w PB, PW, EP. `v_steel_stage_coverage` pokazuje wszystkie stadia, a flaga `used_in_summary` zaznacza to, które idzie do podsumowania (to samo co `v_steel_by_project`).
3. Wykazy są powielane (inny widok tej samej listy, wersja, podzbiór); `v_bom_lists.counted = false` oznacza dubel. Nie sumuj `v_bom_lists.mass_t` bez filtra counted.
4. Gdy `several_lists_in_one_scope` = true, w jednym zakresie nadal jest kilka wykazów i wynik wymaga przeglądu człowieka; wspomnij o tym.
5. Różne gatunki podawaj osobno; suma wszystkich gatunków jest dopuszczalna jako "razem" (ROUND(SUM(mass_kg)/1000, 3)).

Stan: wykazy stali wczytano tylko dla projektu 0546 (3 gatunki, ok. 91,6 t razem, stadium PW; 22 wykazy, w tym 2 liczone i 20 dubli). Dla innych projektów odpowiedź brzmi "brak wykazów stali w bazie".
