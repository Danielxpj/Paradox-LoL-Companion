@echo off
setlocal enabledelayedexpansion
cd /d "%~dp0"
title Paradox LoL Companion - generar instalador

echo === Cerrando instancia previa (si hay) ===
taskkill /IM "Paradox LoL Companion.exe" /F >nul 2>&1
taskkill /IM LoLAdvisor.App.exe /F >nul 2>&1

set /p APPVER=<version.txt
echo Version a empaquetar: !APPVER!

echo.
echo === 1/3  Publicando exe portable (single-file, self-contained) ===
dotnet publish src\LoLAdvisor.App\LoLAdvisor.App.csproj -c Release -r win-x64 ^
    --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:EnableCompressionInSingleFile=true ^
    -p:DebugType=None -p:DebugSymbols=false ^
    -o publish
if errorlevel 1 (
    echo.
    echo *** Fallo la publicacion. Revisa los errores de arriba. ***
    pause
    exit /b 1
)

echo.
echo === Renombrando exe ===
if exist "publish\Paradox LoL Companion.exe" del "publish\Paradox LoL Companion.exe"
ren "publish\LoLAdvisor.App.exe" "Paradox LoL Companion.exe"

rem Copia sin espacios: es el asset que se sube a la GitHub Release y que el
rem auto-update descarga desde releases/latest/download/ParadoxLoLCompanion.exe
copy /y "publish\Paradox LoL Companion.exe" "publish\ParadoxLoLCompanion.exe" >nul

echo.
echo === 2/3  Buscando Inno Setup (ISCC) ===
set "ISCC="
if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"
if exist "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" set "ISCC=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"
if not defined ISCC (
    echo.
    echo *** No se encontro Inno Setup 6. ***
    echo   Instalalo con:   winget install JRSoftware.InnoSetup
    echo   o descargalo de: https://jrsoftware.org/isdl.php
    echo.
    echo   El exe portable ya quedo en:  publish\Paradox LoL Companion.exe
    pause
    exit /b 1
)
echo Usando: "!ISCC!"

echo.
echo === 3/3  Compilando instalador ===
"!ISCC!" /DMyAppVersion=!APPVER! "installer\ParadoxLoLCompanion.iss"
if errorlevel 1 (
    echo.
    echo *** Fallo la compilacion del instalador. ***
    pause
    exit /b 1
)

echo.
echo ============================================================
echo  Listo.
echo    Instalador:              installer\output\ParadoxLoLCompanion-Setup-!APPVER!.exe
echo    Asset para la Release:   publish\ParadoxLoLCompanion.exe
echo.
echo  Para publicar una ACTUALIZACION:
echo    1) Subi el numero en version.txt y en el .csproj (Version/AssemblyVersion/FileVersion)
echo    2) Corre este instalador.bat
echo    3) Crea una GitHub Release y subi  publish\ParadoxLoLCompanion.exe  como asset
echo    4) Commitea y pushea version.txt a la rama main
echo  La app instalada detectara la nueva version al abrir y se actualizara sola.
echo ============================================================
pause
endlocal
