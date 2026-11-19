@echo off
rem Builds PickAds.exe using the C# compiler that comes with Windows.
rem No downloads or installs needed. Output: dist\PickAds.exe
setlocal
cd /d "%~dp0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo Could not find the .NET Framework C# compiler.
  exit /b 1
)

if not exist dist mkdir dist
"%CSC%" /nologo /target:winexe /optimize+ /platform:anycpu ^
  /win32manifest:assets\PickAds.manifest /win32icon:assets\PickAds.ico ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Management.dll ^
  /out:dist\PickAds.exe src\Program.cs src\HostsSecurity.cs src\LookupsForm.cs
if errorlevel 1 (
  echo Build failed.
  exit /b 1
)
echo.
echo Built dist\PickAds.exe
