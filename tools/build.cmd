@echo off
rem ============================================================================
rem  Q Pet - build both exes and copy them next to this file.
rem  Requirements: .NET SDK 6.0+ ( https://dotnet.microsoft.com/download )
rem  Just double-click this file. Messages are English on purpose (cmd codepage).
rem  Layout: sources in ..\src  ->  outputs to ..\build  ->  exes copied to repo root.
rem
rem  Steps:  0) version from git tag   1) XAML check   2) unit tests
rem           3) Pet.exe   4) PetSettings.exe   5) copy to this folder
rem  Optional: python on PATH enables the XAML check (skipped when missing).
rem ============================================================================
setlocal
cd /d "%~dp0"
echo ============================================
echo  Q Pet - build (Release)
echo ============================================
where dotnet >nul 2>nul
if errorlevel 1 (
  echo [!] .NET SDK not found.
  echo     Install it first: https://dotnet.microsoft.com/download
  echo     Then reopen this window and run build.cmd again.
  echo.
  pause
  exit /b 1
)
for /f "delims=" %%v in ('dotnet --version') do echo  dotnet SDK %%v

rem ---- [0/5] 版本号：取最近的 git tag（形如 v1.2.3），没有就用 2.0.0 ----
rem      第 4 位是提交数，方便区分同一个 tag 之后的多次构建。
set QVER=2.0.0
set QREV=0
set TAG=
for /f "delims=" %%t in ('git describe --tags --abbrev^=0 2^>nul') do set "TAG=%%t"
if not "%TAG%"=="" set "TAG=%TAG:v=%"
if not "%TAG%"=="" (
  echo %TAG%| findstr /r "^[0-9][0-9]*\.[0-9][0-9]*\.[0-9][0-9]*$" >nul
  if not errorlevel 1 set "QVER=%TAG%"
)
for /f "delims=" %%c in ('git rev-list --count HEAD 2^>nul') do set "QREV=%%c"
set QFILE=%QVER%.%QREV%
echo  version    %QFILE%
echo.

rem ---- [1/5] XAML 校验：编译 0 错误不等于界面能起来，这段 XAML 是运行时才解析的 ----
where python >nul 2>nul
if errorlevel 1 (
  echo [1/5] XAML check  - skipped (python not found)
) else (
  echo [1/5] XAML check ...
  python "%~dp0xamlgen.py" check
  if errorlevel 1 goto fail
)
echo.

rem ---- [2/5] 纯函数单测 ----
echo [2/5] unit tests ...
dotnet build "..\src\Tests.csproj" -c Release -nologo -v q
if errorlevel 1 goto fail
"..\build\tests\Tests.exe"
if errorlevel 1 goto fail
echo.

echo [3/5] building Pet.exe ...
dotnet build "..\src\Pet.csproj" -c Release -nologo -v q -p:Version=%QVER% -p:FileVersion=%QFILE%
if errorlevel 1 goto fail
echo [4/5] building PetSettings.exe ...
dotnet build "..\src\PetSettings.csproj" -c Release -nologo -v q -p:Version=%QVER% -p:FileVersion=%QFILE%
if errorlevel 1 goto fail
echo.
echo [5/5] copying into the repo root ...
copy /y "..\build\pet\Pet.exe" "..\Pet.exe" >nul
if errorlevel 1 goto busy
copy /y "..\build\settings\PetSettings.exe" "..\PetSettings.exe" >nul
if errorlevel 1 goto busy
echo.
echo [OK] Done.  version %QFILE%
echo      now open the repo folder and double-click Pet.exe        -^> desktop pet
echo      double-click PetSettings.exe -^> settings
echo.
pause
exit /b 0

:busy
echo.
echo [X] Could not overwrite Pet.exe / PetSettings.exe.
echo     The pet is probably still running: right-click its tray icon and exit,
echo     or run: taskkill /im Pet.exe /f
echo.
pause
exit /b 1

:fail
echo.
echo [X] Build failed - see the messages above.
echo     No internet? NuGet restore needs a connection the first time.
echo.
pause
exit /b 1
