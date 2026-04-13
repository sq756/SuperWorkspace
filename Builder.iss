[Setup]
; 🌟 唯一标识符：确保以后更新版本时，能精准覆盖旧版，不会在控制面板出现两个软件！
AppId={{9A2B45A8-F7C1-4D99-A6E3-128CD5348F2A}
; 软件基本信息
AppName=Super Workspace 扩展坞
AppVersion=1.2.31
AppPublisher=Commander sq756 Studio
AppCopyright=Copyright (C) 2026 Commander Studio

; 🌟 强制 64 位模式：因为我们编译的是 win-x64，必须让它安装到原生的 Program Files！
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64

; 默认安装到 C:\Program Files\SuperWorkspace
DefaultDirName={autopf}\SuperWorkspace
DefaultGroupName=Super Workspace

; 生成的安装包存放位置和名字
OutputDir=.\SetupBuild\Output
OutputBaseFilename=SuperWorkspace_Installer_v1.2.31
SetupIconFile=.\SetupBuild\ico.ico

; 极客压缩算法，把体积压到极致
Compression=lzma2/ultra64
SolidCompression=yes

; 🌟 核心防御：安装时自动检测并强行关闭躲在托盘里的旧版软件！
CloseApplications=yes
RestartApplications=yes

; 必须索要管理员权限（因为我们的软件需要安装虚拟显卡、注册虚拟摄像头等）
PrivilegesRequired=admin
UninstallDisplayIcon={app}\SuperWorkspace.exe

[Tasks]
Name: "desktopicon"; Description: "在桌面上创建快捷方式"; GroupDescription: "附加任务:"; Flags: unchecked

[Files]
; 🌟 终极洁癖模式：只打包 publish 纯净发布文件夹里的所有内容！
Source: ".\bin\Release\net8.0-windows\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; 🌟 智能扫货雷达：自动把放在项目根目录下的驱动一并吸入安装包！（即使没有也不会报错）
Source: ".\UnityCaptureFilter*bit.dll"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: ".\VBCABLE_Driver_Pack45\*"; DestDir: "{app}\VBCABLE_Driver_Pack45"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
Source: ".\VirtualDisplayDriver\*"; DestDir: "{app}\VirtualDisplayDriver"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
Source: ".\scrcpy-win64-v2.4\*"; DestDir: "{app}\scrcpy-win64-v2.4"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
Source: ".\SetupBuild\ico.ico"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
[Icons]
; 生成开始菜单快捷方式
Name: "{group}\Super Workspace"; Filename: "{app}\SuperWorkspace.exe"; IconFilename: "{app}\ico.ico"
; 生成卸载快捷方式
Name: "{group}\卸载 Super Workspace"; Filename: "{uninstallexe}"
; 生成桌面快捷方式
Name: "{autodesktop}\Super Workspace"; Filename: "{app}\SuperWorkspace.exe"; Tasks: desktopicon; IconFilename: "{app}\ico.ico"

[Run]
; 安装完成后，提供“立即运行”的勾选框
Filename: "{app}\SuperWorkspace.exe"; Description: "立即运行 Super Workspace"; Flags: nowait postinstall skipifsilent

[Dirs]
; 🌟 终极生态权限释放：自动创建 Plugins 文件夹，并赋予所有普通用户写入权限！解决 C 盘拖拽 DLL 报权限不足的死穴！
Name: "{app}\Plugins"; Permissions: users-modify
