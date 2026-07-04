@echo off
setlocal
cd /d "%~dp0"
title Paradox LoL Companion - publicar exe portable

echo === Cerrando instancia previa (si hay) ===
taskkill /IM LoLAdvisor.App.exe /F >nul 2>&1
taskkill /IM "Paradox LoL Companion.exe" /F >nul 2>&1

echo.
echo === Publicando exe portable (single-file, self-contained) ===
rem Sin PublishTrimmed: WPF no soporta trimming. La compresion integra las DLL
rem nativas y de framework dentro del exe: corre en cualquier Windows x64 sin
rem tener .NET instalado.
dotnet publish src\LoLAdvisor.App\LoLAdvisor.App.csproj -c Release -r win-x64 ^
    --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:EnableCompressionInSingleFile=true ^
    -o publish
if errorlevel 1 (
    echo.
    echo *** Fallo la publicacion. Revisa los errores de arriba. ***
    pause
    exit /b 1
)

echo.
echo === Renombrando ===
if exist "publish\Paradox LoL Companion.exe" del "publish\Paradox LoL Companion.exe"
ren "publish\LoLAdvisor.App.exe" "Paradox LoL Companion.exe"

echo.
echo Listo: publish\Paradox LoL Companion.exe
echo Es portable: copialo a donde quieras (los logs se crean junto al exe).
pause
endlocal
