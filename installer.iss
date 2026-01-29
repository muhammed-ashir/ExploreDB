; DbExplore Installer Script for Inno Setup
; Download Inno Setup from: https://jrsoftware.org/isdl.php

[Setup]
AppName=DbExplore
AppVersion=1.0.0
AppPublisher=DbExplore
AppPublisherURL=https://github.com/YOUR_USERNAME/DbExplore
DefaultDirName={autopf}\DbExplore
DefaultGroupName=DbExplore
OutputDir=.\installer_output
OutputBaseFilename=DbExplore-Setup-v1.0.0
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=Platforms\Windows\Images\Square150x150Logo.png
UninstallDisplayIcon={app}\DbExplore.exe
WizardStyle=modern
PrivilegesRequired=lowest

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}";

[Files]
Source: "bin\Release\net8.0-windows10.0.19041.0\win10-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\DbExplore"; Filename: "{app}\DbExplore.exe"
Name: "{group}\Uninstall DbExplore"; Filename: "{uninstallexe}"
Name: "{autodesktop}\DbExplore"; Filename: "{app}\DbExplore.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\DbExplore.exe"; Description: "{cm:LaunchProgram,DbExplore}"; Flags: nowait postinstall skipifsilent
