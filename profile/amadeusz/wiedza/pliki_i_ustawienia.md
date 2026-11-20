# Gdzie są pliki Amadeusza: ustawienia, rozmowy, historia, błędy

- program: `%LOCALAPPDATA%\Programs\LokalneAI\` (umiejętności w `profile\` obok programu);
- ustawienia: `%APPDATA%\LokalneAI\ustawienia.json`;
- liczniki tokenów bramy: `%APPDATA%\LokalneAI\tokeny.json`;
- klucze API: `%APPDATA%\LokalneAI\klucze.json` (zaszyfrowane kontem Windows — nie ruszać);
- rozmowy Code: `<folder grupy>\.amadeusz\*.json`, bez grupy `Dokumenty\Amadeusz\.amadeusz\`;
- rozmowy Czatu: `Dokumenty\Amadeusz\Czat\`;
- historia Czatu: `%LOCALAPPDATA%\LokalneAI\historia.jsonl` (JSON w linii, zapis można wyłączyć);
- błędy programu: `%LOCALAPPDATA%\LokalneAI\bledy.log`;
- dane OCR: `%LOCALAPPDATA%\LokalneAI\tessdata`; kopie wiedzy z dysku: `%LOCALAPPDATA%\LokalneAI\wiedza_z_dysku\`.
Hasła baz, FTP i SMB: Menedżer poświadczeń Windows (wpisy `Amadeusz:…`).

# ustawienia.json: pola (ollamaUrl, model, contextLength, computeOn, shareOnLan…)

Nazwy pól camelCase. Najczęściej potrzebne: `ollamaUrl` (adres Ollamy, domyślnie http://127.0.0.1:11434); `model`, `modelProvider` (pusty = Ollama, albo anthropic/openai/google); `contextLength` (kontekst Czatu); `codeContextLength` (kontekst zakładki Code, 0 = jak w Czacie, ale co najmniej 16384); `codeSkillSearch` (szukanie brakujących umiejętności, domyślnie false); `computeOn`, `computeOnName` (Gdzie liczyć; null = ten komputer); `shareOnLan`, `gatewayPort`, `maxContextForOthers` (udostępnianie); `agentMachines`, `agentsUseThisComputer` (podagenci); `favoriteModels`, `cloudModels`; `harnessProfiles` (zaznaczone umiejętności), `workProfiles` (profile); `showTokens`, `showSteps`, `internetAccess`.

# Zmiana ustawienia.json ręcznie lub skryptem — tylko przy wyłączonym programie

Okno zapisuje cały plik przy każdej zmianie w Ustawieniach i nadpisze ręczną poprawkę. Kolejność: zasobnik → prawy przycisk → **Zakończ Amadeusza**, zmiana pliku (UTF-8, poprawny JSON), uruchomienie skrótu „Amadeusz”. Uszkodzony plik program pomija (startuje z ustawieniami domyślnymi) i przy pierwszej zmianie w oknie go nadpisuje — przed edycją zrób kopię. Bezpieczniej zmieniać ustawienia w oknie.
