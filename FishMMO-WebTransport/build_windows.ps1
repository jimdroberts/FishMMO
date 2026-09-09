# Build fishmmo_webtransport.dll for Windows x86_64.
#
# Default (fast): prebuilt msquic NuGet, OpenSSL flavour - compiles only the
# wrapper sources (seconds after the first NuGet download). msquic.dll ships
# with quictls built in, so it loads the PEM certificate/key files the server
# configs point at. Output: fishmmo_webtransport.dll + msquic.dll.
#
# Optional (slow): -Static fetches and builds msquic + quictls via CMake and
# links them into a single self-contained fishmmo_webtransport.dll.
# Prefer Ninja (or VS multi-config) for parallel jobs; never NMake.
#
# -Tls Schannel selects the Schannel NuGet flavour instead. CLIENT-ONLY:
# Schannel reads certificates from the Windows certificate store, cannot load
# PEM files, and a server built this way refuses to start (WT_ERR_TLS_BACKEND).
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File build_windows.ps1
#   powershell -ExecutionPolicy Bypass -File build_windows.ps1 -Static
#   powershell -ExecutionPolicy Bypass -File build_windows.ps1 -Static -Clean
#   powershell -ExecutionPolicy Bypass -File build_windows.ps1 -Tls Schannel   (client-only)
#
# Prerequisites (default / NuGet):
#   Visual Studio 2022+ with C++ desktop workload
#
# Prerequisites (-Static):
#   CMake 3.20+, OpenSSL (see openssl_cache.cmake or vcpkg), Ninja recommended:
#     winget install Kitware.CMake
#     winget install Ninja-build.Ninja

param(
    [switch]$Static,
    [switch]$Clean,
    [int]$Jobs = 0,
    [ValidateSet("OpenSSL", "Schannel")]
    [string]$Tls = "OpenSSL"
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

function Get-CpuJobCount {
    if ($Jobs -gt 0) { return $Jobs }
    $n = [Environment]::ProcessorCount
    if ($n -lt 1) { return 4 }
    return $n
}

if (-not $Static) {
    Write-Host "=== FishMMO WebTransport - Windows (default: NuGet msquic, TLS: $Tls) ==="
    Write-Host "Tip: use -Static for full CMake + static msquic (slow first build)."
    Write-Host ""
    & "$PSScriptRoot\build_windows_nuget.ps1" -Tls $Tls
    exit $LASTEXITCODE
}
if ($Tls -ne "OpenSSL") {
    Write-Host "NOTE: -Tls applies to the NuGet build only; -Static always builds quictls (OpenSSL)."
}

# --- Static msquic path (CMake) --------------------------------
$BuildDir = Join-Path $PSScriptRoot "build"
$UnityDir = Join-Path $PSScriptRoot "..\FishMMO-Unity\Assets\Plugins\FishNet\Plugins\WebTransport\Plugins\windows_x86_64"
$OpenSslCache = Join-Path $PSScriptRoot "openssl_cache.cmake"
$JobCount = Get-CpuJobCount

Write-Host "=== FishMMO WebTransport - Windows x86_64 (STATIC msquic / CMake) ==="
Write-Host "Parallel jobs: $JobCount"

if ($Clean -and (Test-Path $BuildDir)) {
    Write-Host "Cleaning $BuildDir ..."
    Remove-Item -Recurse -Force $BuildDir
}

$cmakeExe = $null
$cmakeCmd = Get-Command cmake -ErrorAction SilentlyContinue
if ($cmakeCmd) {
    $cmakeExe = $cmakeCmd.Source
}
if (-not $cmakeExe) {
    $vsCMakeCandidates = @(
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Professional\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\18\Community\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe"
    )
    foreach ($c in $vsCMakeCandidates) {
        if (Test-Path $c) {
            $cmakeExe = $c
            break
        }
    }
}
if (-not $cmakeExe) {
    throw "cmake not found on PATH. Install with: winget install Kitware.CMake"
}

$hasCache = Test-Path (Join-Path $BuildDir "CMakeCache.txt")
$needConfigure = -not $hasCache

# Prefer Ninja for parallel single-config builds; fall back to VS generator.
$generator = $null
$generatorArgs = @()
$ninja = Get-Command ninja -ErrorAction SilentlyContinue
if ($ninja) {
    $generator = "Ninja"
} else {
    $generator = "Visual Studio 17 2022"
    $generatorArgs = @("-A", "x64")
    Write-Host "Ninja not found - using '$generator'. Install for faster builds: winget install Ninja-build.Ninja"
}

$cachedGen = $null
if ($hasCache) {
    $cachedGen = Select-String -Path (Join-Path $BuildDir "CMakeCache.txt") -Pattern "^CMAKE_GENERATOR:INTERNAL=(.+)$" |
        ForEach-Object { $_.Matches.Groups[1].Value } |
        Select-Object -First 1
    if ($cachedGen -eq "NMake Makefiles") {
        Write-Host ""
        Write-Host "WARNING: Existing build/ uses NMake (single-threaded). Reconfigure with Ninja/VS for speed."
        Write-Host "  Re-run with:  .\build_windows.ps1 -Static -Clean"
        Write-Host "  Continuing incremental NMake build for now..."
        Write-Host ""
        $needConfigure = $false
    } else {
        $needConfigure = $false
        if ($cachedGen) {
            Write-Host "Using existing CMake cache (generator: $cachedGen)"
        }
    }
}

if ($needConfigure) {
    Write-Host "Configuring with generator: $generator"
    $configArgs = @(
        "-S", ".",
        "-B", $BuildDir,
        "-G", $generator
    ) + $generatorArgs + @(
        "-DCMAKE_BUILD_TYPE=Release",
        "-DWT_BUILD_TESTS=OFF",
        "-DBUILD_SHARED_LIBS=ON",
        "-DWT_STATIC_MSQUIC=ON"
    )
    if (Test-Path $OpenSslCache) {
        $configArgs = @("-C", $OpenSslCache) + $configArgs
        Write-Host "Using OpenSSL cache: $OpenSslCache"
    }
    & $cmakeExe @configArgs
    if ($LASTEXITCODE -ne 0) { throw "cmake configure failed ($LASTEXITCODE)" }
    $cachedGen = $generator
}

Write-Host "Building..."
$buildArgs = @("--build", $BuildDir, "--config", "Release")
$genForJobs = $cachedGen
if (-not $genForJobs) { $genForJobs = $generator }

if ($genForJobs -eq "Ninja" -or (Test-Path (Join-Path $BuildDir "build.ninja"))) {
    $buildArgs += @("-j", "$JobCount")
} elseif ($genForJobs -like "Visual Studio*") {
    $buildArgs += @("--", "/m:$JobCount")
} else {
    $buildArgs += @("-j", "$JobCount")
}

& $cmakeExe @buildArgs
if ($LASTEXITCODE -ne 0) { throw "cmake build failed ($LASTEXITCODE)" }

Write-Host ""
Write-Host "=== Done (static) ==="
$dll = Join-Path $UnityDir "fishmmo_webtransport.dll"
if (Test-Path $dll) {
    Get-ChildItem $dll | ForEach-Object {
        Write-Host "$($_.Name)  $([math]::Round($_.Length/1KB,1)) KB"
    }
} else {
    Write-Host "WARNING: expected output missing: $dll"
}
