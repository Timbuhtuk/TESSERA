@echo off
if not exist "%~dp0artifacts\workspace-preview\Tessera.exe" (
    echo Build the prototype with Build-WorkspacePrototype.ps1 first.
    pause
    exit /b 1
)
start "" "%~dp0artifacts\workspace-preview\Tessera.exe"
