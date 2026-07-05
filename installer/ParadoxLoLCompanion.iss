; ============================================================================
;  Instalador de Paradox LoL Companion (Inno Setup 6)
;
;  Instala POR USUARIO (LocalAppData) y sin pedir admin, para que el
;  auto-update pueda reemplazar el exe en caliente sin elevación.
;
;  La versión se pasa desde instalador.bat con /DMyAppVersion=x.y.z
;  (o usa el valor por defecto de abajo si se compila a mano).
; ============================================================================

#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif

#define MyAppName "Paradox LoL Companion"
#define MyAppExe  "Paradox LoL Companion.exe"
#define MyAppPublisher "Danielxpj"
#define MyAppUrl "https://github.com/Danielxpj/Paradox-LoL-Companion"

[Setup]
; AppId fijo: identifica la app entre versiones (no cambiar).
AppId={{7B2F1E64-9C3A-4D28-9F1B-A1B2C3D4E5F6}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppUrl}
AppSupportURL={#MyAppUrl}
VersionInfoVersion={#MyAppVersion}

; Instalación por usuario: no requiere admin -> el auto-update puede escribir.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={localappdata}\Programs\{#MyAppName}
DisableProgramGroupPage=yes
DisableDirPage=auto

; Icono e imagen del propio instalador y del "Agregar o quitar programas".
SetupIconFile=..\src\LoLAdvisor.App\img\app.ico
UninstallDisplayIcon={app}\{#MyAppExe}
UninstallDisplayName={#MyAppName}

; Compresión y salida.
Compression=lzma2/max
SolidCompression=yes
OutputDir=output
OutputBaseFilename=ParadoxLoLCompanion-Setup-{#MyAppVersion}
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible

[Languages]
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "Crear un acceso directo en el escritorio"; GroupDescription: "Accesos directos:"

[Files]
; El exe portable single-file publicado por instalador.bat.
Source: "..\publish\{#MyAppExe}"; DestDir: "{app}"; Flags: ignoreversion
; Assets del demo de Replay (opcional pero liviano): la app funciona sin ellos.
Source: "..\publish\Assets\*"; DestDir: "{app}\Assets"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExe}"; Description: "Abrir {#MyAppName}"; Flags: nowait postinstall skipifsilent
