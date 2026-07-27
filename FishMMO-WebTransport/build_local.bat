@echo off
REM Local convenience wrapper — same as build_windows.ps1 (default: fast Schannel).
REM   build_local.bat           → Schannel / NuGet msquic
REM   build_local.bat -Static   → full CMake + static msquic (Ninja preferred)
REM   rebuild_only.bat          → incremental only
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build_windows.ps1" %*
exit /b %ERRORLEVEL%
