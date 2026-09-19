@echo off
title RGL Control - panel del servidor
REM Abre solo el panel web (start.bat ya lo abre junto con el servidor).
where node >nul 2>nul || (
  echo No se encuentra Node.js. Instalalo desde https://nodejs.org y vuelve a probar.
  pause
  exit /b 1
)
netstat -ano | findstr /r /c:"127\.0\.0\.1:28080 .*LISTENING" >nul && (
  start "" http://127.0.0.1:28080
  exit /b 0
)
node "%~dp0panel\server.js"
pause
