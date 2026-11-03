@echo off
setlocal
set "APP=%~dp0build\AnalizadorFase2.exe"
echo Preparando Fase 2: compilacion y verificaciones.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1"
if errorlevel 1 (
    echo.
    echo No se pudo generar la entrega. Si la aplicacion esta abierta, cierrala y vuelve a ejecutar este archivo.
    pause
    exit /b 1
)
start "" "%APP%"
endlocal
