# Modelarz węzłów — modelowanie połączeń stalowych (program Marka Kowolika)

## Co to jest Modelarz węzłów, do czego służy, jak uruchomić (MODELARZ.bat)
Modelarz węzłów to samodzielny program do modelowania połączeń stalowych „jak w IDEA StatiCa Connection 25.1, ale szybciej”:
operacje programu mają geometrię od razu (bez czekania na IDEA), śruby układają się same wg PN-EN 1993-1-8, wymiary można
wziąć z katalogów DAST IH i MOSTOSTAL Z1, a blachy zmienia się przeciągając uchwyty w 3D. Zapis do IDEA (`.ideaCon`),
obliczenia CBFEM, notka i pełny przegląd wyników są w IDEA StatiCa (licencja). Kod: `kod/modelarz_wezlow/` w repozytorium MKK.
Uruchomienie: raz `INSTALUJ.bat` (Python 3.12, `.venv`), potem `MODELARZ.bat` (można przeciągnąć na niego plik `.mkwez`
albo `.ideaCon`). Do AI (polecenia pisane i dyktowane) potrzebna jest aplikacja Claude Code zalogowana na komputerze.

## Pliki Modelarza: .mkwez, .ideaCon, zapis do IDEA w trybie natywnym
Projekt węzła to plik `.mkwez`. Zapis do IDEA tworzy `.ideaCon`; domyślnie w **trybie natywnym**: blachy, śruby, spoiny
i cięcia trafiają do IDEA jako zwykłe operacje, więc plik da się dalej edytować w IDEA. Gdy IDEA odrzuci szablon, program
zapisuje części jak dawniej (import) i pokazuje uwagę. `.ideaCon` jest zaszyfrowanym formatem IDEA: program zapisuje go
i czyta wyłącznie przez oficjalne API IDEA (licencja). **⟲ Przemodeluj**: węzeł otwarty z `.ideaCon` (np. z Hali albo
zrobiony w IDEA) jest odtwarzany operacjami programu i dalej można go zmieniać głosem i myszą.

## Okno Modelarza: wstęga, panel Węzeł, widok 3D, uchwyty, właściwości
**Wstęga**: Projekt (nowy, otwórz, zapisz, .ideaCon, przykłady, cofnij / ponów), Pręty (belka, słup, stężenie), Operacje
programu, Biblioteka IDEA, Widok, IDEA StatiCa (podgląd dokładny, policz, notka, otwórz w IDEA), EC3 — bez IDEA, Warsztat.
**Lewy panel — Węzeł**: pręty i operacje (klik = wybór, przeciąganie = kolejność, ⏸ = wyłącz); Obciążenia; Wyniki.
**Widok 3D**: lewy przycisk obrót / wybór, środkowy i prawy przesuw, kółko powiększenie, dwuklik w pustym = cały model;
prawy przycisk na pręcie = operacje dla tego pręta; pomarańczowe **uchwyty** wybranej operacji przeciąga się (Shift = co 1 mm).
**Prawy panel — właściwości**: parametry operacji w mm, ⌖ wskaż pręt w 3D, ↺ wartość domyślna, blok katalogu (pozycja,
nośność tablicowa, wykorzystanie), rysunek blach. Ctrl+Z / Ctrl+Y, Delete = usuń operację, Esc = przerwij, 📷 = PNG widoku.

## Operacje programu w Modelarzu: cięcia, spoiny, blacha czołowa, żebra, podstawa, kątowniki
**Cięcie** pręta: do lica innego (luz, podcięcie pasów), płaszczyzną pod kątem, ukośne w narożniku; **styk doczołowy**
dwóch prętów współliniowych. **Spoiny**: każda grupa i krawędź osobno (pachwinowa dwustronna / jednostronna, czołowa
pełnoprzetopowa / niepełna, kontaktowa, brak); klik spoiny w 3D wybiera krawędź. **Blacha czołowa**: wysunięcia, grubość,
śruby (rzędy pod pasami, nad pasem, 2 / 4 w rzędzie), wymiary wg reguł (Chodor), DAST IH albo MOSTOSTAL Z1.2.
**Żebro** dwuteownika, **Blacha podstawy** z kotwami, **Blacha węzłowa** (zakładka na środniku, rura w rozcięciu),
**Kątowniki** (MOSTOSTAL Z1.3), **Żebro przegubowe** (Z1.1), **Blacha**, **Śruba tutaj** (prawy przycisk na blasze).

## Części z Hali, biblioteka IDEA (106 węzłów), profile z Tekli w Modelarzu
**Części z Hali**: węzeł typu z programu Hala (przycisk „⇨ Modelarz” przy typie węzła) trafia do Modelarza z blachami, śrubami,
spoinami i cięciami Hali oraz z efektami obciążeń z wyników modelu; dalej można dokładać operacje. Sekcja „Blachy z Hali”
pozwala zmienić grubość i usunąć każdą blachę (głosem: „GL daj piętnaście”, „usuń T2.BS”).
Operacje z **biblioteki IDEA** (106 węzłów): pola jak w IDEA, geometrię pokazuje „Podgląd dokładny” (ok. 10 s).
**Kątowniki i ceowniki** (LR, LN, UPE, UPN, C…): zapis do IDEA, blacha węzłowa przy zewnętrznym licu ramienia / środnika.
Nazwy profili z Tekli (L60*6, UNP100, RHS120*80*4, CHS48.3*3.2) są rozpoznawane same.

## Praca głosem i rysikiem w Modelarzu: 🎤, ✏, wskazanie, szkic, przykładowe polecenia
**🎤** = mów (dyktowanie Windows); po 2,5 s ciszy polecenie idzie do AI; w czasie mówienia rysik jest włączony.
**✏** = sam rysik. **Wskazanie**: zakreśl element (pręt, blachę, śrubę, krawędź spoiny): „ta spoina jednostronna od dołu a 6”.
**Szkic** na licu pręta albo na blasze: zamknięty obrys = blacha, kropki = siatka śrub, linia przez pręt = cięcie płaszczyzną.
Sprawdzone polecenia: „sztywny węzeł ramy, blacha czołowa wystająca u góry, osiem śrub M24, żebra słupa”, „przegub na
kątownikach”, „podstawa: blacha czterdzieści, sześć kotew M30, żebra, beton C30/37”, „dospawaj wszystko”, „siły próbne do
sześćdziesięciu procent nośności połączenia i policz”, „pokaż w IDEA”, „dlaczego IDEA nie liczy”. **📚 Przykłady** = galeria
węzłów sprawdzonych w IDEA. **Kontrole na żywo**: styki bez spoiny („⚡ Dospawaj styki”), kolizje blach (pomarańczowe),
części bez połączenia (czerwone), śruby za blisko krawędzi.

## Obciążenia, siły próbne, obliczenia w IDEA i wzorami EC3 bez IDEA, Warsztat (DXF, zestawienie)
**Obciążenia**: „⚖ Siły próbne” z nośności przekrojów (pręt nośny domyka równowagę; opcja „do 60 % nośności połączenia”),
przy każdym efekcie ΣF / ΣM i „⚖ domknij”. **Wyniki** po „▶ Policz” (licencjonowana IDEA) z kontrolą sensowności: szacunek
ręczny śrub, spoin i przekrojów obok wyników IDEA (śruby zwykle 0,6–1,5×, spoiny 1,5–3×, bo CBFEM skupia naprężenia).
**EC3 — bez IDEA**: „▶ policz EC3” wzorami PN-EN 1993-1-8 (te same algorytmy co w Hali): węzły momentowe na blachach
czołowych, przeguby, podstawy słupów, węzły kratownic z rur, zakładki blach węzłowych; połączenia poza wzorami mają
dopisek „wymaga IDEA”. „⚙ dobierz EC3” poprawia parametry z η > 1; „📄 notka EC3” = notka obliczeniowa HTML.
Jedna seria IDEA naraz (Hala albo Modelarz).

## Warsztat w Modelarzu: rysunki blach DXF, zestawienie stali, pozycje blach
Wstęga **Warsztat**: „📐 DXF blach” = rysunki blach do wypalania (obrys, otwory, krawędzie spawane, opis pozycji
„P1 PL20 330x150 1 szt.”), plik obok węzła; „📋 Zestawienie” = pozycje blach, śruby, spoiny (długość × a), masa.
Głosem: „rysunki blach”, „zestawienie stali”. W Hali podobnie: 📄 lista blach (rysunek warsztatowy do wydruku) i ⬇ DXF
w edytorze węzła oraz „📄 zestawienie stali węzłów” (masy blach wg grubości, śruby i spoiny wszystkich typów).
