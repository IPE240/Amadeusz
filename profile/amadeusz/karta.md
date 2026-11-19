# Amadeusz — ściąga

## Części i adresy
- Okno `LokalneAI.exe` (skrót „Amadeusz”); po instalacji `%LOCALAPPDATA%\Programs\LokalneAI\`. Jedna kopia na użytkownika; × chowa do zasobnika, wyłącza „Zakończ Amadeusza” w menu ikony.
- Ollama (modele): `http://127.0.0.1:11434`, tylko ten komputer.
- Brama Amadeusza: `http://127.0.0.1:11435` (TCP) — API Ollamy i OpenAI (`/v1`), kolejka, liczniki tokenów. Z udostępnianiem także z podsieci: `http://NAZWA-PC:11435`.
- Wyszukiwanie komputerów: UDP 11436 („Szukaj w sieci”).
- Opis bramy: `GET /lokalneai/info` (komputer, karta, liczy, czeka, maks_kontekst).

## Najważniejsze miejsca w oknie
- Ustawienia → Gdzie liczyć (ten komputer / inny komputer, komputery dla podagentów).
- Ustawienia → Udostępnianie (przełącznik, port, limit kontekstu dla innych, „Otwórz port w zaporze…”).
- Ustawienia → Kolejka (kto liczy i czeka, „Odrzuć”, liczniki tokenów z „Resetuj”).
- Ustawienia → Czat → Diagnostyka („Pokaż tokeny i czasy”, „Pokaż kroki modelu”).
- Ustawienia → Umiejętności (folder, źródła wiedzy, „Aktualizuj wiedzę”), Ustawienia → Profile.

## Pojęcia
- umiejętność = folder `profile\<nazwa>\` (np. tekla, cde, office); profil = nazwany zestaw umiejętności (np. Biuro).
- „Gdzie liczyć” = na którym komputerze liczy model; rozmowa zostaje na tym PC.
- Kontrola bez klikania = `LokalneAI.exe --autotest "…" --wynik <folder>` (z `--harness <umiejętność>` dla zakładki Code).

## Zakazy
- Brama nie przepuszcza pobierania, usuwania i tworzenia modeli (404). Model pobiera się `ollama pull <tag>` na komputerze, który liczy.
- Zapytanie z przeglądarki (nagłówek Origin) dostaje 403.
- `ustawienia.json` zmienia się tylko przy wyłączonym programie.
