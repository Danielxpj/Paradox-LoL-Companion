# Instalador y auto-actualización

Paradox LoL Companion se distribuye con un **instalador** y se actualiza **solo** al abrirse.

## Cómo funciona el auto-update

Al arrancar, la app (antes de abrir la ventana principal):

1. Borra restos `*.old` de una actualización anterior.
2. Lee la última versión desde
   `https://raw.githubusercontent.com/Danielxpj/Paradox-LoL-Companion/main/version.txt`
   (timeout corto; si falla, arranca igual — *fail-open*).
3. La compara con la versión del propio exe (`<Version>` del `.csproj`).
4. Si hay una más nueva:
   - Muestra un splash y descarga el exe de la última GitHub Release desde
     `https://github.com/Danielxpj/Paradox-LoL-Companion/releases/latest/download/ParadoxLoLCompanion.exe`.
   - Renombra el exe en ejecución a `.old`, deja el nuevo en su lugar (swap en caliente,
     con rollback si algo falla), **relanza** la app y cierra la instancia vieja.

No requiere permisos de administrador porque el instalador coloca la app en
`%LocalAppData%\Programs\Paradox LoL Companion` (instalación por usuario).

> En desarrollo (con depurador adjunto) el auto-update se omite.

## Publicar una nueva versión

1. **Subí el número de versión** en dos lugares (deben coincidir):
   - `version.txt` (raíz del repo) — ej. `1.0.1`
   - `src/LoLAdvisor.App/LoLAdvisor.App.csproj` → `<Version>`, `<AssemblyVersion>`, `<FileVersion>`
2. Ejecutá **`instalador.bat`** (raíz). Genera:
   - `installer/output/ParadoxLoLCompanion-Setup-<ver>.exe` — el instalador.
   - `publish/ParadoxLoLCompanion.exe` — el **asset** para la Release (nombre sin espacios).
3. En GitHub, creá una **Release** nueva y subí `publish/ParadoxLoLCompanion.exe` como asset.
4. **Commiteá y pusheá `version.txt`** a la rama `main`.

Listo: cualquier app instalada detectará la nueva versión al abrirse y se actualizará sola.

> El orden importa poco, pero conviene subir el asset a la Release **antes** de pushear
> `version.txt`, para que ninguna app vea la versión nueva antes de que el exe esté disponible.

## Regenerar el icono

`installer/make-icon.ps1` dibuja `src/LoLAdvisor.App/img/app.ico` (tema Tactical HUD).
Correlo con **Windows PowerShell 5.1** (no pwsh 7):

```
powershell.exe -NoProfile -ExecutionPolicy Bypass -File installer\make-icon.ps1
```

## Requisitos para compilar el instalador

- .NET SDK 10.
- Inno Setup 6 (`winget install JRSoftware.InnoSetup`). `instalador.bat` lo busca en
  Program Files y en `%LocalAppData%\Programs\Inno Setup 6`.
