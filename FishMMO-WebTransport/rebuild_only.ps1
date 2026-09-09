# Incremental rebuild of fishmmo_webtransport.dll - prefers the fast path.
#
# Order:
#   1. NuGet tree (build_win_nuget) if present, OR no static CMake cache
#      - re-uses the TLS flavour recorded there (OpenSSL unless you chose
#        -Tls Schannel, which is client-only)
#   2. Existing CMake build/ cache (static msquic) with parallel jobs
#   3. Fall through to default build_windows.ps1 (NuGet, OpenSSL)
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File rebuild_only.ps1
#   rebuild_only.bat

param(
    [int]$Jobs = 0
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

function Get-CpuJobCount {
    if ($Jobs -gt 0) { return $Jobs }
    $n = [Environment]::ProcessorCount
    if ($n -lt 1) { return 4 }
    return $n
}

function Find-CMake {
    $cmd = Get-Command cmake -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $candidates = @(
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Professional\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\18\Community\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe"
    )
    foreach ($c in $candidates) {
        if (Test-Path $c) { return $c }
    }
    return $null
}

$nugetDir = Join-Path $PSScriptRoot "build_win_nuget"
$nugetReady = (Test-Path (Join-Path $nugetDir "obj")) -or
    ((Get-ChildItem -Path $nugetDir -Directory -Filter "msquic-*" -ErrorAction SilentlyContinue | Measure-Object).Count -gt 0)
$cmakeCache = Join-Path $PSScriptRoot "build\CMakeCache.txt"
$hasStaticCache = Test-Path $cmakeCache

# Prefer the NuGet tree whenever it exists, or when there is no static cache yet.
if ($nugetReady -or (-not $hasStaticCache)) {
    $tls = "OpenSSL"
    $stamp = Join-Path $nugetDir "last_tls.txt"
    if (Test-Path $stamp) { $tls = (Get-Content $stamp -Raw).Trim() }
    Write-Host "=== rebuild_only: NuGet msquic (fast, TLS: $tls) ==="
    & "$PSScriptRoot\build_windows_nuget.ps1" -Tls $tls
    exit $LASTEXITCODE
}

Write-Host "=== rebuild_only: CMake static cache ==="
$cmakeExe = Find-CMake
if (-not $cmakeExe) {
    throw "cmake not found. Run build_windows.ps1 (default NuGet/OpenSSL) or install CMake."
}

$JobCount = Get-CpuJobCount
$cachedGen = Select-String -Path $cmakeCache -Pattern "^CMAKE_GENERATOR:INTERNAL=(.+)$" |
    ForEach-Object { $_.Matches.Groups[1].Value } |
    Select-Object -First 1

if ($cachedGen -eq "NMake Makefiles") {
    Write-Host "WARNING: NMake is single-threaded. Prefer: .\build_windows.ps1 -Static -Clean (Ninja/VS)"
}

$buildArgs = @("--build", "build", "--config", "Release")
if ($cachedGen -eq "Ninja") {
    $buildArgs += @("-j", "$JobCount")
} elseif ($cachedGen -like "Visual Studio*") {
    $buildArgs += @("--", "/m:$JobCount")
} else {
    $buildArgs += @("-j", "$JobCount")
}

Write-Host "Generator: $cachedGen  jobs: $JobCount"
& $cmakeExe @buildArgs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$dll = Join-Path $PSScriptRoot "..\FishMMO-Unity\Assets\Plugins\FishNet\Plugins\WebTransport\Plugins\windows_x86_64\fishmmo_webtransport.dll"
if (Test-Path $dll) {
    Get-ChildItem $dll | ForEach-Object {
        Write-Host "OK $($_.Name)  $([math]::Round($_.Length/1KB,1)) KB"
    }
}
