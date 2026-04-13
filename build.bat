@echo off
setlocal EnableDelayedExpansion

echo.
echo  ===================================================
echo   Terminon Build Script
echo  ===================================================
echo.

:: ── Locate dotnet ────────────────────────────────────────────────────────────
where dotnet >nul 2>&1
if errorlevel 1 (
    echo  [ERROR] dotnet CLI not found.
    echo.
    echo  Please install the .NET 8 SDK from:
    echo    https://dotnet.microsoft.com/download/dotnet/8.0
    echo.
    pause
    exit /b 1
)

:: Verify .NET 8 is available
dotnet --list-sdks 2>&1 | findstr /r "^8\." >nul
if errorlevel 1 (
    echo  [ERROR] .NET 8 SDK not found. Installed SDKs:
    dotnet --list-sdks
    echo.
    echo  Please install the .NET 8 SDK from:
    echo    https://dotnet.microsoft.com/download/dotnet/8.0
    echo.
    pause
    exit /b 1
)

echo  [OK] .NET SDK found:
dotnet --version
echo.

:: ── Determine output directory ────────────────────────────────────────────────
set PROJECT=src\Terminon\Terminon.csproj
set PUBLISH_DIR=%~dp0publish

:: ── Restore ───────────────────────────────────────────────────────────────────
echo  [1/3] Restoring NuGet packages...
dotnet restore "%PROJECT%" --verbosity quiet
if errorlevel 1 (
    echo  [ERROR] Restore failed.
    pause
    exit /b 1
)
echo  [OK] Packages restored.
echo.

:: ── Build (Release) ───────────────────────────────────────────────────────────
echo  [2/3] Building Release...
dotnet build "%PROJECT%" -c Release --no-restore --verbosity quiet
if errorlevel 1 (
    echo  [ERROR] Build failed.
    pause
    exit /b 1
)
echo  [OK] Build succeeded.
echo.

:: ── Publish — self-contained single EXE ──────────────────────────────────────
echo  [3/3] Publishing self-contained executable to:
echo         %PUBLISH_DIR%
echo.

dotnet publish "%PROJECT%" ^
    -c Release ^
    -r win-x64 ^
    --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:EnableCompressionInSingleFile=true ^
    -p:DebugType=none ^
    -p:DebugSymbols=false ^
    -o "%PUBLISH_DIR%" ^
    --verbosity quiet

if errorlevel 1 (
    echo  [ERROR] Publish failed.
    pause
    exit /b 1
)

echo.
echo  ===================================================
echo   Build complete!
echo  ===================================================
echo.
echo   Output : %PUBLISH_DIR%\Terminon.exe
echo   Size   :
for %%F in ("%PUBLISH_DIR%\Terminon.exe") do echo            %%~zF bytes
echo.
echo  Run the app now? [Y/N]
set /p RUN=
if /i "!RUN!"=="Y" (
    start "" "%PUBLISH_DIR%\Terminon.exe"
)

endlocal
