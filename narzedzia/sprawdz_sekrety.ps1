# Pilnuje, żeby hasła i klucze API nie trafiły do repozytorium (i na GitHub).
# Bez parametrów: pliki dodane do commitu (tak woła go hak .git\hooks\pre-commit). -Wszystkie: całe repozytorium.
# Hasła programu leżą w %APPDATA%\LokalneAI (zaszyfrowane kontem Windows), nigdy w repozytorium.
# Wypisuje tylko plik i linię, nigdy znalezionej wartości.
param([switch]$Wszystkie)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
Set-Location (git rev-parse --show-toplevel)
$ten = 'narzedzia/sprawdz_sekrety.ps1' # sam skrypt zawiera wzorce — nie sprawdzamy go

$wzorce = @(
    @{ Opis = 'hasło w JSON ("password": "...")'; Wzorzec = '"password"\s*:\s*"(?!WPISZ)[^"]{3,}"' },
    @{ Opis = 'hasło w połączeniu (Password=...)'; Wzorzec = '(?i)\bpassword=(?![{$…])[^;"''\s,<]{4,}' },
    @{ Opis = 'klucz API Anthropic'; Wzorzec = 'sk-ant-[A-Za-z0-9_-]{20,}' },
    @{ Opis = 'klucz API OpenAI'; Wzorzec = '\bsk-(proj-)?[A-Za-z0-9]{32,}' },
    @{ Opis = 'klucz API Google'; Wzorzec = 'AIza[0-9A-Za-z_-]{30,}' },
    @{ Opis = 'adres bazy z hasłem'; Wzorzec = 'postgres(ql)?://[^:\s/]+:[^@\s]+@' }
)
$zakazanePliki = '(\.local\.json|(^|/)klucze\.json|(^|/)\.env)$'

if ($Wszystkie) {
    $pliki = git ls-files --cached --others --exclude-standard
} else {
    $pliki = git diff --cached --name-only --diff-filter=ACMR
}

$problemy = @()
foreach ($plik in $pliki) {
    if ($plik -eq $ten) { continue }
    if ($plik -match $zakazanePliki) {
        $problemy += "$plik — taki plik trzyma hasła i nie może być w repozytorium"
        continue
    }

    if ($Wszystkie) {
        if (-not (Test-Path -LiteralPath $plik -PathType Leaf)) { continue }
        $tekst = [IO.File]::ReadAllText((Resolve-Path -LiteralPath $plik))
    } else {
        $tekst = (git show ":$plik" 2>$null) -join "`n"
    }

    if ($tekst -match "`0") { continue } # plik binarny
    foreach ($w in $wzorce) {
        foreach ($m in [regex]::Matches($tekst, $w.Wzorzec)) {
            $linia = ($tekst.Substring(0, $m.Index) -split "`n").Count
            $problemy += "${plik}:$linia — $($w.Opis)"
        }
    }
}

if ($problemy.Count -gt 0) {
    Write-Host 'STOP: wygląda na hasło albo klucz API. Usuń je z pliku (hasła trzyma program w %APPDATA%\LokalneAI):' -ForegroundColor Red
    $problemy | ForEach-Object { Write-Host "  $_" }
    exit 1
}

Write-Host "Bez haseł i kluczy API ($(@($pliki).Count) plików)."
exit 0
