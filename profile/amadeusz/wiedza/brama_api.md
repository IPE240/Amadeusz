# Brama: trasy API — co przepuszcza (chat, generate, embed, v1)

Brama `http://127.0.0.1:11435` przepuszcza tylko liczenie i odczyt:
- odczyt: `GET /api/tags` (lista modeli), `GET /api/ps` (załadowane), `GET /api/version`, `POST /api/show`, `GET /v1/models`;
- liczenie API Ollamy: `POST /api/chat`, `POST /api/generate`, `POST /api/embed`, `POST /api/embeddings`;
- liczenie API OpenAI: `POST /v1/chat/completions`, `POST /v1/completions`, `POST /v1/embeddings`;
- opis bramy: `GET /lokalneai/info`.
Każda inna trasa dostaje **404** — także `/api/pull` (pobieranie), `/api/delete`, `/api/create`, `/api/copy`, `/api/push`. Model pobiera się w Ollamie na komputerze, który liczy: `ollama pull <tag>`. Wszystkie zapytania liczące stają w jednej kolejce.

# /lokalneai/info — opis bramy, czy Amadeusz działa

```powershell
Invoke-RestMethod http://127.0.0.1:11435/lokalneai/info
```
Pola: `program` (LokalneAI), `komputer` (nazwa Windows), `wersja`, `port`, `udostepnia` (czy przyjmuje inne komputery), `karta` (karta graficzna), `temperatura` (°C), `liczy` i `czeka` (kolejka), `maks_kontekst` (największy num_ctx od innych komputerów, domyślnie 16384). Brak odpowiedzi: program nie działa, port jest inny albo bramę ma inne okno.

# Nagłówki X-LokalneAI-Program, X-LokalneAI-Komputer, X-LokalneAI-Przekazane, Origin

- `X-LokalneAI-Program: <nazwa>` — ustawia Twój program; nazwa w kolejce okna (np. „Skrypt raportu”). Polskie litery kodowane jak w adresie (%C5%82). Bez niego brama bierze User-Agent.
- `X-LokalneAI-Komputer: <nazwa PC>` — ustawia brama przy „Gdzie liczyć” i zakładka Code przy podagentach; nazwa w kolejce i licznikach tokenów innego PC. Bez niego widać adres IP. To podpis, nie hasło.
- `X-LokalneAI-Przekazane: 1` — „licz tutaj, nie przekazuj dalej” (bez pętli).
- `Origin` (wysyła każda przeglądarka) — zawsze 403: strona internetowa nie użyje modelu.
- `Authorization` — przechodzi bez sprawdzania; brama nie ma haseł.

# Kody błędów bramy: 400, 403, 404, 413, 502, 503

Błąd to JSON `{"error":"…"}` po polsku.
- 400 — zapytanie nie jest JSON-em; inny komputer chce kontekstu (num_ctx) większego niż limit udostępniającego.
- 403 — nagłówek Origin (przeglądarka); komputer nie udostępnia; pytający spoza podsieci; model „-cloud” (chmura Ollamy wyłączona).
- 404 — trasa spoza listy (pobieranie, usuwanie, tworzenie modeli).
- 413 — zapytanie większe niż 16 MB.
- 502 — brak połączenia z Ollamą albo z bramą innego komputera (treść mówi, co sprawdzić).
- 503 — kolejka innych komputerów pełna (8 czekających); właściciel odrzucił albo przerwał zapytanie.

# Przykład PowerShell: zapytanie do modelu przez bramę (api/chat)

```powershell
$body = @{ model = "qwen3.5:4b-q4_K_M"; stream = $false; think = $false; options = @{ num_ctx = 8192 }
    messages = @(@{ role = "user"; content = "Stolica Polski? Jedno słowo." }) } | ConvertTo-Json -Depth 5
$r = Invoke-RestMethod http://127.0.0.1:11435/api/chat -Method Post -ContentType "application/json; charset=utf-8" `
    -Headers @{ "X-LokalneAI-Program" = "PowerShell" } -Body ([Text.Encoding]::UTF8.GetBytes($body))
$r.message.content; $r.prompt_eval_count; $r.eval_count
```
Body jako bajty UTF-8 — inaczej Windows PowerShell 5.1 psuje polskie litery. `prompt_eval_count` = tokeny przeczytane, `eval_count` = napisane.

# Przykład curl i Python bez paczek (urllib)

```powershell
curl.exe -s http://127.0.0.1:11435/api/chat -H "X-LokalneAI-Program: curl" -d '{\"model\":\"qwen3.5:4b-q4_K_M\",\"stream\":false,\"think\":false,\"messages\":[{\"role\":\"user\",\"content\":\"2+2?\"}]}'
```
```python
import json, urllib.request
req = urllib.request.Request("http://127.0.0.1:11435/api/chat",
    data=json.dumps({"model": "qwen3.5:4b-q4_K_M", "stream": False, "think": False,
                     "messages": [{"role": "user", "content": "Stolica Polski?"}]}).encode("utf-8"),
    headers={"Content-Type": "application/json", "X-LokalneAI-Program": "Python"})
print(json.load(urllib.request.urlopen(req))["message"]["content"])
```

# API zgodne z OpenAI (v1/chat/completions): base_url, klucz, myślenie, kontekst

Adres: `http://127.0.0.1:11435/v1`, klucz API dowolny niepusty (np. `ollama`).
```python
from openai import OpenAI
client = OpenAI(base_url="http://127.0.0.1:11435/v1", api_key="ollama")
r = client.chat.completions.create(model="qwen3.5:4b-q4_K_M", reasoning_effort="none",
    messages=[{"role": "user", "content": "Stolica Niemiec?"}])
```
Pułapki: (1) modele z myśleniem (qwen3.5) bez `reasoning_effort: "none"` zużywają tokeny na myślenie — przy małym `max_tokens` treść jest pusta; (2) API OpenAI nie ma `num_ctx`, Ollama bierze kontekst domyślny (np. 4096) i **dłuższy prompt obcina po cichu** — długie prompty wysyłaj przez `/api/chat` z `options.num_ctx`.

# Myślenie modelu (think) i kontekst (num_ctx) w API Ollamy

- `"think": false` wyłącza „myślenie” modeli takich jak qwen3.5 (inaczej potrafią myśleć minutami). Ollama przyjmuje to pole także dla modeli bez myślenia.
- `"options": {"num_ctx": 16384}` — długość kontekstu. Prompt dłuższy niż kontekst Ollama obcina po cichu. Inny komputer przyjmuje najwyżej tyle, ile ma w `maks_kontekst` (większy = błąd 400, nie obcięcie).
- `"keep_alive": "5m"` — jak długo model zostaje w pamięci karty; zapytaniom innych komputerów właściciel narzuca swoją wartość (5 min).
- Nazwę modelu bierz dokładnie z `GET /api/tags`, np. `qwen3.5:9b-q4_K_M`, `SpeakLeash/bielik-11b-v3.0-instruct:Q4_K_M`.
