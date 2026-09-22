@echo off
rem Sets up Lane Node (OpenAI-compatible) on Windows:
rem   1. installs the .NET 10 SDK into %LOCALAPPDATA%\Microsoft\dotnet if it isn't already available
rem   2. restores (the Lane packages come from nuget.org) and builds this project
setlocal EnableExtensions EnableDelayedExpansion

set "ROOT=%~dp0"
if "%ROOT:~-1%"=="\" set "ROOT=%ROOT:~0,-1%"
set "LOCAL_DOTNET=%LOCALAPPDATA%\Microsoft\dotnet"

rem --- .NET 10 SDK -------------------------------------------------------------

set "DOTNET="
where dotnet >nul 2>&1 && (
  dotnet --list-sdks 2>nul | findstr /b "10." >nul && set "DOTNET=dotnet"
)
if not defined DOTNET if exist "%LOCAL_DOTNET%\dotnet.exe" (
  "%LOCAL_DOTNET%\dotnet.exe" --list-sdks 2>nul | findstr /b "10." >nul && set "DOTNET=%LOCAL_DOTNET%\dotnet.exe"
)
if not defined DOTNET (
  echo ==^> Installing the .NET 10 SDK into %LOCAL_DOTNET%
  powershell -NoProfile -ExecutionPolicy Bypass -Command ^
    "$ErrorActionPreference='Stop'; [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12; & ([scriptblock]::Create((Invoke-WebRequest -UseBasicParsing 'https://dot.net/v1/dotnet-install.ps1').Content)) -Channel 10.0 -InstallDir '%LOCAL_DOTNET%'"
  if errorlevel 1 goto :fail
  set "DOTNET=%LOCAL_DOTNET%\dotnet.exe"
)
if not "%DOTNET%"=="dotnet" (
  set "DOTNET_ROOT=%LOCAL_DOTNET%"
  set "PATH=%LOCAL_DOTNET%;%PATH%"
)
for /f "usebackq delims=" %%v in (`"%DOTNET%" --version`) do echo ==^> Using .NET SDK %%v

rem --- Build -------------------------------------------------------------------

echo ==^> Building Lane Node
"%DOTNET%" build "%ROOT%\Lane.Node.OpenAi.csproj" --nologo -v quiet
if errorlevel 1 goto :fail

echo ==^> Done. Start the node with:
if "%DOTNET%"=="dotnet" (
  echo     dotnet run
) else (
  echo     "%DOTNET%" run
)
endlocal
exit /b 0

:fail
echo error: installation failed
endlocal
exit /b 1
