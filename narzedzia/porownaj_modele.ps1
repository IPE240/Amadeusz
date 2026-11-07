# Porównanie modeli na zestawach testów obu profili.
# Uruchom z folderu LokalneAI:  powershell -NoProfile -ExecutionPolicy Bypass -File narzedzia\porownaj_modele.ps1
# Wymaga: zbudowanego LokalneAI.Eval, Ollamy z pobranymi modelami, pliku połączenia z bazą CDE (tylko do odczytu).
param(
    [string[]]$Modele = @(
        "SpeakLeash/bielik-11b-v3.0-instruct:Q4_K_M",
        "SpeakLeash/bielik-minitron-7B-v3.0-instruct:Q4_K_M",
        "qwen3.5:9b-q4_K_M",
        "gemma4:e4b-it-q4_K_M",
        "qwen3.5:4b-q4_K_M",
        "gemma4:26b-a4b-it-q4_K_M"
    ),
    [string[]]$Profile = @("tekla", "cde"),
    [int]$Kontekst = 16384,
    [string]$Polaczenie = "C:\ODS\cde\cdeReposForAI\rozmowa_z_baza_cde\polaczenie.local.json",
    # person_id pytającego w CDE (do pytań „moje godziny”); bez niego profil cde jest pomijany.
    [string]$Ja = $env:LOKALNEAI_JA,
    [string]$Wyniki = "wyniki\porownanie"
)

$eval = Join-Path $PSScriptRoot "..\LokalneAI.Eval\bin\Debug\net10.0\LokalneAI.Eval.exe"
New-Item -ItemType Directory -Force $Wyniki | Out-Null
foreach ($model in $Modele) {
    foreach ($profil in $Profile) {
        if ($profil -eq "cde" -and -not $Ja) {
            Write-Warning "Pomijam profil cde: podaj -Ja <person_id> albo ustaw zmienną LOKALNEAI_JA."
            continue
        }

        Write-Host "=== $model / $profil  $(Get-Date -Format HH:mm:ss)"
        $argumenty = @("test", "--profil", "profile\$profil", "--model", $model, "--ctx", $Kontekst, "--wyniki", $Wyniki)
        if ($profil -eq "cde") { $argumenty += @("--polaczenie", $Polaczenie, "--ja", $Ja) }
        & $eval @argumenty | Select-Object -Last 18
    }
    # Zwolnij pamięć karty przed następnym modelem.
    $body = @{ model = $model; keep_alive = 0 } | ConvertTo-Json -Compress
    try { Invoke-RestMethod -Method Post -Uri "http://127.0.0.1:11434/api/generate" -Body $body -ContentType "application/json" | Out-Null } catch { }
}
Write-Host "KONIEC $(Get-Date -Format HH:mm:ss)"
