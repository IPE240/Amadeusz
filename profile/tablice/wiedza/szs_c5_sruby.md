# SZS C5 Konstruktionstabellen: co to jest, norma SIA 263, γM2, zakres tablic połączeń

- „Konstruktionstabellen C5” Stahlbau Zentrum Schweiz (2. wydanie 2005, przedruk 2012) — szwajcarskie tablice do konstrukcji
  stalowych: tablice profili (IPE, HEA/HEB/HEM, UNP, UPE, LNP, RND, VKT, ROR, RRK/RRW), łączniki (śruby SBS i SHV, kołki,
  pręty gwintowane), połączenia śrubowe i spawane, tolerancje walcowania, formuły geometryczne. Tekst po niemiecku i francusku.
- Podstawa: norma szwajcarska SIA 263 (Swisscode). Nośności w C5 są zawsze wartościami obliczeniowymi (Bemessungswerte)
  z γM2 = 1,25 (łączniki, przekrój netto) i γM1 = 1,05 (nośność przekroju). Kryterium: F_Ed ≤ F_Rd.
- Wartości są bliskie Eurokodowi 3, ale wzory SIA 263 (np. docisk 0,85·(e1/d0)·fu·d·t/γM2) różnią się od EN 1993-1-8 —
  przy projektowaniu wg PN-EN traktuj tablice C5 jako wartości orientacyjne wg SIA 263.
- Oznaczenia C5: SBS — śruby zwykłe (Stahlbau-Schrauben, 4.6/5.6), SHV — śruby sprężane HV (10.9), SUN/SUH — podkładki do SBS/SHV,
  SKUN/SKIN — podkładki klinowe do UNP/INP, F_v,Rd — ścinanie, F_b,Rd — docisk, F_t,Rd — rozciąganie, F_s,Rd — poślizg.

# Śruby: średnice, otwory d0, pola przekroju A i As (SZS C5 s. 97)

- Otwór d0 [mm]: M5 6; M6 7; M8 9; M10 12; M12 14; M16 18; M20 22; M24 26; M27 30; M30 33 (d0 = d + 2 mm od M10, M30: +3).
- Pole trzpienia A [mm²]: M5 19,6; M6 28,3; M8 50,3; M10 78,5; M12 113; M16 201; M20 314; M24 452; M27 573; M30 707.
- Pole czynne (naprężeniowe) As [mm²]: M5 14,2; M6 20,1; M8 36,6; M10 58,0; M12 84,3; M16 157; M20 245; M24 353; M27 459; M30 561.
- Klasy śrub (fyb/fub [N/mm²]): 4.6 240/400; 5.6 300/500; 8.8 640/800; 10.9 900/1000. Inne klasy (5.8, 6.8, 12.9) niezalecane.
- Naprężenia graniczne: ścinanie 0,6·fub/γM2 = 192 (4.6), 240 (5.6), 384 (8.8), 480 (10.9) N/mm²; rozciąganie 0,9·fub/γM2 =
  288, 360, 576, 720 N/mm² (do pola As; śruby rozciągane tylko sprężone).

# Nośność śrub na ścinanie Fv,Rd i rozciąganie Ft,Rd wg SIA 263 (SZS C5 s. 97)

- Fv,Rd [kN] na jedną płaszczyznę ścinania (γM2 = 1,25; Fv,Rd = 0,6·fub·A/γM2, A = pole trzpienia):
  śruby 4.6 (SBS): M10 15,2; M12 21,7; M16 38,6; M20 60,3; M24 86,8; M27 110; M30 136.
  śruby 10.9 (SHV): M10 37,9; M12 54,2; M16 96,5; M20 151; M24 217; M27 275; M30 339.
  Dwie płaszczyzny ścinania: podwójnie (4.6: 30,3; 43,4; 77,2; 121; 174; 220; 271 — 10.9: 75,8; 108; 193; 301; 434; 550; 679).
  Przy ścinaniu w części gwintowanej trzpienia: SIA 263 tab. 16 (mniejsze wartości).
- Ft,Rd [kN] (Ft,Rd = 0,9·fub·As/γM2): 4.6 (SBS): M10 16,7; M12 24,2; M16 45,2; M20 70,6; M24 102; M27 132; M30 162;
  10.9 (SHV): M10 41,8; M12 60,5; M16 113; M20 176; M24 254; M27 331; M30 404. Śruby rozciągane wykonywać jako sprężone.
- Dla klas 5.6 i 8.8 wartości proporcjonalne do fub (5.6 = 1,25 × 4.6; 8.8 = 0,8 × 10.9).
- Ścinanie + rozciąganie jednocześnie: interakcja wg SIA 263 pkt 6.2.2.6.

# Nośność na docisk Fb,Rd śrub M10–M30 w blachach S235 i S355 (SZS C5 s. 97)

- Wzór SIA 263 (71): Fb,Rd = 0,85·(e1/d0)·fu·d·t/γM2, ważny dla e1 ≤ 2,8·d0 i p1 ≥ e1 + d0/2; e1 — odległość od krawędzi
  w kierunku siły, p1 — rozstaw w kierunku siły, t — grubość blachy. Kolumny: M10, M12, M16, M20, M24, M27, M30 [kN na śrubę].
- Rozstawy minimalne (e1 = 15 20 25 30 35 40 45; p1 = 30 35 40 45 55 65 70):
  S235 t = 6: 18,4 25,2 32,6 40,1 47,4 52,9 60,1; t = 8: 24,5 33,6 43,5 53,4 63,3 70,5 80,1; t = 10: 30,6 42,0 54,4 66,8 79,1 88,1 100.
  S355 t = 6: 26,0 35,7 46,2 56,7 67,2 74,9 85,1; t = 8: 34,7 47,6 61,7 75,7 89,6 99,9 114; t = 10: 43,3 59,5 77,1 94,6 112 125 142.
- Rozstawy zalecane (Regelfall; e1 = 20 25 35 40 50 55 60; p1 = 30 40 50 60 70 80 90):
  S235 t = 6: 24,5 31,5 45,7 53,4 67,8 72,7 80,1; t = 8: 32,6 42,0 60,9 71,2 90,4 96,9 107; t = 10: 40,8 52,5 76,2 89,0 113 121 134.
  S355 t = 6: 34,7 44,6 64,7 75,7 96,0 103 114; t = 8: 46,2 59,5 86,3 101 128 137 151; t = 10: 57,8 74,3 108 126 160 172 189.
- Rozstawy maksymalne (e1 ≥ 2,8·d0, p1 ≥ 3,3·d0; e1 = 35 40 55 65 75 85 95; p1 = 40 50 65 75 90 100 120):
  S235 t = 6: 41,5 49,8 66,4 82,9 99,5 112 124; t = 8: 55,3 66,4 88,5 111 133 149 166; t = 10: 69,1 82,9 111 138 166 187 207.
  S355 t = 6: 58,8 70,5 94,0 118 141 159 176; t = 8: 78,3 94,0 125 157 188 212 235; t = 10: 97,9 118 157 196 235 264 294.
- Inne grubości: proporcjonalnie do t. Naprężenia graniczne docisku [N/mm²] (S235/S275/S355/S460): minimum 343/409/485/543,
  zalecane 490/585/694/775, maksimum 691/826/979/1094. Większe rozstawy nie zwiększają nośności.

# Nośność na poślizg Fs,Rd śrub sprężanych HV 10.9 (SZS C5 s. 97) i otwory podłużne (s. 92)

- Fs,Rd [kN] na jedną śrubę i jedną powierzchnię tarcia, pełne sprężenie, otwory normalne, γM3 = 1,1:
  μ = 0,25 (zwykłe powierzchnie): M12 8,4; M16 12,2; M20 22,8; M24 35,6; M27 51,3; M30 66,8;
  μ = 0,4 (tylko gdy wykonawca udokumentuje współczynnik tarcia): M12 13,5; M16 19,5; M20 36,5; M24 57,0; M27 82,1; M30 107.
- Otwory podłużne LL (d0 × l) [mm]: M10 11×14; M12 13×16; M16 18×22; M20 22×26; M24 26×32; M27 30×37.
  Największa grubość do wykrawania: S235 10 12 15 20 20 20 mm; S355 8 10 12 15 15 15 mm.
- Siła poprzecznie do otworu podłużnego: docisk Fb,Rd ze strony 97 × 0,8. Siła wzdłuż otworu podłużnego: docisk zredukowany
  do 60 % (SIA 263 wzór 71), zwykle tylko w dylatacjach i połączeniach montażowych tymczasowych.
- Poślizg przy otworach podłużnych (SHV, μ = 0,25) [kN]: krótkie LL M12 8,2; M16 15; M20 24; M24 34; M27 45; długie LL 6,7; 13; 20; 28; 37.

# Rozstawy i odległości śrub od krawędzi p1, p2, e1, e2 (SZS C5 s. 90, SIA 263)

- Otwór d0: M10 12, M12 14, M16 18, M20 22, M24 26, M27 30, M30 33 mm.
- p1 (rozstaw w kierunku siły) zalecany: M10 30, M12 40, M16 50, M20 60, M24 70, M27 80, M30 90; minimalny: 30, 35, 40, 45, 55, 65, 70.
- p2 (rozstaw prostopadle do siły): zalecany i minimalny jak p1.
- e1 (odległość od krawędzi w kierunku siły) zalecana: 20, 25, 35, 40, 50, 55, 60; minimalna: 15, 20, 25, 30, 35, 40, 45.
- e2 (odległość od krawędzi prostopadle do siły) zalecana: 15, 20, 25, 30, 40, 45, 50; minimalna: 12, 15, 20, 25, 30, 35, 40;
  w kształtownikach e2 według wymiaru w (Rissmass) z tablic profili.
- Nośność na docisk zależy od e1 i p1 (tablica na s. 97); przekrój netto An w przekroju przez otwory sprawdzać osobno.

# Śruby zwykłe SBS i sprężane SHV: wymiary łba, nakrętki, podkładek, długości zacisku (SZS C5 s. 86–88)

- SBS (DIN 7990, M10 ISO 4016; klasy 4.6 i 5.6; z magazynu tylko ocynkowane ogniowo), kolumny M5 M6 M8 M10 M12 M16 M20 M24 M27 M30:
  wysokość łba k: 4 4 6 7 8 10 13 15 17 19; klucz s: 8 10 13 17 19 24 30 36 41 46; nakrętka m: 4 5 6,5 8 10 13 16 19 22 24;
  podkładka SUN (DIN 7989) D: 10 12 16 21 24 30 37 44 50 56, d2: 5,3 6,4 8,4 11 14 18 22 26 30 33, grubość 8 mm (M10–M30); otwór 6 8 10 12 14 18 22 26 30 33.
- SHV (EN 14399-4/-6, klasa 10.9, nakrętka 10, ocynkowane ogniowo), kolumny M12 M16 M20 M24 M27 M30:
  k: 8 10 13 15 17 19; gwint b: 23 28 33 39 41 44; klucz s: 22 27 32 41 46 50; nakrętka m: 10 13 16 20 22 24;
  podkładka SUH (hartowana) D: 24 30 37 44 50 56, d2: 13 17 21 25 28 31, grubość: 3 4 4 4 5 5; otwór 14 18 22 26 30 33.
- Długość śruby dobiera się do długości zacisku lk (tablice klemm w C5 s. 86–87, np. SHV M16: l = 50 → lk 27–32, l = 60 → lk 37–42,
  l = 70 → 47–52, l = 80 → 57–62; SHV M20: l = 60 → 33–38, l = 70 → 43–48, l = 80 → 53–58, l = 90 → 63–68).
- Podkładki klinowe SKUN (UNP, 2 rowki) i SKIN (INP, 1 rowek) wyrównują pochylenie półek; grubość pomocnicza a do długości zacisku: UNP 80–300: 11–22 mm, INP 160–550: 12–37 mm.

# Sprężanie śrub HV 10.9: siły sprężające Fp,Cd i momenty dokręcenia (SZS C5 s. 96)

- Kolumny M12 M16 M20 M24 M27 M30; As = 84,3 157 245 353 459 561 mm². Fp,Cd = 0,64·fub·As.
- Pełne sprężenie (zmęczenie, połączenia cierne) Fp,Cd [kN]: 50 100 160 220 290 350. Częściowe sprężenie (statyczne rozciąganie), minimum: 25 50 80 110 145 (M12–M27).
- Metoda momentowa (klucz dynamometryczny ±5 %), pełne sprężenie: śruby czarne lekko naoliwione 120 350 600 1100 1650 2200 Nm;
  ocynkowane z nakrętką smarowaną MoS2 (przypadek normalny) 100 250 450 800 1250 1600 Nm. Częściowe sprężenie (ocynkowane): 50 120 220 400 620 Nm.
  Wkrętarka udarowa: nastawa 10 % powyżej; zabroniona przy zmęczeniowo rozciąganych śrubach.
- Metoda kombinowana (moment wstępny + kąt): moment wstępny czarne 90 260 450 800 1200 1600 Nm, ocynkowane 75 190 340 600 900 1200 Nm;
  kąt dokręcenia zależnie od długości zacisku L (z podkładkami): L < 2d 45°, 2d ≤ L < 6d 60°, 6d ≤ L ≤ 10d 90°; położenie nakrętki
  oznakować trwale przed dokręceniem.

# Połączenia belek dwoma kątownikami (SZS C9.1) i śruby duże M33–M64 (SZS C5 s. 93, 99)

- Standardowe połączenie dwoma kątownikami równoramiennymi (SZS C9.1), kolumny M12 M16 M20 M24: kątownik L 80×8, L 90×9, L 100×10, L 120×12;
  w1 = w2: 45 55 60 70; e1: 25 35 40 50; p1 min: 40 50 60 80; p1 max: 120 150 180 240; l min: 90 120 140 180; f max: 70 85 90 100;
  w3 min: 30 30 35 40; 2c min: 160 180 200 230; w = 2·w1 + tw + 2 mm.
- Układ przesunięty (małe szerokości, śruby SBS): L 60×6, L 70×7, L 80×8, L 90×9; w1 = w2: 35 40 45 50; e1 min: 25 35 40 50; g min: 35 40 45 50; p1 min: 70 80 90 100; otwór w belce a: 30 35 40 50.
- Śruby duże (DIN 931/934): kolumny M33 M36 M39 M42 M45 M48 M52 M56 M60 M64: klucz s 50 55 60 65 70 75 80 85 90 95; łeb k 21 23 25 26 28 30 33 35 38 40;
  nakrętka m 26 29 31 34 36 38 42 45 48 51; otwór 36 39 42 45 48 51 55 60 64 68; A 855 1018 1195 1385 1590 1810 2124 2463 2827 3217 mm²;
  As 694 817 976 1120 1310 1470 1760 2030 2360 2680 mm²; Ft,Rd (5.6, bez sprężenia, do As) 250 294 351 403 472 530 633 731 850 963 kN —
  zaleca się wykorzystanie do 75 %; z magazynu do M48; spawanie śrub zabronione.
