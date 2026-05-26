@echo off
setlocal
cd /d "%~dp0"
set "DOTNET=%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe"
if not exist "%DOTNET%" set "DOTNET=dotnet"
"%DOTNET%" run --project "%~dp0src\ImagePromptStudio.csproj"
if errorlevel 1 pause
