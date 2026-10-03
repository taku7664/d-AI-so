; DAIso 설치 프로그램 (Inno Setup 6)
;
; 만드는 방법은 tools\make-installer.ps1 을 쓴다. 판 번호와 원본 폴더를 그 스크립트가 넘긴다.
;   .\tools\make-installer.ps1
;
; 왜 Inno Setup 인가
;   MSIX 는 WinUI 의 정식 포장이지만 서명 인증서가 있어야 설치된다.
;   자체 서명으로 만들면 받는 사람이 인증서를 먼저 신뢰 저장소에 넣어야 해서 더 번거롭다.
;   Inno 는 서명이 없어도 설치되고(SmartScreen 경고만 뜬다) 제거 항목도 제대로 만든다.
;
; 왜 사용자 폴더에 깔리나
;   PrivilegesRequired=lowest. 이 앱은 %LOCALAPPDATA% 의 제 설정과 사용자의 CLI 파일만 읽으므로
;   관리자 권한이 필요 없다. UAC 창도 뜨지 않는다.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

#ifndef SourceDir
  #define SourceDir "..\..\src\Daiso.App\bin\Release\net8.0-windows10.0.19041.0\win-x64"
#endif

#ifndef RedistDir
  #define RedistDir "..\..\artifacts\redist"
#endif

#define AppName "DAIso"
#define AppExe "DAIso.exe"
#define AppPublisher "PPAK_JU"
#define AppUrl "https://github.com/taku7664/d-AI-so"

[Setup]
; 이 값은 제거·판올림을 같은 앱으로 묶는 열쇠다. 절대 바꾸지 않는다
AppId={{B7F3E2A1-4C5D-4E6F-9A8B-1D2C3E4F5A6B}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#AppVersion}

DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
AllowNoIcons=yes
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName} {#AppVersion}

PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763

OutputDir=..\..\artifacts\installer
OutputBaseFilename=DAIso-{#AppVersion}-setup
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes
WizardStyle=modern

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Tasks]
Name: "desktopicon"; Description: "바탕화면에 아이콘 만들기"; GroupDescription: "추가 작업:"; Flags: unchecked

[Files]
; 컴파일된 XAML(.xbf)과 DAIso.pri 까지 통째로 담는다. 하나라도 빠지면 시작하자마자 죽는다.
; .NET 런타임도 이 폴더 안에 있다(SelfContained=true). 받는 PC 에 .NET 이 없어도 뜬다 (docs/RELEASE.md)
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "arm64\*,*.pdb"

; WebView2 Evergreen 부트스트래퍼(약 2MB). 내장 터미널(xterm)이 WebView2 위에서 돈다.
; 없는 PC 에서만 돌린다. Microsoft 가 재배포용으로 배포하는 파일이고 tools\make-installer.ps1 이 공식 주소에서 받아 둔다
Source: "{#RedistDir}\MicrosoftEdgeWebview2Setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: not WebView2Found

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
; 부트스트래퍼는 인터넷에서 런타임을 받아 깐다. 스스로 권한을 올리고, 못 올리면 사용자 단위로 깐다.
; 실패해도 앱 설치는 계속한다 — 앱은 WebView2 가 없으면 내장 터미널 대신 새 창으로 CLI 를 띄운다
Filename: "{tmp}\MicrosoftEdgeWebview2Setup.exe"; Parameters: "/silent /install"; StatusMsg: "WebView2 런타임을 설치하는 중…"; Flags: waituntilterminated skipifdoesntexist; Check: not WebView2Found
Filename: "{app}\{#AppExe}"; Description: "{#AppName} 실행"; Flags: nowait postinstall skipifsilent

[Code]

{
  WebView2 런타임이 있는지 본다. Microsoft 문서가 정한 자리 — Edge 업데이트 클라이언트 키의 pv(버전) 값.
  기계 단위(64비트 OS 는 WOW6432Node) 또는 사용자 단위 어느 쪽이든 있으면 된다.
}
function WebView2Found: Boolean;
var
  Version: String;
begin
  Result := False;

  if RegQueryStringValue(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version)
     and (Version <> '') and (Version <> '0.0.0.0') then
    Result := True
  else if RegQueryStringValue(HKCU, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version)
     and (Version <> '') and (Version <> '0.0.0.0') then
    Result := True;
end;

{
  제거할 때 설정과 인덱스를 지울지 물어본다.

  <b>조용한 제거(/VERYSILENT, /SUPPRESSMSGBOXES)에서는 묻지도 지우지도 않는다.</b>
  전에는 물어보게만 해 두었는데, 메시지를 억누른 제거에서 Inno 가 기본 단추를 고른 것으로 처리해
  사람의 설정·인덱스·계정 보관함이 말없이 지워졌다 (2026-09-11, 내 실수로 실제로 날렸다).
  사람이 창을 보고 고를 때만 지운다.
}
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep <> usPostUninstall then
    Exit;

  if UninstallSilent then
    Exit;

  DataDir := ExpandConstant('{localappdata}\DAIso');

  if not DirExists(DataDir) then
    Exit;

  if MsgBox('설정과 세션 인덱스도 지우시겠습니까?' + #13#10#13#10 +
            DataDir + #13#10#13#10 +
            '보관해 둔 로그인 프로필도 함께 지워집니다.' + #13#10 +
            '아니요를 누르면 그대로 남겨 둡니다. 다시 설치하면 이어서 씁니다.',
            mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
    DelTree(DataDir, True, True, True);
end;
