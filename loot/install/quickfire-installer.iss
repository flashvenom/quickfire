#define MyAppName "Openfire"
#define MyAppVersion "1.2.0"
#define MyAppPublisher "flashvenom"
#define MyAppURL "https://www.quickfireams.com/"
#define MyAppExeName "Quickfire.Desktop.exe"
#define RootDir ExtractFileDir(ExtractFileDir(ExtractFileDir(SourcePath)))
#define InstallerDir RootDir + "\\loot\\install"
#define BuildDir RootDir + "\\build"
#define PublishDir BuildDir + "\\desktop"

[Setup]
AppId={{6ED12FAC-0A41-4613-A3F8-A1C8CBC23D78}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={localappdata}\flashvenom\openfire
DisableDirPage=yes
UninstallDisplayIcon={app}\{#MyAppExeName}
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
DisableProgramGroupPage=yes
LicenseFile={#InstallerDir}\license-agreement.rtf
InfoBeforeFile={#InstallerDir}\welcome-screen.rtf
WizardSmallImageFile={#InstallerDir}\installer-graphic-stemp.bmp
WizardImageFile={#InstallerDir}\installer-graphic-done.bmp
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=commandline
OutputDir={#BuildDir}\installer
OutputBaseFilename=Install_Openfire
SetupIconFile={#InstallerDir}\installer-icon.ico
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
