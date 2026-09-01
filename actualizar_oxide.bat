@echo off
title Actualizar Oxide
echo Descargando la ultima version de Oxide...
curl -L -o "%TEMP%\oxide_rust.zip" https://umod.org/games/rust/download
echo Extrayendo sobre el servidor...
powershell -NoProfile -Command "Expand-Archive -Path $env:TEMP\oxide_rust.zip -DestinationPath %~dp0server -Force"
del "%TEMP%\oxide_rust.zip"
echo.
echo Oxide actualizado.
pause
