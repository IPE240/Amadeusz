# Buduje paczkę instalacyjną Amadeusza: dist\Amadeusz-<wersja>.zip
#   powershell -NoProfile -ExecutionPolicy Bypass -File narzedzia\zbuduj_instalator.ps1 [-Wersja 0.5.0] [-ZDokumentacjaZRepozytoriow]
# Program jest publikowany jako self-contained (win-x64): na komputerach biura nie trzeba instalować .NET.
# Pliki pobrane z repozytoriów (profile\*\wiedza\zrodla) domyślnie NIE trafiają do paczki — bywają z prywatnych
# repozytoriów; na każdym komputerze program pobierze je sam, jeśli jego użytkownik ma dostęp.
param([string]$Wersja = "0.5.0", [switch]$ZDokumentacjaZRepozytoriow)

$ErrorActionPreference = "Stop"
$repo = Split-Path $PSScriptRoot -Parent
Set-Location $repo
$nazwa = "Amadeusz-$Wersja"
$folder = Join-Path $repo "dist\$nazwa"
$zip = Join-Path $repo "dist\$nazwa.zip"
$utf8Bom = New-Object System.Text.UTF8Encoding($true)

if (Test-Path $folder) { Remove-Item $folder -Recurse -Force }
New-Item -ItemType Directory -Force $folder | Out-Null

Write-Host "[1/4] Testy..."
# -m:1 = po kolei: testy Office na net10 i net48 naraz walczą o tego samego Worda/PowerPointa (COM).
dotnet test LokalneAI.sln -nologo -v q -m:1
if ($LASTEXITCODE -ne 0) { throw "Testy nie przeszły — paczki nie buduję." }

Write-Host "[2/4] Publikacja programu (self-contained, win-x64)..."
dotnet publish LokalneAI.App\LokalneAI.App.csproj -c Release -r win-x64 --self-contained true -nologo -v q `
    -p:Version=$Wersja -p:DebugType=none -p:SatelliteResourceLanguages=pl -o (Join-Path $folder "program")
if ($LASTEXITCODE -ne 0) { throw "Publikacja nie powiodła się." }

Write-Host "[3/4] Profile, dokumentacja, skrypty instalacji..."
Copy-Item profile (Join-Path $folder "profile") -Recurse
if (-not $ZDokumentacjaZRepozytoriow) {
    # Kopia w folderze paczki (dist\...), nie w repozytorium.
    Get-ChildItem (Join-Path $folder "profile") -Directory | ForEach-Object {
        $pobrane = Join-Path $_.FullName "wiedza\zrodla"
        if (Test-Path $pobrane) { Remove-Item $pobrane -Recurse -Force }
    }
}
New-Item -ItemType Directory -Force (Join-Path $folder "docs") | Out-Null
Copy-Item docs\osadzanie.md, docs\serwer-modelu.md, docs\profile.md, README.md -Destination (Join-Path $folder "docs")
if (Test-Path docs\wybor-modelu.md) { Copy-Item docs\wybor-modelu.md (Join-Path $folder "docs") }
Copy-Item instalator\instaluj.bat, instalator\CZYTAJ.txt -Destination $folder
foreach ($skrypt in @("instaluj.ps1", "odinstaluj.ps1")) {
    # Windows PowerShell 5.1 czyta .ps1 bez BOM jako ANSI — polskie znaki wymagają UTF-8 z BOM.
    $tresc = [System.IO.File]::ReadAllText((Join-Path $repo "instalator\$skrypt"))
    [System.IO.File]::WriteAllText((Join-Path $folder $skrypt), $tresc, $utf8Bom)
}
Set-Content (Join-Path $folder "WERSJA.txt") $Wersja -Encoding ASCII

Write-Host "[4/4] Archiwum..."
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $folder -DestinationPath $zip
$mb = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host "Gotowe: $zip ($mb MB)"
