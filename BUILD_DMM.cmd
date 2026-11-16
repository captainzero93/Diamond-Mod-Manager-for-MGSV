@echo off
rem DMM checked build launcher
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Build-DMM.ps1"
set RC=%ERRORLEVEL%
pause
exit /b %RC%
