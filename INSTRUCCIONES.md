# Paradox LoL Companion — Instrucciones (instalador + auto-update)

Guía práctica para **compilar el instalador** y **publicar actualizaciones** que la app
descarga sola. Para el detalle técnico de cómo funciona el auto-update, ver
[`docs/actualizaciones.md`](docs/actualizaciones.md).

---

## Requisitos (una sola vez)

- **.NET SDK 10**
- **Inno Setup 6** — instalalo con:
  ```
  winget install JRSoftware.InnoSetup
  ```
  `instalador.bat` lo busca en Program Files y en `%LocalAppData%\Programs\Inno Setup 6`.
- **git** + una cuenta con push al repo `Danielxpj/LoLAdvisor`.
- *(Opcional pero recomendado)* **GitHub CLI** (`gh`) para crear Releases desde la terminal:
  ```
  winget install GitHub.cli
  gh auth login
  ```

---

## Generar el instalador

Doble clic (o desde terminal) a **`instalador.bat`** en la raíz del repo. Hace todo:

1. Publica el exe portable (single-file, self-contained — corre sin .NET instalado).
2. Lo renombra a `Paradox LoL Companion.exe` y saca una copia sin espacios
   `ParadoxLoLCompanion.exe` (el asset del auto-update).
3. Compila el instalador.

**Resultado:**

| Archivo | Para qué |
|---|---|
| `installer/output/ParadoxLoLCompanion-Setup-<ver>.exe` | El **instalador** que le pasás a la gente |
| `publish/ParadoxLoLCompanion.exe` | El **asset** que subís a la GitHub Release (auto-update) |

> El instalador coloca la app en `%LocalAppData%\Programs\Paradox LoL Companion`
> (por-usuario, sin pedir admin) para que el auto-update pueda reemplazar el exe.

---

## Publicar una actualización (el flujo importante)

Cada vez que quieras sacar una versión nueva:

### 1. Subí el número de versión en **los dos** lugares (deben coincidir)

- `version.txt` (raíz) — ej. `1.0.1`
- `src/LoLAdvisor.App/LoLAdvisor.App.csproj` → `<Version>`, `<AssemblyVersion>`, `<FileVersion>`

### 2. Generá el instalador

```
instalador.bat
```

### 3. Creá la GitHub Release y subí el asset

Con GitHub CLI (reemplazá la versión):

```
gh release create v1.0.1 "publish/ParadoxLoLCompanion.exe" --title "v1.0.1" --notes "Cambios de esta versión"
```

O a mano: GitHub → **Releases** → *Draft a new release* → tag `v1.0.1` → arrastrás
`publish/ParadoxLoLCompanion.exe` como asset → *Publish release*.

### 4. Pusheá `version.txt` a `main`

```
git add version.txt src/LoLAdvisor.App/LoLAdvisor.App.csproj
git commit -m "release: v1.0.1"
git push origin main
```

**Orden recomendado:** primero subí el asset a la Release (paso 3), después pusheá
`version.txt` (paso 4). Así ninguna app ve la versión nueva antes de que el exe exista.

Listo: cualquier app ya instalada, al abrirse, verá la versión nueva y **se actualizará sola**.

---

## Cómo se actualiza la app (resumen)

Al abrir, **antes** de mostrar la ventana:

1. Lee `version.txt` desde `raw.githubusercontent.com/Danielxpj/LoLAdvisor/main/version.txt`.
2. Si hay una versión mayor a la instalada, muestra un splash y descarga el exe de la
   última Release.
3. Reemplaza el exe en caliente y **relanza**.

Si no hay internet o falla algo, **arranca igual** con la versión que ya tenía (nunca
te deja sin app). En desarrollo (con depurador) el auto-update se omite.

---

## Regenerar el icono

`installer/make-icon.ps1` dibuja `src/LoLAdvisor.App/img/app.ico`. Correlo con
**Windows PowerShell 5.1** (no pwsh 7, que no trae System.Drawing):

```
powershell.exe -NoProfile -ExecutionPolicy Bypass -File installer\make-icon.ps1
```

---

## Problemas comunes

| Síntoma | Causa / solución |
|---|---|
| `instalador.bat` dice que no encuentra Inno Setup | `winget install JRSoftware.InnoSetup` y volvé a correr |
| La app no se actualiza | Revisá que `version.txt` en `main` tenga un número **mayor** al instalado y que la Release tenga el asset `ParadoxLoLCompanion.exe` |
| Quiero ver qué pasó con el update | Log en `logs\session.log` junto al exe (línea `[Update] ...`) |
| Falla al reemplazar el exe (permisos) | La app debe estar instalada por-usuario (el instalador ya lo hace). Si copiaste el exe a `Program Files` a mano, el auto-update no tendrá permiso de escritura |
| El número de versión no cambia | Actualizá **también** el `.csproj`, no solo `version.txt` |
