; ===================================================================
;  TSK TakeOff — instalador
;
;  Compilar com Inno Setup 6 (https://jrsoftware.org/isdl.php):
;      "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" TSKTakeOff.iss
;
;  Produz:  Output\TSKTakeOff-Setup-1.2.6.1.exe
;           (o ultimo numero e incrementado a cada compilacao)
;
;  O que faz, do ponto de vista de quem instala: carrega duas vezes em
;  "Seguinte" e o plugin aparece sozinho no AutoCAD. Sem NETLOAD, sem
;  copiar ficheiros à mão, sem .bat.
; ===================================================================

; O AppVersao e o AppVersaoNum sao escritos pelo build
; (Deploy\sincronizar-versao.ps1). Nao editar a mao: a fonte e o
; Version.props, e dois sitios a decidir e como isto comecou a divergir.
;
;   AppVersao     "1.2.6.1"   o que se mostra e nomeia o ficheiro
;   AppVersaoNum  "1.2.6.1"   so numeros, para o VersionInfoVersion
;
; O ultimo numero vem de Version.build e sobe a cada compilacao.
#define AppNome        "TSK TakeOff"
#define AppVersao      "1.2.6.90"
#define AppVersaoNum   "1.2.6.90"
#define AppEditor      "Casquilho"
#define AppURL         "https://tsktakeoff.pt"
#define AppSuporte     "suporte@tsktakeoff.pt"
#define BundleNome     "TSKTakeOff.bundle"

[Setup]
AppId={{8F3A6C21-4B7E-4D19-9C42-0AC5011AD003}
AppName={#AppNome}
AppVersion={#AppVersao}
AppVerName={#AppNome} {#AppVersao}
AppPublisher={#AppEditor}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}/suporte
AppUpdatesURL={#AppURL}/descargas
; Numerico e nao o AppVersao: esta directiva recusa sufixos como "-rc1".
VersionInfoVersion={#AppVersaoNum}
VersionInfoCompany={#AppEditor}
VersionInfoDescription=Plugin de medições para AutoCAD

; Instala por utilizador: não pede permissões de administrador, o que
; evita o bloqueio típico dos departamentos de informática dos gabinetes.
PrivilegesRequired=lowest
DefaultDirName={userappdata}\Autodesk\ApplicationPlugins\{#BundleNome}
DisableDirPage=yes
DisableProgramGroupPage=yes
UsePreviousAppDir=yes

OutputDir=Output
OutputBaseFilename=TSKTakeOff-Setup-{#AppVersao}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
SetupIconFile=tsk.ico
UninstallDisplayName={#AppNome}
UninstallDisplayIcon={app}\Contents\TSKTakeOff.dll

; Assinatura digital — ver assinar.ps1. Sem isto o Windows mostra
; "Editor desconhecido" e metade das pessoas cancela a instalação.
; SignTool=assinatura
; SignedUninstaller=yes

[Languages]
Name: "pt"; MessagesFile: "compiler:Languages\Portuguese.isl"

[Types]
Name: "completa";  Description: "Instalação completa"
Name: "minima";    Description: "Apenas o plugin"
Name: "custom";    Description: "Personalizada"; Flags: iscustom

[Components]
Name: "plugin";  Description: "Plugin TSK TakeOff";              Types: completa minima custom; Flags: fixed
Name: "config";  Description: "Ligação ao servidor de licenças"; Types: completa custom
Name: "modelo";  Description: "Modelo de folha de medições";     Types: completa custom

[Files]
; ---- o plugin propriamente dito ----
Source: "..\TSKTakeOff.bundle\PackageContents.xml"; DestDir: "{app}";           Components: plugin; Flags: ignoreversion
Source: "Payload\Contents\*";                       DestDir: "{app}\Contents"; Components: plugin; Flags: ignoreversion recursesubdirs createallsubdirs

; ---- configuração do servidor (url + chave publishable) ----
Source: "Payload\supabase.json"; DestDir: "{userappdata}\TSKTakeOff"; Components: config; Flags: onlyifdoesntexist

; ---- modelo de folha de medições ----
; onlyifdoesntexist: nunca por cima do modelo que o cliente já afinou.
Source: "Payload\modelo.xlsx"; DestDir: "{userappdata}\TSKTakeOff"; DestName: "modelo.xlsx"; Components: modelo; Flags: onlyifdoesntexist; Check: ModeloPadraoNecessario

; ---- documentação ----
Source: "Payload\MANUAL.pdf"; DestDir: "{app}"; Components: plugin; Flags: ignoreversion skipifsourcedoesntexist

[Icons]
Name: "{userprograms}\{#AppNome}\Manual do TSK TakeOff"; Filename: "{app}\MANUAL.pdf"; Flags: excludefromshowinnewinstall
Name: "{userprograms}\{#AppNome}\Página do produto";     Filename: "{#AppURL}"

[Run]
Filename: "{app}\MANUAL.pdf"; Description: "Abrir o manual"; \
    Flags: postinstall shellexec nowait skipifsilent skipifdoesntexist

[UninstallDelete]
; O bundle tem de sair inteiro, senão o AutoCAD queixa-se de um plugin partido.
Type: filesandordirs; Name: "{app}"

[Code]
{ ------------------------------------------------------------------
  Verificações antes de instalar:
   1. existe algum AutoCAD suportado?
   2. o AutoCAD está aberto? (não se substitui uma DLL carregada)
  ------------------------------------------------------------------ }

const
  SeriesSuportadas = 'R24.0 (2021), R24.1 (2022), R24.2 (2023), R24.3 (2024), R25.0 (2025), R25.1 (2026)';

function ModeloPadraoNecessario(): Boolean;
var
  Pasta, Registo, Alvo: String;
begin
  Pasta := ExpandConstant('{userappdata}\TSKTakeOff\');
  Result := True;

  { Só considerar modelo.txt se o caminho registado ainda existir. }
  Registo := Pasta + 'modelo.txt';
  if FileExists(Registo) then
  begin
    Alvo := '';
    if LoadStringFromFile(Registo, Alvo) and FileExists(Trim(Alvo)) then
    begin
      Result := False;
      Exit;
    end;
  end;

  if FileExists(Pasta + 'modelo.xlsx') or
     FileExists(Pasta + 'modelo.xlsm') or
     FileExists(Pasta + 'modelo.xls') or
     FileExists(Pasta + 'modelo.xltm') or
     FileExists(Pasta + 'modelo.xlt') then
    Result := False;
end;

function AutoCADInstalado(): Boolean;
var
  Chaves: TArrayOfString;
  I: Integer;
begin
  Result := False;
  if RegGetSubkeyNames(HKEY_CURRENT_USER, 'Software\Autodesk\AutoCAD', Chaves) then
  begin
    for I := 0 to GetArrayLength(Chaves) - 1 do
    begin
      { R24.x = 2021-2024, R25.x = 2025-2026 }
      if (Pos('R24.', Chaves[I]) = 1) or (Pos('R25.', Chaves[I]) = 1) then
      begin
        Result := True;
        Exit;
      end;
    end;
  end;
end;

function AutoCADAberto(): Boolean;
var
  Codigo: Integer;
begin
  { tasklist devolve 0 e imprime a linha se o processo existir }
  Result := False;
  if Exec('cmd.exe',
          '/C tasklist /FI "IMAGENAME eq acad.exe" | find /I "acad.exe"',
          '', SW_HIDE, ewWaitUntilTerminated, Codigo) then
    Result := (Codigo = 0);
end;

function InitializeSetup(): Boolean;
begin
  Result := True;

  if not AutoCADInstalado() then
  begin
    if MsgBox('Não encontrei nenhuma versão suportada do AutoCAD neste computador.' + #13#10 + #13#10 +
              'Versões suportadas: ' + SeriesSuportadas + '.' + #13#10 + #13#10 +
              'Quer instalar mesmo assim?',
              mbConfirmation, MB_YESNO) = IDNO then
    begin
      Result := False;
      Exit;
    end;
  end;

  if AutoCADAberto() then
  begin
    MsgBox('O AutoCAD está aberto.' + #13#10 + #13#10 +
           'Feche o AutoCAD antes de continuar — enquanto estiver aberto, ' +
           'os ficheiros do plugin não podem ser substituídos.',
           mbError, MB_OK);
    Result := False;
  end;
end;

function InitializeUninstall(): Boolean;
begin
  Result := True;
  if AutoCADAberto() then
  begin
    MsgBox('Feche o AutoCAD antes de desinstalar o TSK TakeOff.', mbError, MB_OK);
    Result := False;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Modelo: String;
begin
  if CurStep = ssPostInstall then
  begin
    { Tirar a marca de "vindo da internet" ao modelo: sem isto o Excel
      abre-o em Vista Protegida e o plugin não consegue lá escrever. }
    Modelo := ExpandConstant('{userappdata}\TSKTakeOff\modelo.xlsx');
    if FileExists(Modelo) then
      DeleteFile(Modelo + ':Zone.Identifier');
  end;
end;
