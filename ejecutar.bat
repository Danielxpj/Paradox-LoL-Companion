@echo off
setlocal
cd /d "%~dp0"
title LoL Advisor - compilar y ejecutar

echo === Cerrando instancia previa (si hay) ===
taskkill /IM LoLAdvisor.App.exe /F >nul 2>&1

echo.
echo === Compilando LoL Advisor ===
dotnet build LoLAdvisor.slnx -c Debug --nologo
if errorlevel 1 (
    echo.
    echo *** Fallo la compilacion. Revisa los errores de arriba. ***
    pause
    exit /b 1
)

echo.
echo === Ejecutando ===
start "" "src\LoLAdvisor.App\bin\Debug\net10.0-windows\LoLAdvisor.App.exe"

echo Listo. La ventana de LoL Advisor deberia estar abierta.
endlocal
