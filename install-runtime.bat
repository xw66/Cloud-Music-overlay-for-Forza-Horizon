@echo off
chcp 65001 >nul
title 安装运行库
echo.
echo  ====================================================
echo   网易云悬浮窗 - 运行库安装
echo  ====================================================
echo.
echo  正在检查 .NET 8 运行库...
echo.

dotnet --list-runtimes 2>nul | findstr /C:"Microsoft.NETCore.App 8" >nul
if %errorlevel%==0 (
    echo  [√] .NET 8 运行库已安装，无需操作。
    goto :done
)

echo  [!] 未检测到 .NET 8 运行库，正在安装...
echo.

winget install Microsoft.DotNet.Runtime.8 --accept-package-agreements --accept-source-agreements 2>nul
if %errorlevel%==0 (
    echo  [√] .NET 8 运行库安装完成。
    goto :done
)

echo  [!] winget 安装失败，正在下载离线安装包...
echo.

set "URL=https://download.visualstudio.microsoft.com/download/pr/89f9072c-1c4f-4a2e-8a5c-2c7f3b1a8e9d/2c9ebf3c3f7e8b2e8b2e8b2e8b2e8b2e/dotnet-runtime-8.0.11-win-x64.exe"
set "FILE=%TEMP%\dotnet-runtime-8.0-win-x64.exe"

powershell -Command "Invoke-WebRequest -Uri '%URL%' -OutFile '%FILE%' -UseBasicParsing" 2>nul
if not exist "%FILE%" (
    echo  [!] 下载失败，请手动安装：
    echo      https://dotnet.microsoft.com/download/dotnet/8.0
    pause
    exit /b 1
)

echo  [√] 下载完成，正在安装...
"%FILE%" /quiet /norestart
del /f /q "%FILE%" 2>nul

dotnet --list-runtimes 2>nul | findstr /C:"Microsoft.NETCore.App 8" >nul
if %errorlevel%==0 (
    echo  [√] .NET 8 运行库安装完成。
) else (
    echo  [!] 安装可能未成功，请重启后重试或手动安装。
    echo      https://dotnet.microsoft.com/download/dotnet/8.0
    pause
    exit /b 1
)

:done
echo.
echo  ====================================================
echo   启动程序: HorizonRadioOverlay.exe
echo  ====================================================
echo.
timeout /t 3 >nul
