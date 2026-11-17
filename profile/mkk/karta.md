# Karta: programy Marka Kowolika i programy obliczeniowe (repozytorium MKK)

## Programy biura (kod w `kod/`)
- **Hala** (`kod/hala/`, start `HALA.bat`, raz `INSTALUJ.bat`): kreator modelu hali stalowej z parametrów (osie, nawy, kratownice, dach, attyki, uskoki). Projekt to plik `.mkonH` (stare `.mkhal`). Działy okna: GEOMETRIA, ODDZIAŁYWANIA (śnieg, wiatr wg PN-EN 1991), OBLICZENIA (Robot 2017 albo własny MES), KOMBINACJE, WYNIKI, WYMIAROWANIE (EC3, SGU, węzły IDEA, żelbet, fundamenty), WZMOCNIENIA. Ctrl+K = szybkie przejście do zakładki. Python 3.12, `.venv`.
- **Modelarz węzłów** (`kod/modelarz_wezlow/`, `MODELARZ.bat`): modelowanie połączeń stalowych jak w IDEA StatiCa Connection 25.1, ale od razu z geometrią; zapis `.mkwez` i `.ideaCon`; obliczenia w IDEA (licencja) albo wzorami EC3 bez IDEA. Praca głosem (🎤) i rysikiem (✏).
- **Węzły IDEA** (`WEZLY.bat` w Hali): osobny program na tym samym pliku `.mkonH`.
- Wspólna baza przekrojów `baza/przekroje.json` w notacji Robota (`HEA 300`, `RK 100x4`, `RO 219.1x8`); wymiary mm, charakterystyki cm², cm³, cm⁴, masa kg/m.

## Wersje i drogi dostępu
| Program | Jak się z nim rozmawia | Kluczowa pułapka |
|---|---|---|
| Robot 2017 | COM, `Interop.RobotOM` przez pythonnet | `app.Interactive = 0` przed obliczeniami; `Calculate()` zwraca −1 = OK, 0 = błąd (przyczyna w `robwin.log`) |
| RFEM 6 (6.12–6.16) | API II: gRPC, port 9000, pakiet `dlubal.api`, klucz `ak-…`; plik `.rf6` = ZIP z `model.db` (SQLite) | wersja klienta musi pasować do buildu RFEM; plan Free = 1000 zapytań/mies. |
| RFEM 5 | RF-COM (dodatek w licencji) | kąt obrotu pręta w radianach |
| IDEA StatiCa 25.1 | REST API lokalne, klient `ideastatica_connection_api==25.1.*` | serwis rezerwuje licencję; gasić w `finally`; `.ideaCon` jest zaszyfrowany |
| IFC (Tekla) | ifcopenshell, IFC2X3 i IFC4 | geometria z IFC, siły z modelu obliczeniowego |
| SAP2000 | eksport CIS/2 `.stp` | obciążenia liniowe 1000× za małe, stal z E betonu |

## Znaki i jednostki (najczęstsze pomyłki)
- Robot: FX > 0 = **ściskanie**; RFEM: N > 0 = **rozciąganie**; wszystkie 6 sił wewnętrznych w RFEM ma znak przeciwny niż w Robocie.
- Osie lokalne: RFEM = osie Robota obrócone o 180° wokół x (`angle` RFEM = γ Robota + 180°).
- API Robota i `model.db` RFEM 6 zawsze w SI (N, m, Pa, rad); RFEM 5 odległości obciążeń w procentach.
- Ciężar stali: Robot 2017 77,01 kN/m³, RFEM 78,5 kN/m³ (ok. 1,9 % różnicy w reakcjach).
- Po przeniesieniu modelu zawsze: suma reakcji = wypadkowa obciążeń w każdym przypadku.
- Polski Robot czyta zakresy „1do20”, nie „1to20”.

## Gdzie szukać (pliki w `programy/`)
| Pytanie o… | Plik |
|---|---|
| przenoszenie modeli, osie, znaki, zgodność programów | POWIAZANIA_PROGRAMOW.md |
| Robot przez COM: pręty, przeguby, okładziny, obciążenia, dynamika, wyniki | ROBOT_COM.md |
| wymiarowanie stali w Robocie (RDim, wyboczenie, zwichrzenie, C1) | ROBOT_WYMIAROWANIE_EC3.md |
| plik `.rtd`, wersje 2027/2017/2014 | ROBOT_FORMAT_RTD.md |
| budowa `.rf6`, `model.db`, wyniki w `!solver` | RFEM6_FORMAT_RF6.md |
| zapis modelu wprost do `.rf6` bez API | RFEM6_ZAPIS_RF6.md |
| API II RFEM 6, wersje klienta, limit zapytań | RFEM6_API.md |
| RFEM 5 przez RF-COM | RFEM5_COM.md |
| IDEA: REST, efekty obciążeń, licencja | IDEA_REST_API.md |
| IDEA: IOM, szablony, operacje, podstawy słupów | IDEA_IOM_I_OPERACJE.md |
| IDEA: wyniki, sztywność, zapis z wynikami | IDEA_WYNIKI_SZTYWNOSC.md |
| IFC z Tekli, zapis IFC z węzłami | IFC.md |
| SAP2000 `.stp` | SAP2000_CIS2.md |
| obsługa Hali / Modelarza | kod/hala/README.md, kod/modelarz_wezlow/README.md |
| węzły EC3 w kodzie Hali (API modułów) | kod/hala/tools/ec3_wezly/*.md |

Oznaczenia w dokumentacji: **[P]** = zmierzone próbą, **[W]** = wniosek do potwierdzenia, **D123** = numer decyzji z budowy programu.
