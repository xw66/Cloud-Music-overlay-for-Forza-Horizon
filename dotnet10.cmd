@echo off
setlocal EnableExtensions DisableDelayedExpansion
set "MSBuildSDKsPath="

if defined DOTNET_ROOT if exist "%DOTNET_ROOT%\dotnet.exe" (
    "%DOTNET_ROOT%\dotnet.exe" --list-sdks 2>nul | "%SystemRoot%\System32\findstr.exe" /b /c:"10." >nul
    if not errorlevel 1 (
        "%DOTNET_ROOT%\dotnet.exe" %*
        exit /b %errorlevel%
    )
)

for /f "delims=" %%D in ('where.exe dotnet.exe 2^>nul') do (
    "%%D" --list-sdks 2>nul | "%SystemRoot%\System32\findstr.exe" /b /c:"10." >nul
    if not errorlevel 1 (
        "%%D" %*
        exit /b %errorlevel%
    )
)

echo .NET 10 SDK host was not found. 1>&2
exit /b 1
