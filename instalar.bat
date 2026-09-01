@echo off
title Instalar servidor de Rust
setlocal
set "ROOT=%~dp0"

echo ==========================================
echo   Instalacion del servidor de pruebas
echo ==========================================
echo.

echo [1/3] SteamCMD...
if not exist "%ROOT%steamcmd\steamcmd.exe" (
  if not exist "%ROOT%steamcmd" mkdir "%ROOT%steamcmd"
  "%SystemRoot%\System32\curl.exe" -L -o "%ROOT%steamcmd\steamcmd.zip" https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip
  "%SystemRoot%\System32\tar.exe" -xf "%ROOT%steamcmd\steamcmd.zip" -C "%ROOT%steamcmd"
  del "%ROOT%steamcmd\steamcmd.zip"
) else (
  echo     ya estaba instalado.
)

echo.
echo [2/3] Servidor de Rust (~6 GB, esto tarda un buen rato)...
"%ROOT%steamcmd\steamcmd.exe" +force_install_dir "%ROOT%server" +login anonymous +app_update 258550 validate +quit

echo.
echo [3/3] Oxide...
"%SystemRoot%\System32\curl.exe" -L -o "%TEMP%\oxide_rust.zip" https://umod.org/games/rust/download
"%SystemRoot%\System32\tar.exe" -xf "%TEMP%\oxide_rust.zip" -C "%ROOT%server"
del "%TEMP%\oxide_rust.zip"

echo.
echo ==========================================
echo   Listo.
echo   1) Pon tu SteamID64 en:
echo      server\server\skintest\cfg\users.cfg
echo   2) Ejecuta start.bat
echo ==========================================
pause
