@echo off
title RGL - Servidor de Rust
setlocal enabledelayedexpansion
set "ROOT=%~dp0"
cd /d "%ROOT%"

:menu
cls
echo ==========================================
echo    RGL  -  Servidor de Rust + panel web
echo ==========================================
echo.
if exist "%ROOT%server\RustDedicated.exe" (echo   Servidor:  instalado) else (echo   Servidor:  NO instalado  ^<-- empieza por la opcion 3)
if exist "%ROOT%server\RustDedicated_Data\Managed\Oxide.Rust.dll" (echo   Oxide:     instalado) else (echo   Oxide:     NO instalado)
where node >nul 2>nul && (echo   Node.js:   instalado ^(panel web disponible^)) || (echo   Node.js:   NO instalado ^<-- sin panel web)
echo.
echo   [1]  Arrancar servidor + panel web
echo   [2]  Actualizar servidor y Oxide, y arrancar
echo   [3]  Instalar o reparar todo  (SteamCMD + servidor + Oxide)
echo   [4]  Abrir solo el panel web
echo   [5]  Actualizar solo Oxide
echo   [0]  Salir
echo.
choice /c 123450 /n /t 10 /d 1 /m "Elige una opcion (1 en 10 s): "
set "OPCION=%errorlevel%"
echo.

if "%OPCION%"=="1" goto arrancar
if "%OPCION%"=="2" goto actualizar
if "%OPCION%"=="3" goto instalar
if "%OPCION%"=="4" goto solo_panel
if "%OPCION%"=="5" goto oxide
if "%OPCION%"=="6" exit /b 0
goto menu

REM ─────────────────────────────────────────────────────────────
:instalar
echo [1/3] SteamCMD...
if not exist "%ROOT%steamcmd\steamcmd.exe" (
  if not exist "%ROOT%steamcmd" mkdir "%ROOT%steamcmd"
  "%SystemRoot%\System32\curl.exe" -L -o "%ROOT%steamcmd\steamcmd.zip" https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip
  "%SystemRoot%\System32\tar.exe" -xf "%ROOT%steamcmd\steamcmd.zip" -C "%ROOT%steamcmd"
  del "%ROOT%steamcmd\steamcmd.zip"
) else (
  echo      ya estaba instalado.
)
echo.
echo [2/3] Servidor de Rust ^(~6 GB la primera vez^)...
call :servidor
echo.
echo [3/3] Oxide...
call :oxide_paso
echo.
echo Instalacion terminada.
echo Pon tu SteamID64 en server\server\skintest\cfg\users.cfg o hazlo desde el panel.
echo.
pause
goto menu

REM ─────────────────────────────────────────────────────────────
:actualizar
if not exist "%ROOT%steamcmd\steamcmd.exe" (
  echo No esta SteamCMD: usa primero la opcion 3.
  pause
  goto menu
)
echo Actualizando el servidor de Rust...
call :servidor
echo.
echo Actualizando Oxide ^(el paso anterior lo sobrescribe, por eso va despues^)...
call :oxide_paso
echo.
goto arrancar

REM ─────────────────────────────────────────────────────────────
:oxide
call :oxide_paso
echo.
pause
goto menu

REM ─────────────────────────────────────────────────────────────
:solo_panel
call "%ROOT%panel.bat"
goto menu

REM ─────────────────────────────────────────────────────────────
:arrancar
if not exist "%ROOT%server\RustDedicated.exe" (
  echo No se encuentra el servidor: usa primero la opcion 3 ^(instalar^).
  pause
  goto menu
)
REM start.bat abre el panel web y arranca el servidor
call "%ROOT%start.bat"
goto menu

REM ─────────────────────────────────────────────────────────────
:servidor
"%ROOT%steamcmd\steamcmd.exe" +force_install_dir "%ROOT%server" +login anonymous +app_update 258550 validate +quit
exit /b

:oxide_paso
"%SystemRoot%\System32\curl.exe" -L -o "%TEMP%\oxide_rust.zip" https://umod.org/games/rust/download
"%SystemRoot%\System32\tar.exe" -xf "%TEMP%\oxide_rust.zip" -C "%ROOT%server"
del "%TEMP%\oxide_rust.zip"
echo      Oxide actualizado.
exit /b
