# LokalneAI — odinstalowanie. Usuwa program, skróty i wpis w Aplikacjach.
# Zostają: ustawienia i historia użytkownika (%APPDATA%\LokalneAI, %LOCALAPPDATA%\LokalneAI) oraz Ollama z modelami
# (mogą ich używać inne programy). Ollamę odinstalowuje się osobno: Ustawienia → Aplikacje → Ollama.
$cel = Join-Path $env:LOCALAPPDATA "Programs\LokalneAI"

Get-Process LokalneAI -ErrorAction SilentlyContinue | ForEach-Object { $_.CloseMainWindow() | Out-Null; Start-Sleep 2; if (-not $_.HasExited) { $_.Kill() } }
foreach ($lnk in @(
    (Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\LokalneAI.lnk"),
    (Join-Path ([Environment]::GetFolderPath("Desktop")) "LokalneAI.lnk"))) {
    if (Test-Path $lnk) { Remove-Item $lnk -Force }
}
Remove-Item "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\LokalneAI" -Recurse -Force -ErrorAction SilentlyContinue

# Folder programu kasujemy z osobnego procesu, bo ten skrypt w nim leży.
Start-Process powershell.exe -WindowStyle Hidden -ArgumentList "-NoProfile -Command Start-Sleep 2; Remove-Item -LiteralPath '$cel' -Recurse -Force"
Write-Host "LokalneAI odinstalowane. Ustawienia, historia i Ollama zostały."
