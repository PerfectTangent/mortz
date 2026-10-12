@echo off
setlocal
set "SRC=%~dp0Krita"
set "PYKRITA=%APPDATA%\krita\pykrita"
set "TEMPLATES=%APPDATA%\krita\templates"
set "ACTIONS=%APPDATA%\krita\actions"
if not exist "%PYKRITA%" mkdir "%PYKRITA%"
if not exist "%TEMPLATES%" mkdir "%TEMPLATES%"
if not exist "%ACTIONS%" mkdir "%ACTIONS%"

if exist "%PYKRITA%\mortz" rmdir /S /Q "%PYKRITA%\mortz"
if exist "%TEMPLATES%\mortz" rmdir /S /Q "%TEMPLATES%\mortz"
xcopy /E /I /Q /Y /H "%SRC%\mortz" "%PYKRITA%\mortz" >nul || exit /b 1
copy /Y "%SRC%\mortz.desktop" "%PYKRITA%\mortz.desktop" >nul || exit /b 1
xcopy /E /I /Q /Y /H "%SRC%\templates\mortz" "%TEMPLATES%\mortz" >nul || exit /b 1
copy /Y "%SRC%\mortz.action" "%ACTIONS%\mortz.action" >nul || exit /b 1

echo installed the Mortz plug-in into %PYKRITA% and the map template into %TEMPLATES%
echo restart Krita, then enable "Mortz map tools" in Settings ^> Configure Krita ^> Python Plugin Manager
echo and show the docker with Settings ^> Dockers ^> Mortz map
