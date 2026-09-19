@echo off
title Rust - Skin Test Server (local)

REM El directorio de trabajo debe ser la carpeta del servidor:
REM server.identity guarda el mundo en una ruta relativa a el.
cd /d "%~dp0server" || (echo No se encuentra la carpeta "server". & pause & exit /b 1)

if not exist "%~dp0server\RustDedicated.exe" (
  echo No se encuentra RustDedicated.exe. Ejecuta actualizar_servidor.bat primero.
  pause
  exit /b 1
)

REM ── Panel web (RGL Control): se abre junto con el servidor ──────────────
REM Si el propio panel ha lanzado este .bat (boton Encender) no se repite.
if defined RGL_FROM_PANEL goto start
del "%~dp0panel\stop.flag" 2>nul
where node >nul 2>nul || goto sin_node
netstat -ano | findstr /r /c:"127\.0\.0\.1:28080 .*LISTENING" >nul && goto panel_abierto
start "RGL Control" /min node "%~dp0panel\server.js"
goto start

:panel_abierto
start "" http://127.0.0.1:28080
goto start

:sin_node
echo [panel] No se encuentra Node.js: el panel web no se abre. Instalalo desde https://nodejs.org
goto start

:start
echo.
echo ==========================================
echo   Servidor de pruebas de skins
echo   En Rust pulsa F1 y escribe:
echo     client.connect localhost:28015
echo ==========================================
echo.

"%~dp0server\RustDedicated.exe" -batchmode -nographics -silent-crashes ^
  +server.identity "skintest" ^
  +server.port 28015 ^
  +server.level "Procedural Map" ^
  +server.seed 1219563660 ^
  +server.worldsize 4000 ^
  +server.hostname "Skin Test Local" ^
  +server.maxplayers 8 ^
  +rcon.port 28016 ^
  +rcon.password "skintest" ^
  +rcon.web 1

REM Apagado desde el panel: no se reinicia
if exist "%~dp0panel\stop.flag" (
  del "%~dp0panel\stop.flag"
  echo.
  echo Servidor apagado desde el panel.
  exit
)

echo.
echo El servidor se ha cerrado. Reiniciando en 10 segundos... (Ctrl+C para salir)
"%SystemRoot%\System32\timeout.exe" /t 10 /nobreak
goto start
