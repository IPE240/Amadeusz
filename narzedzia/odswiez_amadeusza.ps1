# Buduje Amadeusz.exe (jeden plik) w głównym folderze repozytorium.
#   powershell -NoProfile -ExecutionPolicy Bypass -File narzedzia\odswiez_amadeusza.ps1 [-Skrot]
# Uruchamia się sam po każdym `dotnet build` projektu LokalneAI.App (Debug) — patrz LokalneAI.App.csproj.
# Wymaga .NET 10 Desktop Runtime na komputerze (paczka dla innych komputerów: zbuduj_instalator.ps1).
# Gdy Amadeusz.exe jest otwarty, stary plik dostaje nazwę Amadeusz.stary.exe (Windows pozwala zmienić nazwę
# działającego programu), a nowy trafia na jego miejsce; przy następnym uruchomieniu skrótu startuje nowa wersja.
param([switch]$Skrot)

$ErrorActionPreference = "Stop"
$repo = Split-Path $PSScriptRoot -Parent
$cel = Join-Path $repo "Amadeusz.exe"
$stary = Join-Path $repo "Amadeusz.stary.exe"
$tymczasowy = Join-Path $repo "LokalneAI.App\obj\amadeusz"

dotnet publish (Join-Path $repo "LokalneAI.App\LokalneAI.App.csproj") -c Release -r win-x64 --self-contained false `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none `
    -p:SatelliteResourceLanguages=pl -p:AmadeuszPublikacja=true -nologo -v q -o $tymczasowy
if ($LASTEXITCODE -ne 0) { throw "Publikacja Amadeusza nie powiodła się." }

# Stara kopia sprzed poprzedniej podmiany — usuwana, gdy już nic jej nie używa.
if (Test-Path $stary) { try { Remove-Item $stary -Force } catch { } }

$nowy = Join-Path $tymczasowy "LokalneAI.exe"
try {
    Copy-Item $nowy $cel -Force
}
catch {
    if (Test-Path $stary) { throw "Amadeusz.exe i Amadeusz.stary.exe są otwarte — zamknij okno i zbuduj ponownie." }
    Rename-Item $cel "Amadeusz.stary.exe"
    Copy-Item $nowy $cel -Force
    Write-Host "Amadeusz.exe był otwarty: nowa wersja wystartuje przy następnym uruchomieniu."
}

if ($Skrot) {
    $pulpit = [Environment]::GetFolderPath("Desktop")
    $shell = New-Object -ComObject WScript.Shell
    $lnk = $shell.CreateShortcut((Join-Path $pulpit "Amadeusz.lnk"))
    $lnk.TargetPath = $cel
    $lnk.WorkingDirectory = $repo
    $lnk.Description = "Amadeusz — lokalne AI biura"
    $lnk.Save()
    Write-Host "Skrót: $(Join-Path $pulpit 'Amadeusz.lnk')"
}

Write-Host "Gotowe: $cel"
