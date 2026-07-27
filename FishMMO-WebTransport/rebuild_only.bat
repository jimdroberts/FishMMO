@echo off
REM Incremental WebTransport rebuild — prefers Schannel, then existing CMake cache.
REM See rebuild_only.ps1 / build_windows.ps1 for details.
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0rebuild_only.ps1" %*
exit /b %ERRORLEVEL%
