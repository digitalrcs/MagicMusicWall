#ifndef AppVersion
  #define AppVersion "0.7.1"
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
ChangesAssociations=no
ChangesEnvironment=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
; User data is outside {app}. Never add an UninstallDelete rule for it.
; Portable launchers/markers are distributed in the portable ZIP only.
Source: "{#PayloadDirectory}\*"; DestDir: "{app}"; Excludes: "Start-MagicMusicWall.cmd,portable.flag,Data\*"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "assets\DigitalRCS.ico"; DestDir: "{app}\Assets"; Flags: ignoreversion

[Icons]
Name: "{group}\Magic Music Wall"; Filename: "{app}\MagicMusicWall.exe"; WorkingDir: "{app}"; IconFilename: "{app}\Assets\DigitalRCS.ico"
Name: "{group}\Magic Music Wall documentation"; Filename: "{app}\OfflineDocs\index.html"; WorkingDir: "{app}\OfflineDocs"; Check: FileExists(ExpandConstant('{app}\OfflineDocs\index.html'))
Name: "{group}\DigitalRCS.com"; Filename: "https://digitalrcs.com"
Name: "{autodesktop}\Magic Music Wall"; Filename: "{app}\MagicMusicWall.exe"; WorkingDir: "{app}"; IconFilename: "{app}\Assets\DigitalRCS.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\MagicMusicWall.exe"; Description: "Launch Magic Music Wall"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

[Messages]
WelcomeLabel2=This installs [name/ver] for your Windows account.%n%nPlay your own music and videos, build playlists, and control playback from your home network.%n%nYour library, settings and media are kept when upgrading or uninstalling. Close Magic Music Wall before continuing.
