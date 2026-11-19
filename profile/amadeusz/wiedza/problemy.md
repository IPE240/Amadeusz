# Brama nie odpowiada, Ollama nie działa, brak modelu

- `/lokalneai/info` (127.0.0.1:11435) nie odpowiada: czy program działa (ikona w zasobniku); port w Ustawienia → Udostępnianie; `Get-NetTCPConnection -LocalPort 11435 -State Listen`.
- „Brama nie wystartowała na porcie 11435”: port zajęty przez inny program — zmień port w Ustawieniach (na wszystkich komputerach).
- Ollama nie działa albo brak modelu: baner nad rozmową podpowiada (Uruchom Ollamę, Pobierz model, polecenie instalacji). Sprawdzenie: `Invoke-RestMethod http://127.0.0.1:11434/api/version`.

# Błąd przy innym komputerze: nie ma połączenia, nie udostępnia, podsieć, kontekst

- „Nie ma połączenia z PC-…”: czy tam działa Amadeusz z udostępnianiem, czy kliknięto „Otwórz port w zaporze”, czy sieć jest prywatna, nie publiczna.
- „Szukaj w sieci” nic nie znajduje, a adres działa: brak reguły UDP 11436 albo różne podsieci — wpisz adres ręcznie.
- „… przyjmuje tylko z podsieci …”: pytający jest w innej podsieci (np. VPN).
- „… nie udostępnia teraz modelu …”: właściciel wyłączył przełącznik.
- „… przyjmuje kontekst do 16384 tokenów …”: zmniejsz kontekst albo poproś o większy limit.
- „Na komputerze … nie ma modelu …”: wybierz model z listy tamtego komputera.

# Zapytanie z programu: 404, 403, pusta odpowiedź, obcięty prompt

- 404 przy `/api/pull` albo `/api/delete`: celowo, pobieraj `ollama pull` na komputerze, który liczy.
- 403 „ze stron internetowych”: klient wysyła nagłówek Origin (przeglądarka) — wołaj bramę z programu albo serwera.
- Pusta treść z `/v1/chat/completions`: model myśli — `reasoning_effort: "none"` albo większe `max_tokens`.
- Model „zapomina” początek długiego promptu: Ollama obcięła prompt dłuższy niż kontekst — `/api/chat` z `options.num_ctx`.
- Zapytanie długo czeka: Ustawienia → Kolejka — liczy coś innego; zapytania z tego PC mają pierwszeństwo.

# Wolno liczy, model na procesorze, „Nie udało się dokończyć polecenia w N krokach”

Wolno: model nie mieści się w karcie z kontekstem (pod odpowiedzią udział karty poniżej 100%) — mniejszy model, krótszy kontekst albo Gdzie liczyć → mocniejszy komputer; „Zwolnij pamięć GPU”, gdy kartę zajmuje inny model. „Nie udało się dokończyć polecenia w N krokach”: model wyczerpał `max_krokow` umiejętności — uprość polecenie, podziel je na części albo wybierz celniejszy model (Qwen3.5 9B).
