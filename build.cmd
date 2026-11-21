@echo off
rem Builds Poke Sprite Creator on Windows with the C# compiler that ships with .NET Framework 4.x.
rem No Visual Studio needed. Output goes to bin\  (or open PokeSpriteCreator.sln in Visual Studio).
setlocal
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (echo Could not find csc.exe from .NET Framework 4. & goto :fail)
if not exist bin mkdir bin
set OPTS=/nologo /optimize+ /nowarn:1591,0168,0219,0649
echo == Poke Sprite Creator
"%CSC%" %OPTS% /target:winexe /out:bin\PokeSpriteCreator.exe /recurse:src\PokeSpriteCreator\*.cs /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll || goto :fail
echo.
echo Build OK. Run bin\PokeSpriteCreator.exe
exit /b 0
:fail
echo.
echo BUILD FAILED
exit /b 1
