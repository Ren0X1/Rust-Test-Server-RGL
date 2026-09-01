@echo off
title Actualizar servidor de Rust
echo Actualizando el servidor de Rust (hazlo despues de cada wipe/parche)...
echo.
"%~dp0steamcmd\steamcmd.exe" +force_install_dir "%~dp0server" +login anonymous +app_update 258550 validate +quit
echo.
echo IMPORTANTE: la actualizacion sobrescribe Oxide. Ejecuta ahora actualizar_oxide.bat
pause
