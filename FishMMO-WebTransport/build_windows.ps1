# Build fishmmo_webtransport.dll for Windows x86_64 (runs on Windows).
# Prerequisites: Visual Studio 2022 with C++ workload, CMake 3.20+.
#
#   winget install Kitware.CMake
#   vcpkg install openssl:x64-windows
param()

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$BuildDir = "build"
$UnityDir = "..\FishMMO-Unity\Assets\Plugins\FishNet\Plugins\WebTransport\Plugins\windows_x86_64"

Write-Host "=== FishMMO WebTransport — Windows x86_64 ==="

cmake -S . -B $BuildDir `
    -DCMAKE_BUILD_TYPE=Release `
    -DWT_BUILD_TESTS=OFF `
    -DBUILD_SHARED_LIBS=ON `
    -DWT_STATIC_MSQUIC=ON

cmake --build $BuildDir --config Release

# CMake outputs directly to the Unity plugins directory (see CMakeLists.txt).
Write-Host ""
Write-Host "=== Done ==="
Get-ChildItem "$UnityDir\fishmmo_webtransport.dll" | ForEach-Object {
    Write-Host "$($_.Name)  $([math]::Round($_.Length/1KB,1)) KB"
}