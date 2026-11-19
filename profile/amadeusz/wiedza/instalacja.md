# Instalacja Amadeusza: instaluj.bat, bez administratora

Z paczki: rozpakuj `Amadeusz-<wersja>.zip`, uruchom `instaluj.bat`, poczekaj na „Gotowe”. Z repozytorium: `instalator\instaluj.bat` (sam zbuduje program, potrzebny .NET SDK 10). Prawa administratora nie są potrzebne. Program trafia do `%LOCALAPPDATA%\Programs\LokalneAI`, skróty „Amadeusz” w menu Start i na pulpicie. Instalator instaluje też Ollamę i pyta o modele. Aktualizacja: nowa paczka i `instaluj.bat` jeszcze raz — ustawienia, historia, hasła, klucze i notatki zostają. Odinstalowanie: Ustawienia Windows → Aplikacje → Amadeusz (LokalneAI); Ollama i modele zostają.

# Wybór modeli przy instalacji (lista, karta graficzna, -Model, -BezPytan)

Instalator pokazuje listę z rozmiarami i gwiazdką przy modelu zalecanym dla pamięci karty: [1] Bielik 11B v3 6,7 GB (karta 8 GB, domyślny w biurze), [2] Qwen3.5 9B 6,6 GB (8 GB, najcelniejszy w Code), [3] Bielik-Minitron 7B 4,5 GB (6 GB), [4] Qwen3.5 4B 3,3 GB (4 GB), [5] Qwen3.5 2B 1,9 GB (bez karty), [6] Bielik 1.5B 1,7 GB (tylko Czat). Numery po przecinku; pierwszy = model domyślny. Bez pytań: `instaluj.bat -Model qwen3.5:4b-q4_K_M` albo `instaluj.bat -BezPytan`. Model na serwerze w biurze: `instaluj.bat -SerwerModelu http://serwer-ai:11434`.

# Karta graficzna, pamięć (VRAM) i kontekst: ile się zmieści

Model musi zmieścić się w pamięci karty razem z kontekstem, inaczej część liczy procesor (wolniej; okno pokazuje udział karty). Przy kontekście 16k: Bielik 11B ok. 10,3 GB (na RTX 4060 8 GB tylko 60–73% na karcie), Qwen3.5 9B ok. 6,7 GB, Qwen3.5 4B ok. 3,7 GB. Kontekst Bielika kosztuje ok. 0,21 GB na 1000 tokenów, Qwena ok. 0,04 GB. Zakładka Code potrzebuje co najmniej 16384 (tekla ~8 tys. tokenów zasad). Słabszy komputer: Gdzie liczyć → inny komputer z mocną kartą.

# Pobrać nowy model (ollama pull) i model domyślny

Model pobiera się w Ollamie na komputerze, który liczy: `ollama pull qwen3.5:9b-q4_K_M` w PowerShellu, albo `instaluj.bat` jeszcze raz. Brama Amadeusza pobierania nie przepuszcza (404). Po pobraniu: przycisk **Odśwież** obok listy modeli. Model domyślny: wybór z listy u góry okna (zapamiętany); gwiazdka dodaje do ulubionych na górze listy.
