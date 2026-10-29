@echo off
cd /d "%~dp0"
where node >nul 2>nul
if errorlevel 1 (
  echo Instala Node.js 22 o superior para iniciar el codigo fuente de PulseCut.
  pause
  exit /b 1
)
if not exist node_modules (
  call npm.cmd install
  if errorlevel 1 (
    pause
    exit /b 1
  )
)
call npm.cmd start
pause
