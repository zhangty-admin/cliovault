; ClipVault 安装包脚本 — Inno Setup 6
; 使用方法：安装 Inno Setup 6 后，双击此文件或在命令行执行 ISCC.exe ClipVaultSetup.iss

#define MyAppName "ClipVault"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "QuestAI"
#define MyAppExeName "ClipVault.exe"
#define MyAppDescription "Windows 剪贴板管理工具"

[Setup]
AppId={{B7F3E2A1-4D5C-6E8F-9A0B-1C2D3E4F5A6B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL=https://github.com/questai
AppSupportURL=https://github.com/questai
AppUpdatesURL=https://github.com/questai
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
DisableDirPage=no
OutputDir=installer_output
OutputBaseFilename=ClipVaultSetup_v{#MyAppVersion}
; SetupIconFile=src\ClipVault\Assets\app.ico   ; 如有 .ico 文件可取消注释
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequiredOverridesAllowed=dialog
PrivilegesRequired=lowest
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}

; 如果没有图标文件则移除上面这行，用下面的默认设置
; SetupIconFile 不存在时会报错，先注释掉

[Languages]
Name: "chinesesimp"; MessagesFile: "ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加图标:"; Flags: checkedonce
Name: "startup"; Description: "开机自动启动"; GroupDescription: "附加图标:"; Flags: checkedonce

[Files]
; 主程序（单文件 exe，已内嵌 .NET 运行时）
Source: "publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
; 开始菜单
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Comment: "{#MyAppDescription}"
; 桌面快捷方式
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Comment: "{#MyAppDescription}"; Tasks: desktopicon
; 安装目录下的卸载快捷方式
Name: "{app}\卸载 ClipVault"; Filename: "{uninstallexe}"; Comment: "卸载 ClipVault"

[Registry]
; 开机自启动注册表项
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
; 安装完成后可选启动
Filename: "{app}\{#MyAppExeName}"; Description: "立即启动 {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; 卸载时彻底清除所有用户数据（不残留任何文件）
Type: filesandordirs; Name: "{localappdata}\ClipVault"
; 清理可能残留的调试日志
Type: files; Name: "{app}\debug.log"

[Code]
// 卸载时关闭正在运行的 ClipVault
function InitializeUninstall(): Boolean;
var
  ErrorCode: Integer;
begin
  ShellExec('open', 'taskkill.exe', '/F /IM ClipVault.exe', '', SW_HIDE, ewNoWait, ErrorCode);
  Sleep(1000);
  Result := True;
end;

// 安装前关闭正在运行的 ClipVault
function InitializeSetup(): Boolean;
var
  ErrorCode: Integer;
begin
  ShellExec('open', 'taskkill.exe', '/F /IM ClipVault.exe', '', SW_HIDE, ewNoWait, ErrorCode);
  Sleep(500);
  Result := True;
end;

// 卸载完成后删除安装目录（以防有额外文件残留）
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    // 删除整个安装目录（包括卸载快捷方式等残留）
    DelTree(ExpandConstant('{app}'), True, True, True);
  end;
end;
