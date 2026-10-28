@echo off
setlocal
cd /d "%~dp0"
set "DOTNET_CLI_HOME=%~dp0.local\dotnet"
if exist "%~dp0.tools\dotnet\dotnet.exe" (
  "%~dp0.tools\dotnet\dotnet.exe" run --project src\Oeps.CommandToAction.App -- %*
) else if exist "%~dp0..\raw-material-sticker\.tools\dotnet\dotnet.exe" (
  "%~dp0..\raw-material-sticker\.tools\dotnet\dotnet.exe" run --project src\Oeps.CommandToAction.App -- %*
) else (
  dotnet run --project src\Oeps.CommandToAction.App -- %*
)
if errorlevel 1 pause
