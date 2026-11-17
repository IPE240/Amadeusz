# Programy obliczeniowe: Robot, RFEM 5 i 6, IDEA StatiCa, IFC, SAP2000 (wiedza z repozytorium MKK)

## Repozytorium MKK: co w nim jest, skąd wiedza, oznaczenia [P] [W] D123
Repozytorium `Optimal-Design-of-Structures/MKK` (Marek Kowolik) zbiera od 2026-09-30 ustalenia o tym, jak naprawdę działają
programy zewnętrzne: API, formaty plików, konwencje i pułapki. Każde ustalenie ma podany sposób sprawdzenia (próba w nowym
tymczasowym modelu albo porównanie wyników kilku programów). Oznaczenia: **[P]** zmierzone próbą, **[W]** wniosek do
potwierdzenia, **D123** numer decyzji z budowy programu Hala. Folder `programy/` ma po jednym pliku na temat
(ROBOT_COM, ROBOT_WYMIAROWANIE_EC3, ROBOT_FORMAT_RTD, RFEM6_API, RFEM6_FORMAT_RF6, RFEM6_ZAPIS_RF6, RFEM5_COM, IDEA_REST_API,
IDEA_IOM_I_OPERACJE, IDEA_WYNIKI_SZTYWNOSC, IFC, SAP2000_CIS2) i zbiorczy POWIAZANIA_PROGRAMOW. Folder `kod/` to kod Hali
i Modelarza węzłów. Repozytorium jest prywatne i poufne: tylko członkowie organizacji, nie udostępniać na zewnątrz.

## Drogi przenoszenia modeli między programami (Robot, RFEM, IDEA, IFC, SAP2000)
Sprawdzone drogi: RFEM 6 (.rf6) → Robot przez odczyt `model.db` i zapis COM Robota; Robot albo model prętowy → RFEM 6 przez
zapis wprost do `model.db` kopii pliku wzorcowego (API tylko na wzorzec i przekroje); RFEM 5 ↔ Robot / MES przez RF-COM
w obie strony; SAP2000 → Robot przez CIS/2 (.stp) z poprawkami jednostek i materiałów; Robot 2027 → 2017 → 2014 przez
przepisanie pliku `.rtd`; Robot / RFEM → IDEA jako efekty obciążeń w osiach lokalnych prętów; IFC (Tekla) → IDEA (geometria
z IFC, siły z modelu obliczeniowego); IDEA → model jako sztywność węzła S_j (zwolnienie sprężyste, w Robocie tylko z DSC);
IDEA (IOM) → IFC. Po każdym przeniesieniu sprawdzać sumę reakcji = wypadkową obciążeń w każdym przypadku.
Szczegóły: POWIAZANIA_PROGRAMOW.md §1.

## Osie lokalne prętów i znaki sił: Robot, RFEM 5/6, IDEA (ściskanie, rozciąganie, gamma, angle)
Robot: γ = 0 oznacza, że dla pręta niepionowego oś z leży w płaszczyźnie pionowej (w górę); pręt dokładnie pionowy ma
y = rzut globalnego Y, a już przy 1,75° pochylenia obowiązuje reguła ogólna (osie odwrócone). RFEM 6: osie „z w dół” = osie
Robota obrócone o 180° wokół x, więc `angle` RFEM = γ Robota + 180°. RFEM 5 jak RFEM 6, ale kąt w radianach. IDEA: efekty
w osiach lokalnych pręta IDEA. IFC z Tekli: oś kątownika / ceownika = środek obrysu, nie środek ciężkości.
Znaki: Robot FX > 0 = ściskanie; RFEM N > 0 = rozciąganie; wszystkie 6 składowych sił wewnętrznych w RFEM ma znak
przeciwny niż w Robocie; `Data.HXN` w pliku .rf6 zapisuje −Mz. Reakcje: Robot = siła podpory na konstrukcję; RFEM w pliku
na konstrukcję, w tabeli i RF-COM konstrukcji na podporę. Szczegóły: POWIAZANIA_PROGRAMOW.md §2–3.

## Materiały, przekroje, jednostki przy przenoszeniu (ciężar stali, RK a SHS, SI, procenty)
Ciężar stali: Robot 2017 77,01 kN/m³, RFEM 78,5 kN/m³ (ok. 1,9 % różnicy w reakcjach od ciężaru); materiały przenosić pręt
po pręcie (E, G, ν, γ, α). Rury: polskie RK (Robot, katalog Polska 2004 / 2007) a SHS EN w RFEM różnią się polem o 1–3 %
i It; cienkościenne RK (np. 120x4) są tylko w „Polska 2004”. RFEM uwzględnia sztywność na ścinanie, Robot domyślnie nie
(ok. 1 % w krótkich belkach). Jednostki: API Robota zawsze SI; `model.db` RFEM 6 w SI (m, N, Pa, rad, K); RFEM 5 odległości
względne obciążeń w procentach; SAP2000 CIS/2 obciążenia liniowe 1000× za małe przy mm. Temperatura w RFEM 6: w pliku
w K, „zmiana temperatury” to różnica w K = °C. Szczegóły: POWIAZANIA_PROGRAMOW.md §4.

## Autodesk Robot 2017 przez COM: pythonnet, Interactive, Calculate, robwin.log, licencja
Z Pythona bez pywin32: pakiet pythonnet i biblioteka `Interop.RobotOM.dll` z instalacji Robota. Przed obliczeniami
`app.Interactive = 0`, inaczej pierwsze okno dialogowe (np. niestateczność) blokuje skrypt bez końca. `Calculate()` zwraca
−1 = OK, 0 = błąd; przyczyna jest w `%APPDATA%\Autodesk\Autodesk Robot Structural Analysis Professional 2017\Tmp\robwin.log`.
API pracuje zawsze w SI niezależnie od jednostek projektu. Na koniec `app.UserControl = True`; zamykanie: najpierw
`Interactive = 1`, potem `Project.Close` i `Quit` (zwolnienie licencji). Polski Robot czyta zakresy „1do900”, nie „1to900”.
Komunikaty Robota mają znaki spoza cp1250: zapisywać w UTF-8. Czas dużego modelu to głównie odczyt wyników, nie solver.
Zasady: pracować w nowym projekcie tymczasowym, nie przejmować okna Robota użytkownika, nazwę procesu porównywać bez
wielkości liter (`robot.EXE`). Szczegóły: ROBOT_COM.md §1–2, §12.

## Robot: pręty, przeguby, okładziny, obciążenia, dynamika, plik bez wyników (ROBOT_COM.md)
ROBOT_COM.md opisuje: numerację, etykiety i materiały (§3); przekroje z katalogu „Polska 2007”, przekroje nietypowe NS,
własne bazy, przekroje spawane i złożone (§4); obrót pręta Gamma, zwolnienia sprężyste i nieliniowe (tylko z DSC), pręty
kratownicowe, węzły dokładane przez Robota, pręty nakładające się, superpręty, ofsety i sztywne powiązania (§5);
obciążenia, obciążenie na konturze `I_LRT_IN_CONTOUR`, obciążenia ruchome nie działają przez COM (§6); okładziny
(kierunek, metoda, pręty ukośne, ofsety) (§7); płyty i powłoki (§8); dynamika, przypadki spektralne, analizy nieliniowe
P-Δ i panele FE (§9); wyniki (§10). Plik bez wyników: `SaveToFormat(I_PSF_RTD_NORESULTS)` → `Project.New` (samo `Close`
nie zwalnia pliku). `Project.Open` pliku nowszego niż Robot nie zgłasza błędu: sprawdzać `Project.FileName` (§11).
Tolerancja łączenia węzła z prętem jest mała: 1,4 mm odchyłki już nie łączy.

## Wymiarowanie stali w Robocie (RDim, EN 1993-1-1): typy prętów, wyboczenie, zwichrzenie, C1, pułapki
ROBOT_WYMIAROWANIE_EC3.md: model obiektowy modułu RDim z `Interop.RobotOM`, typy prętów (nowy typ „Pręt” ma zwichrzenie
wyłączone, „Belka” bez wyboczenia giętnego), długości wyboczeniowe `SetLengthYZUV` (ujemne = współczynnik, dodatnie = metry),
stężenia pośrednie `SetIntPtAddOption`, C1 z `LoadTypeY` (jeden C1 ≤ 1,88 na pręt i oba pasy), rama przesuwna (AUTO daje
μ < 1, `SetStructureSwayYZ` nie zapisuje się), SGU = wszystkie `I_CBT_SLS`. Każdy pręt = osobny pręt projektowy;
`SetCalcConf` po `SetCalcParam` zeruje listę; wyniki tylko w pamięci modułu, który liczył; `Lay/Laz` to smukłości;
domyślne γM2 = 1,25; `LENGTH_Z` zmienia bazę L_LT. M_x ≠ 0 zmniejsza V_pl: duże η przy małych siłach oznacza skręcanie
w modelu. Pełna lista 21 pułapek: §10, pomiary 2026-10-28…30: §10a.

## Plik .rtd Robota: kontener OLE, wersja formatu, 2027 → 2017 → 2014
`.rtd` to kontener OLE (Compound File Binary, jak stare .doc) z ok. 700 strumieniami, np. `/RV6STR/ELDAT` (pręty).
O otwarciu decyduje wersja formatu w strumieniu `/RV6STR/JobNmATR` (rekord 3, tekst w `/RV6STR/JobNmNAM`): 27.0.0 = Robot
2014, 29.0.1 = 2017, 34.3.0 = 2027. Każdy Robot otwiera plik o wersji niższej lub równej i sam go aktualizuje; plik nowszy
po cichu nie otwiera się (zostaje poprzedni projekt). Przepisanie 2027 → 2017 → 2014 bez Robota: zmiana tekstu wersji
i typów prętów (ROBOT_FORMAT_RTD.md §3–4), droga zapasowa przez API (§5), kontrola po konwersji (§6).

## RFEM 6: API II (dlubal.api, gRPC, port 9000), klucz, plan Free, wersja klienta = wersja RFEM
API II to klient–serwer gRPC; serwer startuje razem z RFEM na porcie 9000, nic nie trzeba włączać. Klient: pakiet `dlubal.api`
z PyPI. Musi być otwarty model (sam ekran startowy daje `UNAVAILABLE`). Potrzebny osobisty klucz API II z Extranetu Dlubala
(postać `ak-…`; nie wkładać do repozytoriów). Plan „Free”: 1000 zapytań miesięcznie, jeden aktywny użytkownik; pobranie
modelu w 8 typach obiektów ≈ 19 zapytań; przerwanie w połowie pobierania to zwykle wyczerpany limit. Najważniejsza pułapka:
wersja `dlubal.api` musi pasować do konkretnego buildu RFEM (RFEM podaje właściwą w ostrzeżeniu przy połączeniu; sprawdzone
pary: 6.16 ↔ 2.16.1, 6.15 ↔ 2.15.2 / 2.15.4, 6.12 ↔ 2.12.1). Niezgodność potrafi działać cicho (gubione przekroje, obliczenia
bez wyników). Dlatego klient RFEM w Hali ma własne `.venv` zakładane przez `tools/rfem_setup.py`. Szczegóły: RFEM6_API.md §1–3.

## Plik .rf6 RFEM 6: ZIP, model.db (SQLite), wyniki w !solver, odczyt bez RFEM
`.rf6` to zwykłe archiwum ZIP: `format.txt` (wersja), `model.db` (cały model, baza SQLite), `versioned/*.db` (historia),
`mesh.xml`, `unit.xml`, podgląd `preview.glb`, a w `!solver/` siatka i wyniki obliczeń (prawie cała objętość pliku).
Format jest wewnętrzny i nieudokumentowany; nowa wersja RFEM może przesunąć nagłówki, więc sprawdzać wartości kontrolne
(RFEM6_FORMAT_RF6.md §8). Siły w prętach: `Data.HXN<nr>` (w 6.10 `HXL`), wyniki węzłowe `Data.RN<nr>` (starsze `RO`),
siły wewnątrz prętów tylko w węzłach siatki (§4.1a). Zapis modelu prętowego wprost do `model.db` bez API (RFEM6_ZAPIS_RF6.md):
kopia pliku wzorcowego, klonowanie obiektów, przekroje BASIC, nowy GUID modelu w `general_data.xml`, przy pakowaniu pominąć
`!solver/` i `versioned/`. Zgodność RFEM 6.16 ↔ niezależny MES: sumy 0,00 %, reakcje ≤ 0,5 %, siły ≤ 1–3 %.

## RFEM 5 przez RF-COM: licencja, odczyt, zapis, radiany, procenty
RFEM 5 (5.27, 5.31) rozmawia przez dodatek RF-COM (COM, lokalnie; dodatek w licencji; bez limitu zapytań), inaczej niż
RFEM 6 (gRPC, klucz, limit). Starszy interfejs daje dostęp do większej części modelu: model, podpory, obciążenia, kombinacje,
wyniki, zapis i obliczenie. Konwencje: kąt obrotu pręta w radianach (wzięty w stopniach zamienia Vy/Vz i My/Mz), odległości
względne obciążeń w procentach, składowe przegubów „Special” działają sztywno, współczynnik sztywności pręta działa tylko
na sztywność. Okna dialogowe i stan RFEM potrafią blokować sesję (RFEM5_COM.md §6). Zgodność z niezależnym MES ≤ 2·10⁻⁴.

## IDEA StatiCa 25.1: REST API, serwis lokalny, licencja, plik .ideaCon, efekty obciążeń
Serwis `IdeaStatiCa.ConnectionRestApi.exe` działa lokalnie (bez klucza), klient Pythona `ideastatica_connection_api==25.1.*`
(REST API 3.0; dla 25.0 wersja 2.0, inne adresy). Serwis rezerwuje miejsce licencyjne: uruchamiać tylko na czas pracy
i gasić w `finally`; otwarty program IDEA i serwis zajmują dwa miejsca; „serwis zakończył się, zanim wstał” = zwykle brak
licencji. `.ideaCon` jest zaszyfrowany (bez IDEA nie do odczytu), otwierać kopię, bo otwarcie podnosi wersję pliku; serwis
zapisuje model bez wyników. Publiczne API nie ma endpointów „dodaj blachę / śrubę / spoinę / cięcie”: geometria powstaje
z IOM (XML), z szablonu (`apply_template`), z biblioteki połączeń albo z gotowego `.ideaCon` (IDEA_IOM_I_OPERACJE.md §1).
Efekty obciążeń: siła pręta na węzeł w osiach lokalnych pręta IDEA, punkt przyłożenia `forcesIn` (IDEA_REST_API.md §7).

## IDEA StatiCa: wyniki przez REST, sztywność węzła, model_type, zapis z wynikami
`calculation.calculate` uruchamia CBFEM, `get_results` daje kontrole normowe per komponent, `get_raw_json_results` bogaty
słownik (JSON zakodowany dwa razy): `analysis` (`appliedLoadPercentage`, komunikaty), `plates`, `bolts` (odległości
`distance_e1_EN`… w m), `welds`, `stiffnesess`. REST nie daje pól naprężeń per węzeł siatki (map). Pułapki czytania wyników,
porównanie z szacunkiem ręcznym i z obliczeniem EC3, wybór pręta nośnego zmienia wynik: IDEA_WYNIKI_SZTYWNOSC.md §2.
Dobór spoin `pre_design_welds` (§3), zapis `.ideaCon` z wynikami tylko przez desktop (§4), analiza sztywności ST (§5),
`model_type` = przegub bez zdejmowania pręta (§6), czego API 25.1 nie robi (§7).

## IFC z Tekli: odczyt (ifcopenshell), nazwy profili, zapis IFC z węzłami, zasada „geometria z IFC, siły z modelu”
Zasada nadrzędna: węzeł do sprawdzenia buduje się wyłącznie z IFC wykonawczego (profile, mimośrody, blachy, śruby, cięcia),
a model obliczeniowy służy tylko do przypisania sił końcom prętów; różnicę profilu IFC ≠ model zgłaszać, nie poprawiać.
Przegub w wykonaniu a moment w modelu: liczyć węzeł jak wykonany z wyraźną uwagą; fałszywe rozpoznanie przegubu jest
niebezpieczne (zaniża wytężenie). IFC.md opisuje: pręty, blachy, śruby, otwory, zespoły w IFC2X3 i IFC4 (§2), konwencję nazw
profili Tekli, błędy modeli Tekli, pułapki ifcopenshell, dopasowanie układu IFC ↔ model (§3), przypisanie prętów (§4), typy
węzłów i symetrię (§5), zapis IFC modelu z węzłami w układzie projektu (§6), IFC urządzeń → obciążenia technologiczne (§7).

## SAP2000: eksport CIS/2 (.stp): obciążenia 1000× za małe, stal z E betonu
SAP2000 zapisuje plik w jednym układzie jednostek (np. N i mm); obciążenie liniowe ma jednostkę samej siły, a liczba jest
w N/mm, więc importery czytające N/m dostają obciążenia 1000 razy za małe (poprawka ×1000 przy mm; pułapka: plik bywa już
poprawiony ręcznie, test: mediana |q| nierealnie duża). Materiał nazwany jak stal (S235, S355) ma w pliku E = 24 856 MPa,
ν = 0,2, fy = 0 (szablon betonu): poprawić na E = 210 000 MPa, ν = 0,3, inaczej przemieszczenia ok. 8,5× za duże.
Czego w .stp nie ma, rozkodowanie encji, import do RFEM 5 i do Robota: SAP2000_CIS2.md §2–5.
