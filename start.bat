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
  +server.seed 1337 ^
  +server.worldsize 1500 ^
  +server.hostname "Skin Test Local" ^
  +server.maxplayers 8 ^
  +rcon.port 28016 ^
  +rcon.password "skintest" ^
  +rcon.web 1

echo.
echo El servidor se ha cerrado. Reiniciando en 10 segundos... (Ctrl+C para salir)
"%SystemRoot%\System32\timeout.exe" /t 10 /nobreak
goto start
