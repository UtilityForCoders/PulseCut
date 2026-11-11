[Setup]
AppId={{3CBFAF29-BAB8-472C-9E54-08378B8D1E36}
AppName=PulseCut
AppVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\PulseCut
DefaultGroupName=PulseCut
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputBaseFilename=PulseCut-{#AppVersion}-setup-win-x64
SetupIconFile=PulseCut\Assets\PulseCut.ico
UninstallDisplayIcon={app}\PulseCut.exe
Compression=lzma2/fast
SolidCompression=yes

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Files]
Source: "{#PayloadDir}\PulseCut.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\bin\ffmpeg.exe"; DestDir: "{app}\bin"; Flags: ignoreversion
Source: "{#PayloadDir}\bin\ffprobe.exe"; DestDir: "{app}\bin"; Flags: ignoreversion
Source: "{#PayloadDir}\licenses\*"; DestDir: "{app}\licenses"; Flags: ignoreversion

[Icons]
Name: "{group}\PulseCut"; Filename: "{app}\PulseCut.exe"; WorkingDir: "{app}"
