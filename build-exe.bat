@echo off
cd /d "%~dp0"
set CSC_IDENTITY_AUTO_DISCOVERY=false
echo [1/2] Installing dependencies...
call npm install
if errorlevel 1 goto fail
echo [2/2] Building installer (this can take several minutes)...
call npm run dist
if errorlevel 1 goto fail
echo.
echo DONE. Installer is in the "release" folder.
start "" "%~dp0release"
pause
exit /b 0
:fail
echo.
echo BUILD FAILED. Scroll up to read the error and send it to Claude.
pause
exit /b 1
