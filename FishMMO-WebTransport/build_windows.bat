@echo off
setlocal enabledelayedexpansion

echo === FishMMO WebTransport - Windows Build ===

set BUILD_DIR=build
set OUT_DIR=unity\windows_x86_64

:: Create directories
if not exist "%BUILD_DIR%" mkdir "%BUILD_DIR%"
if not exist "%OUT_DIR%" mkdir "%OUT_DIR%"

:: Configure with CMake
cmake -S . -B "%BUILD_DIR%" ^
    -DCMAKE_BUILD_TYPE=Release ^
    -DWT_BUILD_TESTS=OFF ^
    -DBUILD_SHARED_LIBS=ON ^
    -DWT_STATIC_MSQUIC=ON ^
    -G "Visual Studio 17 2022" -A x64

:: Build
cmake --build "%BUILD_DIR%" --config Release -j

:: Copy output
copy "%BUILD_DIR%\Release\fishmmo_webtransport.dll" "%OUT_DIR%\"

echo === Build complete ===
echo Output: %OUT_DIR%\fishmmo_webtransport.dll
dir "%OUT_DIR%\fishmmo_webtransport.dll"
