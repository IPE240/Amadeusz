@echo off
REM LokalneAI - instalacja (bez praw administratora). Parametry przekazywane do instaluj.ps1, np.:
REM   instaluj.bat -SerwerModelu http://serwer-ai:11434
REM   instaluj.bat -Model qwen3.5:4b-q4_K_M        (bez pytania o modele; kilka po przecinku)
REM   instaluj.bat -BezPytan                       (model zalecany dla tego komputera)
REM   bez parametrow: instalator pokaze liste modeli do wyboru
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0instaluj.ps1" %*
if errorlevel 1 (
    echo.
    echo Instalacja nie powiodla sie - opis bledu wyzej.
)
pause
