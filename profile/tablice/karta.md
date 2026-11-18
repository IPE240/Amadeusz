# Tablice konstrukcyjne — ściąga

## Nazwy przekrojów w wiedzy (tak szukaj)
- dwuteowniki: `I 200`, `IPE 200`, `IPE AA 200`, `IPE o 200`, `IPE v 400`, `HEA 160`, `HEAA 160`, `HEB 300`, `HEC 300`, `HEM 240`, `HL 1000x477`, `HD 360x162`, `HP 305x88`, `BS 203x60`, `JIS 300x37`;
- połówki: `1/2 I 200`, `1/2 IPE 200`; ceowniki: `C 200`, `CE 200`, `UPE 200`, `UAP 200`; teowniki: `T 60`;
- kątowniki: `L 60x60x6` (równoramienne), `L 100x50x8` (nierównoramienne: b×a×t, b dłuższe ramię);
- rury: `RO 88,9x3,2` (okrągłe D×t), `RK 100x100x5` (kwadratowe), `RP 120x80x5` (prostokątne h×b×t);
- zestawienia rodzin (porównania, rankingi, wykresy): `IPE zestawienie rodziny`, `HEA zestawienie rodziny`, `L zestawienie rodziny`… — kilkanaście przekrojów w jednym fragmencie (osobno wymiary i masa, osobno I, W, i); szukaj z `ile` = 4–8.

## Jednostki w tablicach Żyburtowicza
h, b, tw, tf, R [mm]; A [cm²]; masa [kg/m]; U [m²/m]; Iy, Iz, IT [cm⁴]; Wy, Wz [cm³]; iy, iz [cm]; Iω [cm⁶] (w HE i IPE o/v: ×1000 cm⁶); Wω [cm⁴]; k = √(G·IT/(E·Iω)) [1/cm]; e, a (ceowniki, kątowniki) [cm]; d, w — otwór i rozstaw otworów na śruby [mm].
Rury: wszystkie wielkości policzone z D i t (A, I, W, i, masa 0,785·A). Kątowniki: ξ — oś największej, η — najmniejszej bezwładności; v — odległość narożnika od osi ξ.

## Połączenia DAST (Typisierte Anschlüsse, Prüfbescheid II B 2-543-778)
- Typy IH1, IH2 — blacha czołowa równa z pasem (bündig); IH3, IH4 — blacha wystająca (überstehend). Drugi człon (IH1.1, IH1.2…) to wariant rozstawu śrub.
- Każde połączenie Nr 1–859 ma: profil belki, typ, śrubę (M16–M30), wymiary blachy tp×bp×hp, rozstawy e1…e4, u1, u2, w1…w3, spoiny aw (środnik), af (pas), sztywność Sj,ini [MNm/rad] bez słupa i ze słupem IPE/HEA/HEB/HEM.
- Nośności [kNm, kN]: Mj,1,Rd (moment graniczny połączenia), stan graniczny (EPB — blacha czołowa na zginanie, BT — śruby na rozciąganie, BFC — pas i środnik belki na ściskanie, BWT — środnik belki na rozciąganie), Mj,2,Rd (moment odwrotny), Vz,Rd (ścinanie), Mc,y,Rd (nośność belki). Osobno wg DIN 18800 i wg EC 3.
- „erf. Stütze” — najmniejszy słup (IPE/HEA/HEB/HEM) bez żeber, dla 100/80/60 % wykorzystania; „St” = potrzebne żebra w słupie, „/” = brak słupa dla tego poziomu.
- Cztery zestawy: S235 + 8.8, S235 + 10.9, S355 + 8.8, S355 + 10.9 (stal belki i blachy, klasa śrub).

## SZS C5 (Konstruktionstabellen, SIA 263, γM2 = 1,25)
- Śruby SBS (4.6/5.6, zwykłe) i SHV (10.9, sprężane HV): Fv,Rd (ścinanie, 1 lub 2 płaszczyzny), Fb,Rd (docisk; zależy od e1, p1, t i stali), Ft,Rd (rozciąganie), Fs,Rd (poślizg, μ = 0,25/0,4).
- Rozstawy: p1, p2, e1, e2 — „Regelfall” (zalecane) i „Minimum”. Otwory d0 = d + 2 mm (M10: 12, do M30: 33).
- Spoiny pachwinowe: FRd = a · ΔL · 0,6 · fu,E / γM2 (fu,E — wytrzymałość spoiwa; dla S355: 0,6·fu,E = 306 N/mm²).

Wartości z tablic są pomocą projektową — przed użyciem sprawdź aktualność normy i katalogu producenta.
