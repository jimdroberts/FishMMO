@echo off
REM Local convenience wrapper — same as build_windows.ps1 (default: fast NuGet msquic, OpenSSL flavour).
REM   build_local.bat                → NuGet msquic, OpenSSL (loads PEM certs, hosts servers)
REM   build_local.bat -Static        → full CMake + static msquic (Ninja preferred)
REM   build_local.bat -Tls Schannel  → NuGet msquic, Schannel (client-only)
REM   rebuild_only.bat               → incremental only
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build_windows.ps1" %*
exit /b %ERRORLEVEL%
