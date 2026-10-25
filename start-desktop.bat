@echo off
cd /d "%~dp0"
if not exist node_modules (
  echo Installing dependencies...
  call npm install
)
call npm run build
if errorlevel 1 pause & exit /b 1
start "" /b npx electron .
