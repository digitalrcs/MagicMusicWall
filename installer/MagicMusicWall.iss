#ifndef AppVersion
  #define AppVersion "0.9.12"
#endif
#ifndef PayloadDirectory
  #error PayloadDirectory must point to a clean self-contained published application.
#endif
#ifndef ReleaseOutputDirectory
  #define ReleaseOutputDirectory "..\artifacts\release\" + AppVersion
#endif

[Setup]
AppId={{6B57A0A4-EB92-4996-BD9A-2F722366832C}
AppName=Magic Music Wall
AppVersion={#AppVersion}
AppVerName=Magic Music Wall {#AppVersion}
AppPublisher=DigitalRCS.com
AppPublisherURL=https://digitalrcs.com
AppSupportURL=https://github.com/digitalrcs/MagicMusicWall/issues
AppUpdatesURL=https://github.com/digitalrcs/MagicMusicWall/releases
AppCopyright=Copyright (c) 2026 DigitalRCS.com
VersionInfoCompany=DigitalRCS.com
VersionInfoDescription=Magic Music Wall Windows Installer
VersionInfoProductName=Magic Music Wall
VersionInfoVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\MagicMusicWall
DefaultGroupName=Magic Music Wall
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
MinVersion=10.0.22000
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
Uninstallable=yes
UninstallDisplayName=Magic Music Wall
UninstallDisplayIcon={app}\Assets\DigitalRCS.ico
OutputDir={#ReleaseOutputDirectory}
OutputBaseFilename=MagicMusicWall-{#AppVersion}-Setup-x64
SetupIconFile=assets\DigitalRCS.ico
WizardStyle=modern
WizardImageFile=assets\wizard-side.png
WizardSmallImageFile=assets\wizard-small.png
WizardImageBackColor=$181210
WizardSmallImageBackColor=$FFFFFF
DisableWelcomePage=no
InfoBeforeFile=Installation.txt
Compression=lzma2
SolidCompression=yes
CloseApplications=yes
CloseApplicationsFilter=*.exe,*.dll
RestartApplications=no
SetupLogging=yes
ChangesAssociations=yes
ChangesEnvironment=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

Name: "associatefiles"; Description: "Register music file types with MagicMusicWall (choose defaults in Windows)"; GroupDescription: "File associations:"

[Files]
; User data is outside {app}. Never add an UninstallDelete rule for it.
; Portable launchers/markers are distributed in the portable ZIP only.
Source: "{#PayloadDirectory}\*"; DestDir: "{app}"; Excludes: "Start-MagicMusicWall.cmd,portable.flag,Data\*"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "assets\DigitalRCS.ico"; DestDir: "{app}\Assets"; Flags: ignoreversion

[Registry]
; Register choices per user; Windows owns the user's default-app selection.
Root: HKCU; Subkey: "Software\Classes\MagicMusicWall.Audio"; ValueType: string; ValueName: ""; ValueData: "MagicMusicWall music file"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKCU; Subkey: "Software\Classes\MagicMusicWall.Audio\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\MagicMusicWall.exe"",0"; Tasks: associatefiles
Root: HKCU; Subkey: "Software\Classes\MagicMusicWall.Audio\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\MagicMusicWall.exe"" --play ""%1"""; Tasks: associatefiles
Root: HKCU; Subkey: "Software\MagicMusicWall\Capabilities"; ValueType: string; ValueName: "ApplicationName"; ValueData: "MagicMusicWall"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKCU; Subkey: "Software\MagicMusicWall\Capabilities"; ValueType: string; ValueName: "ApplicationDescription"; ValueData: "Play music files with Magic Music Wall."; Tasks: associatefiles
Root: HKCU; Subkey: "Software\RegisteredApplications"; ValueType: string; ValueName: "MagicMusicWall"; ValueData: "Software\MagicMusicWall\Capabilities"; Flags: uninsdeletevalue; Tasks: associatefiles
Root: HKCU; Subkey: "Software\Classes\.mp3\OpenWithProgids"; ValueType: string; ValueName: "MagicMusicWall.Audio"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles
Root: HKCU; Subkey: "Software\MagicMusicWall\Capabilities\FileAssociations"; ValueType: string; ValueName: ".mp3"; ValueData: "MagicMusicWall.Audio"; Tasks: associatefiles
Root: HKCU; Subkey: "Software\Classes\.flac\OpenWithProgids"; ValueType: string; ValueName: "MagicMusicWall.Audio"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles
Root: HKCU; Subkey: "Software\MagicMusicWall\Capabilities\FileAssociations"; ValueType: string; ValueName: ".flac"; ValueData: "MagicMusicWall.Audio"; Tasks: associatefiles
Root: HKCU; Subkey: "Software\Classes\.wav\OpenWithProgids"; ValueType: string; ValueName: "MagicMusicWall.Audio"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles
Root: HKCU; Subkey: "Software\MagicMusicWall\Capabilities\FileAssociations"; ValueType: string; ValueName: ".wav"; ValueData: "MagicMusicWall.Audio"; Tasks: associatefiles
Root: HKCU; Subkey: "Software\Classes\.wave\OpenWithProgids"; ValueType: string; ValueName: "MagicMusicWall.Audio"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles
Root: HKCU; Subkey: "Software\MagicMusicWall\Capabilities\FileAssociations"; ValueType: string; ValueName: ".wave"; ValueData: "MagicMusicWall.Audio"; Tasks: associatefiles
Root: HKCU; Subkey: "Software\Classes\.aac\OpenWithProgids"; ValueType: string; ValueName: "MagicMusicWall.Audio"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles
Root: HKCU; Subkey: "Software\MagicMusicWall\Capabilities\FileAssociations"; ValueType: string; ValueName: ".aac"; ValueData: "MagicMusicWall.Audio"; Tasks: associatefiles
Root: HKCU; Subkey: "Software\Classes\.m4a\OpenWithProgids"; ValueType: string; ValueName: "MagicMusicWall.Audio"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles
Root: HKCU; Subkey: "Software\MagicMusicWall\Capabilities\FileAssociations"; ValueType: string; ValueName: ".m4a"; ValueData: "MagicMusicWall.Audio"; Tasks: associatefiles
Root: HKCU; Subkey: "Software\Classes\.ogg\OpenWithProgids"; ValueType: string; ValueName: "MagicMusicWall.Audio"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles
Root: HKCU; Subkey: "Software\MagicMusicWall\Capabilities\FileAssociations"; ValueType: string; ValueName: ".ogg"; ValueData: "MagicMusicWall.Audio"; Tasks: associatefiles
Root: HKCU; Subkey: "Software\Classes\.oga\OpenWithProgids"; ValueType: string; ValueName: "MagicMusicWall.Audio"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles
Root: HKCU; Subkey: "Software\MagicMusicWall\Capabilities\FileAssociations"; ValueType: string; ValueName: ".oga"; ValueData: "MagicMusicWall.Audio"; Tasks: associatefiles
Root: HKCU; Subkey: "Software\Classes\.opus\OpenWithProgids"; ValueType: string; ValueName: "MagicMusicWall.Audio"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles
Root: HKCU; Subkey: "Software\MagicMusicWall\Capabilities\FileAssociations"; ValueType: string; ValueName: ".opus"; ValueData: "MagicMusicWall.Audio"; Tasks: associatefiles
Root: HKCU; Subkey: "Software\Classes\.wma\OpenWithProgids"; ValueType: string; ValueName: "MagicMusicWall.Audio"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles
Root: HKCU; Subkey: "Software\MagicMusicWall\Capabilities\FileAssociations"; ValueType: string; ValueName: ".wma"; ValueData: "MagicMusicWall.Audio"; Tasks: associatefiles
Root: HKCU; Subkey: "Software\Classes\.aiff\OpenWithProgids"; ValueType: string; ValueName: "MagicMusicWall.Audio"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles
Root: HKCU; Subkey: "Software\MagicMusicWall\Capabilities\FileAssociations"; ValueType: string; ValueName: ".aiff"; ValueData: "MagicMusicWall.Audio"; Tasks: associatefiles
Root: HKCU; Subkey: "Software\Classes\.aif\OpenWithProgids"; ValueType: string; ValueName: "MagicMusicWall.Audio"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles
Root: HKCU; Subkey: "Software\MagicMusicWall\Capabilities\FileAssociations"; ValueType: string; ValueName: ".aif"; ValueData: "MagicMusicWall.Audio"; Tasks: associatefiles
Root: HKCU; Subkey: "Software\Classes\.alac\OpenWithProgids"; ValueType: string; ValueName: "MagicMusicWall.Audio"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles
Root: HKCU; Subkey: "Software\MagicMusicWall\Capabilities\FileAssociations"; ValueType: string; ValueName: ".alac"; ValueData: "MagicMusicWall.Audio"; Tasks: associatefiles
Root: HKCU; Subkey: "Software\Classes\.ape\OpenWithProgids"; ValueType: string; ValueName: "MagicMusicWall.Audio"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles
Root: HKCU; Subkey: "Software\MagicMusicWall\Capabilities\FileAssociations"; ValueType: string; ValueName: ".ape"; ValueData: "MagicMusicWall.Audio"; Tasks: associatefiles

; Opting out also removes registrations made by a previous version.
Root: HKCU; Subkey: "Software\Classes\MagicMusicWall.Audio"; ValueType: none; Flags: deletekey; Tasks: not associatefiles
Root: HKCU; Subkey: "Software\MagicMusicWall\Capabilities"; ValueType: none; Flags: deletekey; Tasks: not associatefiles
Root: HKCU; Subkey: "Software\RegisteredApplications"; ValueName: "MagicMusicWall"; ValueType: none; Flags: deletevalue; Tasks: not associatefiles
Root: HKCU; Subkey: "Software\Classes\.mp3\OpenWithProgids"; ValueName: "MagicMusicWall.Audio"; ValueType: none; Flags: deletevalue; Tasks: not associatefiles
Root: HKCU; Subkey: "Software\Classes\.flac\OpenWithProgids"; ValueName: "MagicMusicWall.Audio"; ValueType: none; Flags: deletevalue; Tasks: not associatefiles
Root: HKCU; Subkey: "Software\Classes\.wav\OpenWithProgids"; ValueName: "MagicMusicWall.Audio"; ValueType: none; Flags: deletevalue; Tasks: not associatefiles
Root: HKCU; Subkey: "Software\Classes\.wave\OpenWithProgids"; ValueName: "MagicMusicWall.Audio"; ValueType: none; Flags: deletevalue; Tasks: not associatefiles
Root: HKCU; Subkey: "Software\Classes\.aac\OpenWithProgids"; ValueName: "MagicMusicWall.Audio"; ValueType: none; Flags: deletevalue; Tasks: not associatefiles
Root: HKCU; Subkey: "Software\Classes\.m4a\OpenWithProgids"; ValueName: "MagicMusicWall.Audio"; ValueType: none; Flags: deletevalue; Tasks: not associatefiles
Root: HKCU; Subkey: "Software\Classes\.ogg\OpenWithProgids"; ValueName: "MagicMusicWall.Audio"; ValueType: none; Flags: deletevalue; Tasks: not associatefiles
Root: HKCU; Subkey: "Software\Classes\.oga\OpenWithProgids"; ValueName: "MagicMusicWall.Audio"; ValueType: none; Flags: deletevalue; Tasks: not associatefiles
Root: HKCU; Subkey: "Software\Classes\.opus\OpenWithProgids"; ValueName: "MagicMusicWall.Audio"; ValueType: none; Flags: deletevalue; Tasks: not associatefiles
Root: HKCU; Subkey: "Software\Classes\.wma\OpenWithProgids"; ValueName: "MagicMusicWall.Audio"; ValueType: none; Flags: deletevalue; Tasks: not associatefiles
Root: HKCU; Subkey: "Software\Classes\.aiff\OpenWithProgids"; ValueName: "MagicMusicWall.Audio"; ValueType: none; Flags: deletevalue; Tasks: not associatefiles
Root: HKCU; Subkey: "Software\Classes\.aif\OpenWithProgids"; ValueName: "MagicMusicWall.Audio"; ValueType: none; Flags: deletevalue; Tasks: not associatefiles
Root: HKCU; Subkey: "Software\Classes\.alac\OpenWithProgids"; ValueName: "MagicMusicWall.Audio"; ValueType: none; Flags: deletevalue; Tasks: not associatefiles
Root: HKCU; Subkey: "Software\Classes\.ape\OpenWithProgids"; ValueName: "MagicMusicWall.Audio"; ValueType: none; Flags: deletevalue; Tasks: not associatefiles

[Icons]
Name: "{group}\Magic Music Wall"; Filename: "{app}\MagicMusicWall.exe"; WorkingDir: "{app}"; IconFilename: "{app}\Assets\DigitalRCS.ico"
Name: "{group}\Magic Music Wall documentation"; Filename: "{app}\OfflineDocs\index.html"; WorkingDir: "{app}\OfflineDocs"; Check: FileExists(ExpandConstant('{app}\OfflineDocs\index.html'))
Name: "{group}\DigitalRCS.com"; Filename: "https://digitalrcs.com"
Name: "{autodesktop}\Magic Music Wall"; Filename: "{app}\MagicMusicWall.exe"; WorkingDir: "{app}"; IconFilename: "{app}\Assets\DigitalRCS.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\MagicMusicWall.exe"; Description: "Launch Magic Music Wall"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

[Messages]
WelcomeLabel2=This installs [name/ver] for your Windows account.%n%nPlay your own music and videos, build playlists, and control playback from your home network.%n%nYour library, settings and media are kept when upgrading or uninstalling. Close Magic Music Wall before continuing.
