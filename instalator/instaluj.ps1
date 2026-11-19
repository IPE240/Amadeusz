# Amadeusz (LokalneAI) — instalacja na komputerze użytkownika (bez praw administratora).
#
#   instaluj.bat                                     model liczy na tym PC: instalator pokazuje listę modeli do pobrania
#                                                    (zalecany według pamięci karty graficznej) i instaluje Ollamę
#   instaluj.bat -SerwerModelu http://serwer-ai:11434  model liczy na serwerze w biurze (bez Ollamy na tym PC)
#   instaluj.bat -Model qwen3.5:4b-q4_K_M            bez pytania: te modele (kilka po przecinku, pierwszy = domyślny)
#   instaluj.bat -BezPytan                           bez pytania: model zalecany dla tego komputera
#
# Lista modeli i opis wyboru: docs\wybor-modelu.md.
#
# Program trafia do %LOCALAPPDATA%\Programs\LokalneAI, dostaje skrót „Amadeusz” w menu Start i na pulpicie
# oraz wpis w Ustawienia → Aplikacje (odinstalowanie). Ustawienia, historia, klucze API i hasło do bazy CDE
# (%APPDATA%\LokalneAI) zostają przy aktualizacji.
param(
    [string]$SerwerModelu = "",
    [string[]]$Model = @(),
    [switch]$BezPytan,
    [switch]$BezSkrotuNaPulpicie
)

$ErrorActionPreference = "Stop"
$zrodlo = $PSScriptRoot
$cel = Join-Path $env:LOCALAPPDATA "Programs\LokalneAI"

function Krok($tekst) { Write-Host ""; Write-Host "== $tekst" -ForegroundColor Cyan }

# Modele do wyboru przy instalacji (rozmiary z ollama.com, listopad 2026). Karta = ile GB pamięci karty graficznej
# potrzeba, żeby model liczył się w całości na karcie (z kontekstem); mniej = liczy częściowo procesor, kilka razy wolniej.
$katalog = @(
    [pscustomobject]@{ Nazwa = "SpeakLeash/bielik-11b-v3.0-instruct:Q4_K_M"; Krotko = "Bielik 11B v3"; GB = 6.7; Karta = 8
        Opis = "polski, najlepszy z Bielików; domyślny w biurze" }
    [pscustomobject]@{ Nazwa = "qwen3.5:9b-q4_K_M"; Krotko = "Qwen3.5 9B"; GB = 6.6; Karta = 8
        Opis = "najcelniejszy w Code (SQL, polecenia), najlepszy w tłumaczeniu; wolniejszy" }
    [pscustomobject]@{ Nazwa = "SpeakLeash/bielik-minitron-7B-v3.0-instruct:Q4_K_M"; Krotko = "Bielik-Minitron 7B v3"; GB = 4.5; Karta = 6
        Opis = "polski, 2-3 razy szybszy od 11B, słabszy w SQL" }
    [pscustomobject]@{ Nazwa = "qwen3.5:4b-q4_K_M"; Krotko = "Qwen3.5 4B"; GB = 3.3; Karta = 4
        Opis = "słabszy komputer (karta 4 GB); Code tylko w prostych sprawach" }
    [pscustomobject]@{ Nazwa = "qwen3.5:2b-q4_K_M"; Krotko = "Qwen3.5 2B"; GB = 1.9; Karta = 3
        Opis = "bardzo słaby komputer albo bez karty; proste pytania w Czacie" }
    [pscustomobject]@{ Nazwa = "SpeakLeash/bielik-1.5b-v3.0-instruct:Q8_0"; Krotko = "Bielik 1.5B v3"; GB = 1.7; Karta = 3
        Opis = "polski, bardzo mały; tylko Czat (bez narzędzi w Code)" }
)

# Pamięć karty graficznej w GB (największa karta). Win32_VideoController.AdapterRAM kończy się na 4 GB,
# więc czytamy wpis sterownika w rejestrze (HardwareInformation.qwMemorySize), a WMI tylko w zapasie.
function Pamiec-Karty {
    $najwiecej = 0.0
    $klasa = "HKLM:\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"
    Get-ChildItem $klasa -ErrorAction SilentlyContinue | Where-Object { $_.PSChildName -match '^\d{4}$' } | ForEach-Object {
        try {
            $wpis = Get-ItemProperty $_.PSPath -ErrorAction Stop
            $bajty = 0.0
            $qw = $wpis."HardwareInformation.qwMemorySize"
            $dw = $wpis."HardwareInformation.MemorySize"
            if ($qw) { $bajty = [double]$qw }
            elseif ($dw -is [byte[]] -and $dw.Length -ge 4) { $bajty = [double][BitConverter]::ToUInt32($dw, 0) }
            elseif ($dw) { $bajty = [double]$dw }
            if ($bajty / 1GB -gt $najwiecej) { $najwiecej = $bajty / 1GB }
        } catch { }
    }
    if ($najwiecej -le 0) {
        try {
            Get-CimInstance Win32_VideoController -ErrorAction Stop | ForEach-Object {
                if ($_.AdapterRAM / 1GB -gt $najwiecej) { $najwiecej = $_.AdapterRAM / 1GB }
            }
        } catch { }
    }
    return [math]::Round($najwiecej, 1)
}

# Modele już pobrane w Ollamie na tym PC (pusta lista, gdy Ollama nie działa).
function Pobrane-Modele {
    try { return @((Invoke-RestMethod "http://127.0.0.1:11434/api/tags" -TimeoutSec 3).models.name) } catch { return @() }
}

function Rozmiar-GB($nazwa) {
    $znany = $katalog | Where-Object { $_.Nazwa -eq $nazwa } | Select-Object -First 1
    if ($znany) { return $znany.GB } else { return 7.0 }   # nieznany model: zakładamy tyle, ile duży
}

# Lista modeli: z parametru -Model, bez pytania (zalecany) albo z wyboru w oknie instalatora.
# Pierwszy model na liście zostaje modelem domyślnym programu.
$wybraneModele = @($Model | ForEach-Object { $_ -split '[,;\s]+' } | Where-Object { $_ })
$modeleZWyboru = $wybraneModele.Count -gt 0
if (-not $SerwerModelu -and $wybraneModele.Count -eq 0) {
    $karta = Pamiec-Karty
    $ramGB = 0
    try { $ramGB = [math]::Round((Get-CimInstance Win32_ComputerSystem -ErrorAction Stop).TotalPhysicalMemory / 1GB) } catch { }
    $zalecany = $katalog | Where-Object { $_.Karta -le [math]::Max($karta + 0.3, 3) } | Select-Object -First 1
    if (-not $zalecany) { $zalecany = $katalog[-2] }
    $pobrane = Pobrane-Modele
    $interaktywnie = -not $BezPytan -and -not [Console]::IsInputRedirected

    if (-not $interaktywnie) {
        $wybraneModele = @($zalecany.Nazwa)
    } else {
        Krok "Które modele językowe pobrać na ten komputer?"
        $opisKarty = if ($karta -gt 0) { "$karta GB" } else { "nie rozpoznano" }
        Write-Host "Karta graficzna: $opisKarty pamięci; pamięć RAM: $ramGB GB."
        Write-Host ""
        for ($i = 0; $i -lt $katalog.Count; $i++) {
            $m = $katalog[$i]
            $znak = if ($m -eq $zalecany) { "*" } else { " " }
            $stan = if ($pobrane -contains $m.Nazwa) { "  (już pobrany)" } else { "" }
            $kolor = if ($m.Karta -gt [math]::Max($karta + 0.3, 3)) { "DarkGray" } else { "Gray" }
            Write-Host ("{0}[{1}] {2,-22} {3,4:N1} GB  {4}{5}" -f $znak, ($i + 1), $m.Krotko, $m.GB, $m.Opis, $stan) -ForegroundColor $kolor
        }
        Write-Host ""
        Write-Host "* = zalecany dla tego komputera; szare = za duże dla tej karty (policzy, ale kilka razy wolniej)."
        Write-Host "Wpisz numery po przecinku (np. 4,6) — pierwszy będzie domyślnym modelem programu."
        Write-Host "Można też wpisać pełną nazwę innego modelu z ollama.com."
        $cosPobrane = @($pobrane).Count -gt 0
        if ($cosPobrane) {
            Write-Host "Enter = nic nowego nie pobieraj (na tym komputerze są już modele); 0 = to samo."
        } else {
            Write-Host "Enter = zalecany ($([array]::IndexOf($katalog, $zalecany) + 1). $($zalecany.Krotko)); 0 = bez modelu (wybierzesz później albo użyjesz chmury)."
        }
        while ($true) {
            $odpowiedz = (Read-Host "Modele").Trim()
            if ($odpowiedz -eq "") {
                $wybraneModele = if ($cosPobrane) { @() } else { @($zalecany.Nazwa) }
                break
            }
            if ($odpowiedz -eq "0") { $wybraneModele = @(); break }
            $lista = @(); $blad = $null
            foreach ($czesc in ($odpowiedz -split '[,;\s]+' | Where-Object { $_ })) {
                if ($czesc -match '^\d+$') {
                    $nr = [int]$czesc
                    if ($nr -lt 1 -or $nr -gt $katalog.Count) { $blad = "Nie ma pozycji $nr (są 1–$($katalog.Count))."; break }
                    $lista += $katalog[$nr - 1].Nazwa
                } elseif ($czesc -match '^[\w./:-]+$') {
                    $lista += $czesc
                } else { $blad = "Nie rozumiem: $czesc (to nie numer ani nazwa modelu)."; break }
            }
            if ($blad) { Write-Host $blad -ForegroundColor Yellow; continue }
            $wybraneModele = @($lista | Select-Object -Unique)
            break
        }
        $modeleZWyboru = $wybraneModele.Count -gt 0
        foreach ($nazwa in $wybraneModele) {
            $m = $katalog | Where-Object { $_.Nazwa -eq $nazwa } | Select-Object -First 1
            if ($m -and $karta -gt 0 -and $m.Karta -gt $karta + 0.3) {
                Write-Warning "$($m.Krotko) potrzebuje ok. $($m.Karta) GB pamięci karty, a ta ma $karta GB — będzie liczył częściowo na procesorze, kilka razy wolniej."
            }
        }
        if ($wybraneModele.Count -gt 0) { Write-Host ("Wybrane: " + ($wybraneModele -join ", ")) -ForegroundColor Green }
        else { Write-Host "Bez pobierania modeli." -ForegroundColor Green }
    }
}

# Po wyborze lista bywa $null albo napisem (PowerShell rozpakowuje tablice z if) — zawsze tablica bez pustych nazw.
$wybraneModele = @($wybraneModele | Where-Object { $_ })

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

$adres = if ($SerwerModelu) { $SerwerModelu.TrimEnd('/') } else { "http://127.0.0.1:11434" }

# 2. Model: lokalnie przez Ollamę albo serwer w biurze.
$nieudane = @()
$lista = @()
if ($SerwerModelu) {
    Krok "Serwer modelu: $adres"
    try {
        $v = Invoke-RestMethod "$adres/api/version" -TimeoutSec 5
        Write-Host "Serwer odpowiada (Ollama $($v.version))."
    } catch {
        Write-Warning "Serwer $adres teraz nie odpowiada — program pokaże to w oknie. Adres można zmienić w Ustawieniach programu."
    }
} else {
    Krok "Ollama i modele na tym komputerze"
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

    $lista = Pobrane-Modele
    # Celowo bez „ollama pull” dla pobranych — pull na pobranym modelu podmienia go na nowszą wersję z rejestru.
    foreach ($nazwa in $wybraneModele) { if ($lista -contains $nazwa) { Write-Host "Model $nazwa już jest pobrany." } }
    $doPobrania = @($wybraneModele | Where-Object { $lista -notcontains $_ })
    if ($doPobrania.Count -gt 0) {
        # Miejsce sprawdzamy tam, gdzie Ollama trzyma modele: suma rozmiarów + 1 GB zapasu.
        $folderModeli = if ($env:OLLAMA_MODELS) { $env:OLLAMA_MODELS } else { Join-Path $env:USERPROFILE ".ollama\models" }
        $dysk = [System.IO.Path]::GetPathRoot([System.IO.Path]::GetFullPath($folderModeli))
        $wolneGB = [math]::Round((New-Object System.IO.DriveInfo($dysk)).AvailableFreeSpace / 1GB, 1)
        $potrzebaGB = [math]::Round((($doPobrania | ForEach-Object { Rozmiar-GB $_ }) | Measure-Object -Sum).Sum + 1, 1)
        if ($wolneGB -lt $potrzebaGB) {
            throw "Za mało miejsca na dysku $dysk ($wolneGB GB wolne, wybrane modele potrzebują ok. $potrzebaGB GB). Zwolnij miejsce albo wybierz mniej modeli i uruchom instalację jeszcze raz."
        }
        $nr = 0
        foreach ($nazwa in $doPobrania) {
            $nr++
            Write-Host ""
            Write-Host ("Pobieram model {0}/{1}: {2} (ok. {3:N1} GB, to potrwa)…" -f $nr, $doPobrania.Count, $nazwa, (Rozmiar-GB $nazwa))
            & $ollama pull $nazwa
            if ($LASTEXITCODE -ne 0) { Write-Warning "Nie udało się pobrać modelu $nazwa."; $nieudane += $nazwa }
        }
        $lista = Pobrane-Modele
    }
}

# 3. Ustawienia startowe. Nowy użytkownik: adres serwera, model domyślny (pierwszy wybrany) i wybrane modele jako ulubione.
# Aktualizacja niczego nie nadpisuje — wyjątek: gdy model domyślny użytkownika nie istnieje w Ollamie na tym PC,
# a w instalatorze wybrano modele, model domyślny staje się pierwszym z nich.
$ustawienia = Join-Path $env:APPDATA "LokalneAI\ustawienia.json"
$dostepne = if ($SerwerModelu) { @($wybraneModele) } else { @($wybraneModele | Where-Object { $lista -contains $_ }) }
$domyslny = $dostepne | Select-Object -First 1
if (-not $domyslny -and -not $SerwerModelu) { $domyslny = $lista | Select-Object -First 1 }
$utf8 = New-Object System.Text.UTF8Encoding($false)
if (-not (Test-Path $ustawienia)) {
    New-Item -ItemType Directory -Force (Split-Path $ustawienia) | Out-Null
    $poczatkowe = [ordered]@{ ollamaUrl = $adres }
    if ($domyslny) { $poczatkowe.model = $domyslny }
    if ($dostepne.Count -gt 0) { $poczatkowe.favoriteModels = @($dostepne) }
    [System.IO.File]::WriteAllText($ustawienia, ($poczatkowe | ConvertTo-Json), $utf8)
    $opisModelu = if ($domyslny) { $domyslny } else { "brak — wybierz w Ustawieniach programu" }
    Write-Host "Ustawienia: serwer modelu $adres, model domyślny $opisModelu."
} else {
    $tekst = [System.IO.File]::ReadAllText($ustawienia)
    $wzor = '(?m)^  "model": "([^"]*)"'
    $obecny = if ($tekst -match $wzor) { $Matches[1] } else { $null }
    if (-not $SerwerModelu -and $modeleZWyboru -and $obecny -and $domyslny -and $lista -notcontains $obecny) {
        $tekst = ([regex]$wzor).Replace($tekst, '  "model": "' + $domyslny + '"', 1)
        [System.IO.File]::WriteAllText($ustawienia, $tekst, $utf8)
        Write-Host "Ustawienia użytkownika zostają; zmieniony tylko model domyślny: $obecny (nie ma go na tym komputerze) → $domyslny."
    } else {
        Write-Host "Ustawienia użytkownika zostają bez zmian ($ustawienia)."
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
if ($nieudane.Count -gt 0) {
    Write-Warning ("Nie pobrały się: " + ($nieudane -join ", ") + ". Program działa; model pobierzesz, uruchamiając instalację jeszcze raz (sprawdź internet i miejsce na dysku).")
}
Write-Host "Uruchom Amadeusza z menu Start albo skrótu na pulpicie."
Write-Host "Odinstalowanie: Ustawienia → Aplikacje → Amadeusz (LokalneAI)."
