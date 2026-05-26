@echo off
setlocal
cd /d "%~dp0"

REM Pick a dotnet: prefer the user-local SDK that VS / dotnet-install drop into LOCALAPPDATA.
set "DOTNET=%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe"
if not exist "%DOTNET%" set "DOTNET=dotnet"

echo Cleaning published\ ...
if exist "%~dp0published" rmdir /S /Q "%~dp0published"

echo.
echo Publishing self-contained single-file exe (win-x64) ...
"%DOTNET%" publish "ImagePromptStudio.csproj" ^
    -c Release ^
    -r win-x64 ^
    --self-contained true ^
    -o "%~dp0published" ^
    --nologo -v:minimal
if errorlevel 1 (
    echo.
    echo Publish FAILED.
    pause
    exit /b 1
)

echo.
echo Done. Output:
echo   %~dp0published\ImagePromptStudio.exe
echo.
pause
