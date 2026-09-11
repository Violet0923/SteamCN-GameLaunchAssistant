; Build through scripts/Publish-Release.ps1.
#ifndef MyAppVersion
  #error MyAppVersion must be supplied by the release script
#endif
#ifndef SourceDir
  #error SourceDir must be supplied by the release script
#endif
#define MyAppName "Steam国服游戏启动助手"
#define MyAppIdName "SteamCN-GameLaunchAssistant"
#define MyAppExeName MyAppIdName + ".exe"
#define MyAppURL "https://github.com/Violet0923/SteamCN-GameLaunchAssistant"

[Setup]
; Retain the installer ID so existing installations are upgraded in place.
AppId={{D8A9B0C0-7DA3-46C5-BA74-10D9408A7A11}
; Detect both old and renamed running builds before replacing files.
AppMutex=Local\WetheringWavesSteamHelper_WinUI_SingleInstance
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=KAMITSUBAKI METAVERSE R&D DIV
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases/latest
DefaultDirName={autopf}\{#MyAppIdName}
UsePreviousAppDir=yes
UninstallDisplayIcon={app}\{#MyAppExeName}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=yes
OutputBaseFilename={#MyAppIdName}-v{#MyAppVersion}-win-x64-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern windows11
SetupIconFile=Assets\Icons\SteamCN-GameLaunchAssistant.ico
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimplified"; MessagesFile: "packaging\languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Dirs]
; 用户背景单独授权写入；升级、卸载时保留用户导入的图片。
Name: "{app}\Backgrounds"; Permissions: users-modify; Flags: uninsneveruninstall
Name: "{app}\logs"; Permissions: users-modify
; 一键更新的 ACF 滚动备份；升级和卸载时保留，便于用户恢复。
Name: "{app}\backups"; Permissions: users-modify; Flags: uninsneveruninstall

[InstallDelete]
; Remove only obsolete application binaries and shortcuts, never user data or logs.
Type: files; Name: "{app}\WetheringWavesSteamHelper_WinUI.exe"
Type: files; Name: "{app}\WetheringWavesSteamHelper_WinUI.dll"
Type: files; Name: "{app}\WetheringWavesSteamHelper_WinUI.deps.json"
Type: files; Name: "{app}\WetheringWavesSteamHelper_WinUI.runtimeconfig.json"
Type: files; Name: "{app}\WetheringWavesSteamHelper_WinUI.pri"
Type: files; Name: "{app}\WetheringWavesSteamHelper_WinUI.pdb"
Type: files; Name: "{app}\Assets\Icons\WutheringWavesSteamHelper.ico"
Type: files; Name: "{autoprograms}\鸣潮 Steam 助手.lnk"
Type: files; Name: "{autodesktop}\鸣潮 Steam 助手.lnk"

[Files]
; Include new dependencies automatically; exclude development symbols and logs.
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "*.pdb,*.log"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
