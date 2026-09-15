@echo off
rem Sets up Lane Node (OpenAI-compatible) on Windows:
rem   1. installs the .NET 10 SDK into %LOCALAPPDATA%\Microsoft\dotnet if it isn't already available
rem   2. downloads lane-bot and packs the Lane NuGet packages into .\lane-packages
rem   3. restores and builds this project
rem
rem Environment overrides:
rem   LANE_BOT_REPO  git URL of lane-bot   (default https://github.com/ImNotJahan/lane-bot)
rem   LANE_BOT_REF   branch or tag to use  (default main)
rem   LANE_BOT_DIR   use an existing lane-bot checkout instead of downloading one
setlocal EnableExtensions EnableDelayedExpansion

if not defined LANE_BOT_REPO set "LANE_BOT_REPO=https://github.com/ImNotJahan/lane-bot"
if not defined LANE_BOT_REF set "LANE_BOT_REF=main"
set "LANE_VERSION=0.1.0"
set "LANE_PROJECTS=Lane.Core Lane.Nodes.Protocol Lane.Node.Sdk Lane.Providers"

set "ROOT=%~dp0"
if "%ROOT:~-1%"=="\" set "ROOT=%ROOT:~0,-1%"
set "PACKAGES_DIR=%ROOT%\lane-packages"
set "LOCAL_DOTNET=%LOCALAPPDATA%\Microsoft\dotnet"
set "WORK_DIR="

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

rem --- lane-bot source ---------------------------------------------------------

rem The repository the packages should be compared against later, filled in below.
set "STAMP_REPO=%LANE_BOT_REPO%"
set "STAMP_REF=%LANE_BOT_REF%"
set "STAMP_COMMIT="

if defined LANE_BOT_DIR (
  if not exist "%LANE_BOT_DIR%\Lane.Core" (
    echo error: LANE_BOT_DIR ^(%LANE_BOT_DIR%^) doesn't look like a lane-bot checkout
    goto :fail
  )
  set "SRC=%LANE_BOT_DIR%"
  echo ==^> Using lane-bot from !SRC!
) else (
  set "WORK_DIR=%TEMP%\lane-install-%RANDOM%%RANDOM%"
  mkdir "!WORK_DIR!"
  set "SRC=!WORK_DIR!\lane-bot"
  where git >nul 2>&1
  if not errorlevel 1 (
    echo ==^> Cloning %LANE_BOT_REPO% ^(%LANE_BOT_REF%^)
    git clone --quiet --depth 1 --branch "%LANE_BOT_REF%" "%LANE_BOT_REPO%" "!SRC!"
    if errorlevel 1 goto :fail
  ) else (
    echo ==^> Downloading %LANE_BOT_REPO% ^(%LANE_BOT_REF%^)
    powershell -NoProfile -ExecutionPolicy Bypass -Command ^
      "$ErrorActionPreference='Stop'; [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12; $w='!WORK_DIR!'; $u=(('%LANE_BOT_REPO%'.TrimEnd('/')) -replace '\.git$','') + '/archive/%LANE_BOT_REF%.zip'; Invoke-WebRequest -UseBasicParsing $u -OutFile \"$w\lane-bot.zip\"; Expand-Archive -Path \"$w\lane-bot.zip\" -DestinationPath \"$w\extract\"; Move-Item -Path (Get-ChildItem \"$w\extract\")[0].FullName -Destination \"$w\lane-bot\""
    if errorlevel 1 goto :fail
  )
)

rem Remember where the packages came from, so the node can say when that source has moved on.
where git >nul 2>&1
if not errorlevel 1 (
  for /f "usebackq delims=" %%c in (`git -C "!SRC!" rev-parse HEAD 2^>nul`) do set "STAMP_COMMIT=%%c"
  if defined LANE_BOT_DIR (
    rem Follow the checkout's own branch and origin rather than the defaults.
    set "STAMP_REPO="
    set "STAMP_REF="
    for /f "usebackq delims=" %%u in (`git -C "!SRC!" remote get-url origin 2^>nul`) do set "STAMP_REPO=%%u"
    for /f "usebackq delims=" %%b in (`git -C "!SRC!" rev-parse --abbrev-ref HEAD 2^>nul`) do set "STAMP_REF=%%b"
    if "!STAMP_REF!"=="HEAD" set "STAMP_REF="
  ) else (
    rem Downloaded as a zip: ask the remote what the ref points at.
    if not defined STAMP_COMMIT (
      for /f "usebackq tokens=1" %%c in (`git ls-remote "%LANE_BOT_REPO%" "%LANE_BOT_REF%" 2^>nul`) do (
        if not defined STAMP_COMMIT set "STAMP_COMMIT=%%c"
      )
    )
  )
)

rem --- Lane packages -----------------------------------------------------------

echo ==^> Packing Lane packages into lane-packages\
if exist "%PACKAGES_DIR%" rmdir /s /q "%PACKAGES_DIR%"
mkdir "%PACKAGES_DIR%"
for %%p in (%LANE_PROJECTS%) do (
  echo     %%p
  "%DOTNET%" pack "!SRC!\%%p\%%p.csproj" -c Release -p:Version=%LANE_VERSION% -o "%PACKAGES_DIR%" --nologo -v quiet
  if errorlevel 1 goto :fail
)

rem Drop cached copies so restore picks up the freshly packed versions.
if defined NUGET_PACKAGES (set "NUGET_CACHE=%NUGET_PACKAGES%") else (set "NUGET_CACHE=%USERPROFILE%\.nuget\packages")
for %%p in (%LANE_PROJECTS%) do (
  if exist "%NUGET_CACHE%\%%p\%LANE_VERSION%" rmdir /s /q "%NUGET_CACHE%\%%p\%LANE_VERSION%"
)

if defined STAMP_REPO if defined STAMP_REF if defined STAMP_COMMIT (
  > "%PACKAGES_DIR%\source.txt" (
    echo repo=!STAMP_REPO!
    echo ref=!STAMP_REF!
    echo commit=!STAMP_COMMIT!
  )
)

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
call :cleanup
endlocal
exit /b 0

:fail
echo error: installation failed
call :cleanup
endlocal
exit /b 1

:cleanup
if defined WORK_DIR if exist "%WORK_DIR%" rmdir /s /q "%WORK_DIR%"
exit /b 0
