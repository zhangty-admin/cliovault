@echo off
chcp 65001 >nul
echo ========================================
echo   ClipVault 安装包打包脚本
echo ========================================
echo.

REM 检查 Inno Setup 是否安装
set "ISCC=D:\soft\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" (
    echo [错误] 未找到 Inno Setup 6！
    echo.
    echo 请先下载安装 Inno Setup 6:
    echo   https://jrsoftware.org/download.php/is.exe
    echo.
    pause
    exit /b 1
)

echo [1/3] 编译 Release 版本...
cd /d "%~dp0"
dotnet publish src\ClipVault\ClipVault.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
if errorlevel 1 (
    echo [错误] 编译失败！
    pause
    exit /b 1
)

echo.
echo [2/3] 生成安装包...
"%ISCC%" ClipVaultSetup.iss
if errorlevel 1 (
    echo [错误] 安装包生成失败！
    pause
    exit /b 1
)

echo.
echo [3/3] 完成！
echo 安装包位于: installer_output\ 目录下
echo.
dir installer_output\*.exe 2>nul
echo.
pause
