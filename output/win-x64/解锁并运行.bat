@echo off
chcp 65001 >nul 2>&1
title 解除安全锁定
echo.
echo  ====================================================
echo   网易云悬浮窗 - 解除 Windows 安全锁定
echo  ====================================================
echo.
echo  Windows 会对从网络下载的文件进行安全锁定，
echo  运行此脚本可解除锁定，之后正常双击 EXE 即可。
echo.
powershell -Command "Get-ChildItem -LiteralPath '%~dp0' -Filter '*.exe' -Recurse | Unblock-File; Get-ChildItem -LiteralPath '%~dp0' -Filter '*.dll' -Recurse | Unblock-File; Get-ChildItem -LiteralPath '%~dp0' -Filter '*.ps1' -Recurse | Unblock-File; Get-ChildItem -LiteralPath '%~dp0' -Filter '*.json' -Recurse | Unblock-File"
echo  [√] 已解除所有文件的锁定。
echo.
echo  现在可以双击 HorizonRadioOverlay.exe 运行了。
echo.
pause
