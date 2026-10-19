@echo off
rem Compile le plugin et l'installeur (necessite le SDK .NET et SimHub installe).
rem Usage : build.bat  [dossier SimHub]
setlocal
set "SIMHUB=%~1"
if "%SIMHUB%"=="" set "SIMHUB=C:\Program Files (x86)\SimHub"
cd /d "%~dp0"
dotnet build src\Plugin -c Release -p:SimHubDir="%SIMHUB%" || goto :err
dotnet build src\Installer -c Release || goto :err
copy /y build\AvixLmuDashSwitcher.dll Plugin\ >nul
copy /y build\AvixInstaller.exe . >nul
echo.
echo OK : Plugin\AvixLmuDashSwitcher.dll et AvixInstaller.exe mis a jour.
pause
exit /b 0
:err
echo ECHEC de la compilation.
pause
exit /b 1
