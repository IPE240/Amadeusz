# Raport, opis techniczny, notatka w Wordzie — układ i style

- Nagłówki: `naglowek1` dla rozdziałów (1. Podstawa opracowania, 2. Opis konstrukcji…), `naglowek2` dla podrozdziałów,
  `naglowek3` rzadko. Tytuł dokumentu stylem `tytul`.
- Zwykły tekst stylem `zwykly` (domyślnym); wyliczenia stylem `lista` (punkty) albo `lista_numerowana`.
- Notatka ze spotkania: tytuł z datą (`Spotkanie 2026-11-17`), lista obecnych, ustalenia jako lista, na końcu
  „Następne spotkanie: …”.
- Rewizje: w nazwie pliku i w nagłówku dokumentu jest numer rewizji (`R01`, `R02`). Przy nowej rewizji zamienia się
  numer w całym dokumencie (`word_zamien`, także w nagłówkach i stopkach) i zapisuje w folderze nowej rewizji.
- Pismo przewodnie: data i miejsce, adresat, temat, treść, podpis. Zwykle 1 strona.

# Zestawienia w Excelu (stal, elementy, koszty) — układ

- Wiersz 1: nagłówki kolumn z jednostką w nagłówku (`Masa kg`, `Długość m`, `Cena zł`).
- Każdy gatunek stali (S235, S275, S355) liczy się osobno; masa w kilogramach w tabeli, w podsumowaniu w tonach.
- Sumy w pierwszym pustym wierszu pod danymi, formułą (`=SUMA(...)`), nie wpisaną liczbą.
- Arkusze: jedno zestawienie = jeden arkusz; nazwy arkuszy krótkie (`Stal`, `Beton`, `Kotwy`).
- Nowe zestawienie: `excel_wstaw_wiersze` od `A1` z nagłówkiem w pierwszym wierszu; potem `excel_zapisz` z sumą.

# Maile z biura — zasady dla asystenta

- Asystent tylko przygotowuje szkic (`outlook_szkic`); wysyła zawsze użytkownik po sprawdzeniu.
- Adresatów bierze od użytkownika albo z przeczytanej wiadomości (odpowiedź na maila: ten sam nadawca i temat
  z przedrostkiem `RE:`). Nie zgaduje adresów.
- Układ: powitanie (`Dzień dobry,`), 2–4 zdania, lista załączników, podpis użytkownika zostawia pusty (Outlook dokleja).
- Załączniki muszą leżeć w dozwolonych folderach; raport wysyła się jako PDF (`office_zapisz_jako`), nie jako docx.

# Prezentacje PowerPoint — slajd przeglądu projektu

- Tytuł slajdu krótki (2–4 słowa), 3–6 punktów po jednej linii.
- Typowe slajdy przeglądu: Zakres, Harmonogram, Ryzyka, Decyzje do podjęcia, Następne kroki.
- `powerpoint_dodaj_slajd` używa układu „Tytuł i zawartość”; punkty to kolejne linie pola `tekst`.
