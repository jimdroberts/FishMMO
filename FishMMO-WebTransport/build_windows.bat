@echo off
cd /d "%~dp0"
echo === FishMMO WebTransport - Windows Build ===

set BUILD_DIR=build
set OUT_DIR=unity\windows_x86_64

if not exist "%OUT_DIR%" mkdir "%OUT_DIR%"

cmake -S . -B "%BUILD_DIR%" ^
    -DCMAKE_BUILD_TYPE=Release ^
    -DWT_BUILD_TESTS=OFF ^
    -DBUILD_SHARED_LIBS=ON ^
    -DWT_STATIC_MSQUIC=ON ^
    -G "Visual Studio 17 2022" -A x64
if errorlevel 1 exit /b 1

cmake --build "%BUILD_DIR%" --config Release -j
if errorlevel 1 exit /b 1

echo === Build complete ===
echo Output: %OUT_DIR%\fishmmo_webtransport.dll
dir "%OUT_DIR%\fishmmo_webtransport.dll"

REM Copy to Unity project plugin directory
set UNITY_PLUGIN_DIR=..\FishMMO-Unity\Assets\Plugins\FishNet\Plugins\WebTransport\Plugins\windows_x86_64
if exist "%UNITY_PLUGIN_DIR%" (
    copy /Y "%OUT_DIR%\fishmmo_webtransport.dll" "%UNITY_PLUGIN_DIR%\"
    echo Copied to Unity project: %UNITY_PLUGIN_DIR%\fishmmo_webtransport.dll
) else (
    echo Warning: Unity plugin directory not found at %UNITY_PLUGIN_DIR%
)