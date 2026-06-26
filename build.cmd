@echo off
rem Build GP3TyreInjectorGui.exe from source. Needs .NET Framework 4 (csc.exe, on all modern Windows).
setlocal
cd /d "%~dp0source"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
"%CSC%" -nologo -optimize+ -codepage:65001 -target:winexe -win32icon:icon.ico -win32manifest:gp3inj.manifest -reference:System.Windows.Forms.dll -reference:System.Drawing.dll -out:"..\GP3TyreInjectorGui.exe" GP3TyreInjectorGui.cs
if errorlevel 1 ( echo BUILD FAILED & exit /b 1 )
echo Built GP3TyreInjectorGui.exe
