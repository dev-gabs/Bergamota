; Instalador da Bergamota (Inno Setup 6)
; Gerar: powershell -File tools/build-installer.ps1   (publica o app e compila este script)
;
; Instala por usuário (sem pedir administrador) em %LocalAppData%\Programs\Bergamota.
; O app já leva o .NET dentro. Programas externos que ampliam as funções
; (FFmpeg, ImageMagick, 7-Zip, PowerToys, LocalSend) aparecem como opções só se faltarem,
; e são instalados pelo winget.

#define AppVersion "1.0.0"

[Setup]
AppId={{2250FDCF-C300-46B6-9F23-281378191C04}
AppName=Bergamota
AppVersion={#AppVersion}
AppVerName=Bergamota {#AppVersion}
AppPublisher=Bergamota
DefaultDirName={localappdata}\Programs\Bergamota
DisableProgramGroupPage=yes
DisableWelcomePage=no
DisableDirPage=auto
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir=..\dist
OutputBaseFilename=Bergamota-Setup-{#AppVersion}
SetupIconFile=..\src\Bergamota\Assets\bergamota.ico
UninstallDisplayIcon={app}\Bergamota.exe
UninstallDisplayName=Bergamota
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern dark
WizardImageFile=art\wizard-100.bmp,art\wizard-200.bmp
WizardSmallImageFile=art\small-100.bmp,art\small-200.bmp
CloseApplications=yes

[Languages]
Name: "pt"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Messages]
; "a Bergamota" (o padrão do Inno diz "do [name]")
WelcomeLabel1=Bem-vindo ao instalador da [name]
WelcomeLabel2=Isto vai instalar a [name/ver] no seu computador.%n%nEla fica na bandeja do Windows: comece a arrastar arquivos e segure CapsLock (ou leve até a borda da tela) para abrir a roda.
FinishedHeadingLabel=A Bergamota está instalada!
FinishedLabel=A [name] foi instalada. Ela fica na bandeja, perto do relógio.
ReadyLabel1=Tudo pronto para instalar a [name].

[CustomMessages]
pt.Deps=Programas que ampliam a Bergamota (instalados pelo winget, precisa de internet):
pt.DepFFmpeg=FFmpeg — converter vídeo e áudio
pt.DepMagick=ImageMagick — converter imagens (WEBP, AVIF, HEIC, ICO, PDF)
pt.Dep7zip=7-Zip — extrair .rar, .7z e outros (o WinRAR também serve)
pt.DepPowerToys=PowerToys — o PowerRename, para renomear em lote (download grande)
pt.DepLocalSend=LocalSend — receber no PC arquivos enviados do celular
pt.Installing=Instalando %1…

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "dep_ffmpeg"; Description: "{cm:DepFFmpeg}"; GroupDescription: "{cm:Deps}"; Check: NeedsFFmpeg
Name: "dep_magick"; Description: "{cm:DepMagick}"; GroupDescription: "{cm:Deps}"; Check: NeedsMagick
Name: "dep_7zip"; Description: "{cm:Dep7zip}"; GroupDescription: "{cm:Deps}"; Check: NeedsArchiver
Name: "dep_localsend"; Description: "{cm:DepLocalSend}"; GroupDescription: "{cm:Deps}"; Check: NeedsLocalSend
Name: "dep_powertoys"; Description: "{cm:DepPowerToys}"; GroupDescription: "{cm:Deps}"; Check: NeedsPowerToys; Flags: unchecked

[Files]
Source: "..\dist\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
; limpa arquivos de uma versão anterior antes de copiar a nova
Type: filesandordirs; Name: "{app}\*"

[UninstallDelete]
Type: dirifempty; Name: "{app}"

[Icons]
Name: "{userprograms}\Bergamota"; Filename: "{app}\Bergamota.exe"
Name: "{userdesktop}\Bergamota"; Filename: "{app}\Bergamota.exe"; Tasks: desktopicon

[Registry]
; iniciar com o Windows (dá para desligar em Configurações dentro do app); some ao desinstalar
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Bergamota"; ValueData: """{app}\Bergamota.exe"" --tray"; Flags: uninsdeletevalue

[Run]
Filename: "{code:Winget}"; Parameters: "install --id Gyan.FFmpeg -e --silent --accept-package-agreements --accept-source-agreements"; StatusMsg: "{cm:Installing,FFmpeg}"; Flags: runhidden waituntilterminated; Tasks: dep_ffmpeg
Filename: "{code:Winget}"; Parameters: "install --id ImageMagick.ImageMagick -e --silent --accept-package-agreements --accept-source-agreements"; StatusMsg: "{cm:Installing,ImageMagick}"; Flags: runhidden waituntilterminated; Tasks: dep_magick
Filename: "{code:Winget}"; Parameters: "install --id 7zip.7zip -e --silent --accept-package-agreements --accept-source-agreements"; StatusMsg: "{cm:Installing,7-Zip}"; Flags: runhidden waituntilterminated; Tasks: dep_7zip
Filename: "{code:Winget}"; Parameters: "install --id LocalSend.LocalSend -e --silent --accept-package-agreements --accept-source-agreements"; StatusMsg: "{cm:Installing,LocalSend}"; Flags: runhidden waituntilterminated; Tasks: dep_localsend
Filename: "{code:Winget}"; Parameters: "install --id Microsoft.PowerToys -e --silent --accept-package-agreements --accept-source-agreements"; StatusMsg: "{cm:Installing,PowerToys}"; Flags: runhidden waituntilterminated; Tasks: dep_powertoys
Filename: "{app}\Bergamota.exe"; Description: "{cm:LaunchProgram,Bergamota}"; Flags: nowait postinstall skipifsilent

[Code]
{ fecha a Bergamota (fica na bandeja) antes de instalar por cima ou desinstalar }
procedure CloseBergamota;
var
  Code: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM Bergamota.exe /F', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Sleep(500);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  CloseBergamota;
  Result := '';
end;

function InitializeUninstall: Boolean;
begin
  CloseBergamota;
  Result := True;
end;

function WingetPath: String;
begin
  Result := ExpandConstant('{localappdata}\Microsoft\WindowsApps\winget.exe');
end;

function Winget(Param: String): String;
begin
  Result := WingetPath;
end;

function HasWinget: Boolean;
begin
  Result := FileExists(WingetPath);
end;

{ procura um executável nas pastas do PATH e nos atalhos do winget }
function InPath(Exe: String): Boolean;
var
  Paths, Dir: String;
  P: Integer;
begin
  Result := FileExists(ExpandConstant('{localappdata}\Microsoft\WinGet\Links\') + Exe);
  Paths := GetEnv('PATH') + ';';
  while (not Result) and (Length(Paths) > 0) do
  begin
    P := Pos(';', Paths);
    Dir := Trim(Copy(Paths, 1, P - 1));
    Delete(Paths, 1, P);
    if (Dir <> '') and FileExists(AddBackslash(Dir) + Exe) then
      Result := True;
  end;
end;

function DirMatches(Pattern: String): Boolean;
var
  Rec: TFindRec;
begin
  Result := FindFirst(Pattern, Rec);
  if Result then FindClose(Rec);
end;

function NeedsFFmpeg: Boolean;
begin
  Result := HasWinget and not InPath('ffmpeg.exe');
end;

function NeedsMagick: Boolean;
begin
  Result := HasWinget and not InPath('magick.exe') and not DirMatches(ExpandConstant('{commonpf64}\ImageMagick*'));
end;

function NeedsArchiver: Boolean;
begin
  Result := HasWinget and not FileExists(ExpandConstant('{commonpf64}\WinRAR\WinRAR.exe'))
    and not FileExists(ExpandConstant('{commonpf64}\7-Zip\7z.exe'));
end;

function NeedsPowerToys: Boolean;
begin
  Result := HasWinget and not FileExists(ExpandConstant('{localappdata}\PowerToys\WinUI3Apps\PowerToys.PowerRename.exe'))
    and not FileExists(ExpandConstant('{commonpf64}\PowerToys\WinUI3Apps\PowerToys.PowerRename.exe'));
end;

function NeedsLocalSend: Boolean;
begin
  Result := HasWinget and not FileExists(ExpandConstant('{commonpf64}\LocalSend\localsend_app.exe'))
    and not FileExists(ExpandConstant('{localappdata}\Programs\LocalSend\localsend_app.exe'));
end;
