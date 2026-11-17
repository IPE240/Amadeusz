# Amadeusz (LokalneAI) — instalacja na komputerze użytkownika (bez praw administratora).
#
#   instaluj.bat                                     model liczy na tym PC (instaluje Ollamę i pobiera model)
#   instaluj.bat -SerwerModelu http://serwer-ai:11434  model liczy na serwerze w biurze (bez Ollamy na tym PC)
#   instaluj.bat -Model SpeakLeash/bielik-minitron-7B-v3.0-instruct:Q4_K_M   inny model (ten: mniejszy i szybszy)
#
# Model domyślny: Bielik 11B v3 (SpeakLeash, Q4_K_M) — najlepszy z Bielików w naszych testach (docs\wybor-modelu.md).
#
# Program trafia do %LOCALAPPDATA%\Programs\LokalneAI, dostaje skrót „Amadeusz” w menu Start i na pulpicie
# oraz wpis w Ustawienia → Aplikacje (odinstalowanie). Ustawienia, historia, klucze API i hasło do bazy CDE
# (%APPDATA%\LokalneAI) zostają przy aktualizacji.
param(
    [string]$SerwerModelu = "",
    [string]$Model = "SpeakLeash/bielik-11b-v3.0-instruct:Q4_K_M",
    [switch]$BezSkrotuNaPulpicie
)

$ErrorActionPreference = "Stop"
$zrodlo = $PSScriptRoot
$cel = Join-Path $env:LOCALAPPDATA "Programs\LokalneAI"

function Krok($tekst) { Write-Host ""; Write-Host "== $tekst" -ForegroundColor Cyan }

# Skąd brać pliki: zbudowana paczka (program\ obok tego skryptu) albo klon repozytorium (instalator\ w repo).
$program = Join-Path $zrodlo "program"
$pliki = $zrodlo                      # profile\, docs\
$wersja = (Get-Content (Join-Path $zrodlo "WERSJA.txt") -ErrorAction SilentlyContinue | Select-Object -First 1)
if (-not (Test-Path $program)) {
    $repo = Split-Path $zrodlo -Parent
    $projekt = Join-Path $repo "LokalneAI.App\LokalneAI.App.csproj"
    if (-not (Test-Path $projekt)) {
        throw "Brak folderu 'program' obok instalatora ($zrodlo). Rozpakuj całą paczkę Amadeusz-<wersja>.zip i uruchom instaluj.bat z niej."
    }
    Krok "Instalacja z repozytorium ($repo) — buduję program"
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw "Do instalacji z repozytorium potrzebny jest .NET SDK 10 (https://dot.net). Bez niego użyj gotowej paczki Amadeusz-<wersja>.zip."
    }
    if (-not $wersja) {
        # Ta sama wersja, którą domyślnie buduje narzedzia\zbuduj_instalator.ps1.
        $budowa = Get-Content (Join-Path $repo "narzedzia\zbuduj_instalator.ps1") -Raw -ErrorAction SilentlyContinue
        if ($budowa -match '\$Wersja\s*=\s*"([^"]+)"') { $wersja = $Matches[1] }
    }
    if (-not $wersja) { $wersja = "0.0.0" }
    $program = Join-Path $env:TEMP "LokalneAI-instalacja\program"
    if (Test-Path $program) { Remove-Item $program -Recurse -Force }
    & dotnet publish $projekt -c Release -r win-x64 --self-contained true -nologo -v q `
        -p:Version=$wersja -p:DebugType=none -p:SatelliteResourceLanguages=pl -o $program
    if ($LASTEXITCODE -ne 0) { throw "Budowanie programu nie powiodło się (opis wyżej)." }
    $pliki = $repo
}
if (-not $wersja) { $wersja = "0.0.0" }

Krok "Amadeusz $wersja → $cel"

# 1. Program (zamknij działającą kopię, żeby dało się podmienić pliki).
Get-Process LokalneAI -ErrorAction SilentlyContinue | ForEach-Object { $_.CloseMainWindow() | Out-Null; Start-Sleep 2; if (-not $_.HasExited) { $_.Kill() } }
New-Item -ItemType Directory -Force $cel | Out-Null
Copy-Item -Path (Join-Path $program "*") -Destination $cel -Recurse -Force
# Źródła wiedzy wpisane przez użytkownika (okno „Źródła wiedzy”) przeżywają aktualizację; własne notatki w wiedza\ też
# (kopiowanie niczego nie kasuje, a pliki użytkownika mają inne nazwy niż pliki z paczki).
$zachowane = @{}
$profileCel = Join-Path $cel "profile"
if (Test-Path $profileCel) {
    Get-ChildItem $profileCel -Directory | ForEach-Object {
        $plik = Join-Path $_.FullName "zrodla.json"
        if (Test-Path $plik) { $zachowane[$plik] = [System.IO.File]::ReadAllBytes($plik) }
    }
}
foreach ($folder in @("profile", "docs")) {
    if (Test-Path (Join-Path $pliki $folder)) {
        Copy-Item -Path (Join-Path $pliki $folder) -Destination $cel -Recurse -Force
    }
}
foreach ($plik in $zachowane.Keys) { [System.IO.File]::WriteAllBytes($plik, $zachowane[$plik]) }
if ($zachowane.Count -gt 0) { Write-Host "Źródła wiedzy użytkownika zostają bez zmian ($($zachowane.Count) profili)." }
Copy-Item -Path (Join-Path $zrodlo "odinstaluj.ps1"), (Join-Path $zrodlo "CZYTAJ.txt") -Destination $cel -Force
Set-Content (Join-Path $cel "WERSJA.txt") $wersja -Encoding ASCII
# Pliki z internetu (zip z maila) Windows oznacza jako obce — zdejmujemy oznaczenie, żeby program się uruchamiał.
Get-ChildItem $cel -Recurse -File | Unblock-File
Write-Host "Skopiowano program."

# 2. Ustawienia startowe — tylko gdy użytkownik jeszcze ich nie ma (aktualizacja niczego nie nadpisuje).
$ustawienia = Join-Path $env:APPDATA "LokalneAI\ustawienia.json"
$adres = if ($SerwerModelu) { $SerwerModelu.TrimEnd('/') } else { "http://127.0.0.1:11434" }
if (-not (Test-Path $ustawienia)) {
    New-Item -ItemType Directory -Force (Split-Path $ustawienia) | Out-Null
    $json = [ordered]@{ ollamaUrl = $adres; model = $Model } | ConvertTo-Json
    [System.IO.File]::WriteAllText($ustawienia, $json, (New-Object System.Text.UTF8Encoding($false)))
    Write-Host "Ustawienia: serwer modelu $adres, model $Model."
} else {
    Write-Host "Ustawienia użytkownika zostają bez zmian ($ustawienia)."
}

# 3. Model: lokalnie przez Ollamę albo serwer w biurze.
if ($SerwerModelu) {
    Krok "Serwer modelu: $adres"
    try {
        $v = Invoke-RestMethod "$adres/api/version" -TimeoutSec 5
        Write-Host "Serwer odpowiada (Ollama $($v.version))."
    } catch {
        Write-Warning "Serwer $adres teraz nie odpowiada — program pokaże to w oknie. Adres można zmienić w Ustawieniach programu."
    }
} else {
    Krok "Ollama i model na tym komputerze"
    $ollama = Join-Path $env:LOCALAPPDATA "Programs\Ollama\ollama.exe"
    if (-not (Test-Path $ollama)) {
        $wSciezce = Get-Command ollama -ErrorAction SilentlyContinue
        if ($wSciezce) { $ollama = $wSciezce.Source }
    }
    if (-not (Test-Path $ollama)) {
        Write-Host "Instaluję Ollamę (https://ollama.com, bez praw administratora)…"
        Invoke-RestMethod https://ollama.com/install.ps1 | Invoke-Expression
        $ollama = Join-Path $env:LOCALAPPDATA "Programs\Ollama\ollama.exe"
        if (-not (Test-Path $ollama)) { throw "Nie udało się zainstalować Ollamy. Zainstaluj ją ręcznie: irm https://ollama.com/install.ps1 | iex" }
    } else {
        Write-Host "Ollama już jest: $ollama"
    }

    # Serwer Ollamy musi działać, żeby pobrać model.
    $dziala = $false
    try { Invoke-RestMethod "http://127.0.0.1:11434/api/version" -TimeoutSec 3 | Out-Null; $dziala = $true } catch { }
    if (-not $dziala) {
        $aplikacja = Join-Path (Split-Path $ollama) "ollama app.exe"
        if (Test-Path $aplikacja) { Start-Process $aplikacja } else { Start-Process $ollama -ArgumentList "serve" -WindowStyle Hidden }
        for ($i = 0; $i -lt 30 -and -not $dziala; $i++) {
            Start-Sleep 1
            try { Invoke-RestMethod "http://127.0.0.1:11434/api/version" -TimeoutSec 3 | Out-Null; $dziala = $true } catch { }
        }
    }
    if (-not $dziala) { throw "Ollama nie wystartowała. Uruchom ją z menu Start i powtórz instalację." }

    $lista = (Invoke-RestMethod "http://127.0.0.1:11434/api/tags").models.name
    if ($lista -contains $Model) {
        # Celowo bez „ollama pull” — pull na pobranym modelu podmienia go na nowszą wersję z rejestru.
        Write-Host "Model $Model już jest pobrany."
    } else {
        # Model Bielik 11B Q4_K_M to ok. 6,7 GB — sprawdzamy miejsce tam, gdzie Ollama trzyma modele.
        $folderModeli = if ($env:OLLAMA_MODELS) { $env:OLLAMA_MODELS } else { Join-Path $env:USERPROFILE ".ollama\models" }
        $dysk = [System.IO.Path]::GetPathRoot([System.IO.Path]::GetFullPath($folderModeli))
        $wolneGB = [math]::Round((New-Object System.IO.DriveInfo($dysk)).AvailableFreeSpace / 1GB, 1)
        if ($wolneGB -lt 8) {
            throw "Za mało miejsca na dysku $dysk ($wolneGB GB wolne, model potrzebuje ok. 8 GB). Zwolnij miejsce i uruchom instalację jeszcze raz."
        }

        Write-Host "Pobieram model $Model (ok. 7 GB, to potrwa; wolne miejsce: $wolneGB GB)…"
        & $ollama pull $Model
        if ($LASTEXITCODE -ne 0) { throw "Nie udało się pobrać modelu $Model." }
    }
}

# 4. Skróty i wpis do odinstalowania.
Krok "Skróty"
$exe = Join-Path $cel "LokalneAI.exe"
$powloka = New-Object -ComObject WScript.Shell
$menuStart = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"
$pulpit = [Environment]::GetFolderPath("Desktop")
# Skróty „LokalneAI” z wersji do 0.3.0 zastępuje skrót „Amadeusz” (usuwamy tylko te, które wskazują ten program).
foreach ($stary in @((Join-Path $menuStart "LokalneAI.lnk"), (Join-Path $pulpit "LokalneAI.lnk"))) {
    if ((Test-Path $stary) -and $powloka.CreateShortcut($stary).TargetPath -eq $exe) { Remove-Item $stary -Force }
}
$miejsca = @(Join-Path $menuStart "Amadeusz.lnk")
if (-not $BezSkrotuNaPulpicie) { $miejsca += Join-Path $pulpit "Amadeusz.lnk" }
foreach ($lnk in $miejsca) {
    New-Item -ItemType Directory -Force (Split-Path $lnk) | Out-Null
    $skrot = $powloka.CreateShortcut($lnk)
    $skrot.TargetPath = $exe
    $skrot.WorkingDirectory = $cel
    $skrot.Description = "Amadeusz — asystent AI biura ODS"
    $skrot.Save()
}

$klucz = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\LokalneAI"
New-Item -Force $klucz | Out-Null
Set-ItemProperty $klucz -Name DisplayName -Value "Amadeusz (LokalneAI)"
Set-ItemProperty $klucz -Name DisplayVersion -Value $wersja
Set-ItemProperty $klucz -Name Publisher -Value "Optimal Design of Structures Sp. z o.o."
Set-ItemProperty $klucz -Name InstallLocation -Value $cel
Set-ItemProperty $klucz -Name DisplayIcon -Value $exe
Set-ItemProperty $klucz -Name UninstallString -Value ("powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"" + (Join-Path $cel "odinstaluj.ps1") + "`"")
Set-ItemProperty $klucz -Name NoModify -Value 1 -Type DWord
Set-ItemProperty $klucz -Name NoRepair -Value 1 -Type DWord

Krok "Gotowe"
Write-Host "Uruchom Amadeusza z menu Start albo skrótu na pulpicie."
Write-Host "Odinstalowanie: Ustawienia → Aplikacje → Amadeusz (LokalneAI)."
