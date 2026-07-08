# ClipVault

ClipVault 是一款面向 Windows 的本地剪贴板历史管理工具。它常驻系统托盘，自动记录复制过的文本、图片和文件，并通过全局快捷键快速检索和再次粘贴。

![ClipVault 界面预览](m9t3i5gg.png)

## 功能

- 自动记录文本、RTF、HTML、图片和文件列表
- 默认使用 `Ctrl + Shift + V` 唤出历史面板
- 支持关键词搜索和分组筛选
- 支持记录置顶、编辑、删除和回收站
- 支持创建、删除及拖拽排序分组标签
- 双击历史记录可直接粘贴到原应用
- 支持多显示器，并优先在鼠标所在屏幕显示
- 支持自定义全局快捷键和开机自启动
- 支持 3 天、7 天、30 天、90 天或永久保留历史
- 历史数据仅保存在本机

## 系统要求

- Windows 10/11 x64
- 从源码运行需要 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- 生成安装包需要 [Inno Setup 6](https://jrsoftware.org/isinfo.php)

## 从源码运行

```powershell
git clone https://github.com/zhangty-admin/cliovault.git
cd cliovault
dotnet restore ClipVault.sln
dotnet run --project src/ClipVault/ClipVault.csproj
```

程序启动后常驻系统托盘，按 `Ctrl + Shift + V` 打开剪贴板历史面板。

## 构建

构建 Release 版本：

```powershell
dotnet build ClipVault.sln -c Release
```

发布 Windows x64 单文件版本：

```powershell
dotnet publish src/ClipVault/ClipVault.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -o publish
```

## 生成安装包

安装 Inno Setup 6 后运行：

```powershell
.\build-installer.bat
```

安装包输出到 `installer_output/`。当前批处理中的 Inno Setup 路径为 `D:\soft\Inno Setup 6\ISCC.exe`，如果安装位置不同，需要修改 `build-installer.bat` 中的 `ISCC` 变量。

## 数据存储

用户数据保存在：

```text
%LocalAppData%\ClipVault\
|-- settings.json
|-- history.json
`-- images\
```

卸载程序会删除该目录中的历史记录、设置和图片。

## 技术栈

- .NET 8
- WPF
- CommunityToolkit.Mvvm
- H.NotifyIcon.Wpf
- Win32 Clipboard、Hotkey 和 Keyboard API

## 文档

详细的产品规则、数据流程和当前实现边界见 [产品逻辑说明](docs/PRODUCT_LOGIC.md)。
