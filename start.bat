@echo off
setlocal
cd /d "%~dp0"
title Win Solar v2 - Test
echo ==============================================
echo   Win Solar v2  -  Tarayici test surumu
echo ==============================================
where node >nul 2>nul
if errorlevel 1 (
  echo [HATA] Node.js bulunamadi. https://nodejs.org adresinden LTS surumunu kur.
  pause
  exit /b 1
)
if not exist node_modules (
  echo [1/2] Ilk kurulum: paketler yukleniyor ^(1-2 dk surebilir^)...
  call npm install
  if errorlevel 1 (
    echo [HATA] npm install basarisiz oldu.
    pause
    exit /b 1
  )
)
echo [2/2] Uygulama baslatiliyor: http://localhost:3000
echo Kapatmak icin bu pencerede CTRL+C bas veya pencereyi kapat.
echo.
call npm run dev
pause
