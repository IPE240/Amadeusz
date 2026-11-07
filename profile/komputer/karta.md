# PowerShell w biurze — ściąga

„Z podfolderami” = zawsze `-Recurse`. „W folderze X” = `-Path 'X'` (albo najpierw `cd X`).

- Nowy folder: `New-Item -ItemType Directory -Path 'Raporty'`
- Pliki z podfolderami: `Get-ChildItem -Recurse -File -Filter *.pdf | Select-Object FullName, Length, LastWriteTime`
- Ile plików: `(Get-ChildItem -Recurse -File -Filter *.pdf).Count`
- Rozmiar folderu w kB: `[math]::Round((Get-ChildItem -Recurse -File | Measure-Object Length -Sum).Sum / 1KB)`; w MB: `/ 1MB`
- Najnowsze pliki z podfolderami: `Get-ChildItem -Recurse -File | Sort-Object LastWriteTime -Descending | Select-Object -First 10 FullName, LastWriteTime`
- Tekst w plikach: `Select-String -Path *.txt -Pattern 'WSP'`
- Kopia folderu: `Copy-Item 'R01' 'R01_kopia' -Recurse`
- Zmiana nazw w folderze R01: `Get-ChildItem -Path 'R01' -File | Rename-Item -NewName { $_.Name -replace 'R01','R02' }`
- Kasowanie wskazanego pliku: `Remove-Item -LiteralPath 'stary.pdf'`
- ZIP: `Compress-Archive -Path 'R02\*' -DestinationPath 'R02.zip'`; rozpakowanie: `Expand-Archive 'paczka.zip' -DestinationPath 'paczka'`
- Lista do pliku: `Get-ChildItem -Name | Set-Content 'lista.txt'`

Teczki projektów mają nazwy `NNNN_SKRÓT_Nazwa` (np. `1095_WSP`), a rewizje to foldery `R00`, `R01`.
