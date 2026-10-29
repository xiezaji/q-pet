@echo off
rem 打开 Q Pet 的设置（独立 exe）
setlocal
cd /d "%~dp0"
if not exist "%~dp0PetSettings.exe" (
  echo [错误] 找不到 PetSettings.exe
  echo 请确认下载/解压完整，PetSettings.exe 应与本文件在同一个文件夹里。
  pause
  exit /b 1
)
start "" "%~dp0PetSettings.exe"
