# Build fishmmo_webtransport.dll for Windows x86_64 against the prebuilt
# Microsoft.Native.Quic.MsQuic.<Tls> NuGet package. Compiles only the seven
# wrapper sources — seconds after the first package download. No CMake, no
# Perl, no quictls build.
#
# -Tls OpenSSL (default)
#   msquic.dll with quictls built in. Loads the PEM certificate/key files the
#   server configs ship with (CertificatePath / PrivateKeyPath) and is the only
#   flavour that can host a server for this library. Self-contained: no
#   OpenSSL installation is needed at build or run time.
#
# -Tls Schannel
#   CLIENT-ONLY. Schannel takes certificates from the Windows certificate
#   store and msquic answers QUIC_STATUS_NOT_SUPPORTED to the PEM files this
#   library passes; a server refuses to start with WT_ERR_TLS_BACKEND (-8).
#   Kept as an escape hatch for client builds on hosts that must not carry a
#   second TLS stack.
#
# Prerequisites:
#   - Visual Studio 2022/2026 with C++ desktop workload (cl.exe + link.exe)
#   - Windows 10 SDK
#
# Usage (normally via build_windows.ps1, which forwards -Tls):
#   powershell -ExecutionPolicy Bypass -File build_windows_nuget.ps1
#   powershell -ExecutionPolicy Bypass -File build_windows_nuget.ps1 -Tls Schannel
#
# Output:
#   ../FishMMO-Unity/Assets/Plugins/FishNet/Plugins/WebTransport/Plugins/windows_x86_64/
#     fishmmo_webtransport.dll
#     msquic.dll                (the flavour selected by -Tls)
#
# Intermediates live in build_win_nuget/ (gitignored): one msquic-<tls>/
# extraction per flavour, a shared obj/ tree, and last_tls.txt recording which
# flavour the DLL in the Unity folder was last linked against — switching
# flavours forces a relink and a fresh msquic.dll copy even when no source
# changed.

param(
    [ValidateSet("OpenSSL", "Schannel")]
    [string]$Tls = "OpenSSL"
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$MsquicVer = "2.5.9"
$Package = "Microsoft.Native.Quic.MsQuic.$Tls"
$BuildDir = Join-Path $PSScriptRoot "build_win_nuget"
$UnityDir = Join-Path $PSScriptRoot "..\FishMMO-Unity\Assets\Plugins\FishNet\Plugins\WebTransport\Plugins\windows_x86_64"
$MsquicDir = Join-Path $BuildDir ("msquic-" + $Tls.ToLowerInvariant())
$ObjDir = Join-Path $BuildDir "obj"
$TlsStamp = Join-Path $BuildDir "last_tls.txt"

Write-Host "=== FishMMO WebTransport - Windows x86_64 (NuGet msquic, TLS: $Tls) ==="
if ($Tls -eq "Schannel") {
    Write-Host ""
    Write-Host "WARNING: Schannel build is CLIENT-ONLY for this library."
    Write-Host "  Schannel loads certificates from the Windows certificate store; the PEM files"
    Write-Host "  the server configs point at (CertificatePath / PrivateKeyPath) cannot be used,"
    Write-Host "  so wt_server_start will fail with WT_ERR_TLS_BACKEND (-8) and no port is bound."
    Write-Host "  Build a server with the default:  .\build_windows.ps1   (OpenSSL flavour)"
    Write-Host ""
}

$TlsChanged = $true
if (Test-Path $TlsStamp) {
    $last = (Get-Content $TlsStamp -Raw).Trim()
    if ($last -eq $Tls) { $TlsChanged = $false }
    else { Write-Host "TLS flavour changed ($last -> $Tls): relinking and refreshing msquic.dll." }
}

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path $vswhere)) {
    throw "vswhere.exe not found. Install Visual Studio with C++ tools."
}

$installPath = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if ([string]::IsNullOrWhiteSpace($installPath)) {
    throw "No Visual Studio install with VC tools found."
}

$vsDevCmd = Join-Path $installPath "Common7\Tools\VsDevCmd.bat"
if (-not (Test-Path $vsDevCmd)) {
    throw "VsDevCmd.bat not found under $installPath"
}

Write-Host "VS: $installPath"

New-Item -ItemType Directory -Force -Path $BuildDir, $ObjDir, $UnityDir | Out-Null

$nupkg = Join-Path $BuildDir ("msquic-" + $Tls.ToLowerInvariant() + ".nupkg")
$msquicDll = Join-Path $MsquicDir "build\native\bin\x64\msquic.dll"
$msquicLib = Join-Path $MsquicDir "build\native\lib\x64\msquic.lib"
$msquicInc = Join-Path $MsquicDir "build\native\include"

if (-not (Test-Path $msquicDll)) {
    Write-Host "Downloading $Package $MsquicVer..."
    $url = "https://www.nuget.org/api/v2/package/$Package/$MsquicVer"
    Invoke-WebRequest -Uri $url -OutFile $nupkg -UseBasicParsing
    if (Test-Path $MsquicDir) { Remove-Item $MsquicDir -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $MsquicDir | Out-Null
    # NuGet packages are zip archives; Expand-Archive requires a .zip extension.
    $zip = Join-Path $BuildDir ("msquic-" + $Tls.ToLowerInvariant() + ".zip")
    Copy-Item -Force $nupkg $zip
    Expand-Archive -Path $zip -DestinationPath $MsquicDir -Force
}

if (-not (Test-Path $msquicDll)) { throw "msquic.dll missing after NuGet extract: $msquicDll" }
if (-not (Test-Path $msquicLib)) {
    $alt = Get-ChildItem $MsquicDir -Recurse -Filter "msquic.lib" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($alt) { $msquicLib = $alt.FullName }
    else { throw "msquic.lib not found under $MsquicDir" }
}
if (-not (Test-Path (Join-Path $msquicInc "msquic.h"))) {
    $altInc = Get-ChildItem $MsquicDir -Recurse -Filter "msquic.h" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($altInc) { $msquicInc = $altInc.DirectoryName }
    else { throw "msquic.h not found under $MsquicDir" }
}

Write-Host "msquic.dll : $msquicDll"
Write-Host "msquic.lib : $msquicLib"
Write-Host "msquic.h   : $msquicInc"

$sources = @(
    "webtransport_api",
    "server",
    "client",
    "session",
    "datagram_queue",
    "stream_manager",
    "http3"
)

$srcDir = Join-Path $PSScriptRoot "src"
$outDll = Join-Path $UnityDir "fishmmo_webtransport.dll"
$implib = Join-Path $BuildDir "fishmmo_webtransport.lib"

# Resolve MSVC + Windows SDK paths (VsDevCmd on some machines leaves INCLUDE empty).
$msvcRoot = Get-ChildItem (Join-Path $installPath "VC\Tools\MSVC") -Directory |
    Sort-Object Name -Descending |
    Select-Object -First 1 -ExpandProperty FullName
if (-not $msvcRoot) { throw "MSVC toolset directory not found under $installPath" }

$sdkRoot = "C:\Program Files (x86)\Windows Kits\10"
$sdkVer = Get-ChildItem (Join-Path $sdkRoot "Include") -Directory |
    Where-Object { Test-Path (Join-Path $_.FullName "ucrt") } |
    Sort-Object Name -Descending |
    Select-Object -First 1 -ExpandProperty Name
if (-not $sdkVer) { throw "Windows 10 SDK includes not found under $sdkRoot\Include" }

$includePath = @(
    (Join-Path $msvcRoot "include"),
    (Join-Path $sdkRoot "Include\$sdkVer\ucrt"),
    (Join-Path $sdkRoot "Include\$sdkVer\um"),
    (Join-Path $sdkRoot "Include\$sdkVer\shared"),
    (Join-Path $sdkRoot "Include\$sdkVer\winrt"),
    $srcDir,
    $msquicInc
) -join ";"

$libPath = @(
    (Join-Path $msvcRoot "lib\x64"),
    (Join-Path $sdkRoot "Lib\$sdkVer\ucrt\x64"),
    (Join-Path $sdkRoot "Lib\$sdkVer\um\x64")
) -join ";"

Write-Host "MSVC: $msvcRoot"
Write-Host "SDK:  $sdkVer"

# Incremental: recompile a .cpp only when it (or any shared header) is newer than .obj.
$headerFiles = @(Get-ChildItem -Path $srcDir -Filter "*.h" -File -ErrorAction SilentlyContinue)
$newestHeader = ($headerFiles | Measure-Object -Property LastWriteTime -Maximum).Maximum
if (-not $newestHeader) { $newestHeader = [datetime]::MinValue }

$toCompile = New-Object System.Collections.Generic.List[string]
foreach ($s in $sources) {
    $cpp = Join-Path $srcDir "$s.cpp"
    $obj = Join-Path $ObjDir "$s.obj"
    $needs = $true
    if ((Test-Path $cpp) -and (Test-Path $obj)) {
        $cppTime = (Get-Item $cpp).LastWriteTime
        $objTime = (Get-Item $obj).LastWriteTime
        if (($objTime -ge $cppTime) -and ($objTime -ge $newestHeader)) {
            $needs = $false
        }
    }
    if ($needs) { [void]$toCompile.Add($s) }
}

$needLink = $true
if ((Test-Path $outDll) -and ($toCompile.Count -eq 0) -and (-not $TlsChanged)) {
    $dllTime = (Get-Item $outDll).LastWriteTime
    $anyObjNewer = $false
    foreach ($s in $sources) {
        $obj = Join-Path $ObjDir "$s.obj"
        if (-not (Test-Path $obj)) { $anyObjNewer = $true; break }
        if ((Get-Item $obj).LastWriteTime -gt $dllTime) { $anyObjNewer = $true; break }
    }
    $msquicDllTime = (Get-Item $msquicDll).LastWriteTime
    $destMsquic = Join-Path $UnityDir "msquic.dll"
    $msquicCopyNeeded = $false
    if (-not (Test-Path $destMsquic)) {
        $msquicCopyNeeded = $true
    } elseif ((Get-Item $destMsquic).LastWriteTime -lt $msquicDllTime) {
        $msquicCopyNeeded = $true
    }
    if ((-not $anyObjNewer) -and (-not $msquicCopyNeeded)) {
        Set-Content -Path $TlsStamp -Value $Tls -NoNewline
        Write-Host "All objects and DLL up to date - nothing to do."
        Get-ChildItem $UnityDir | ForEach-Object {
            $kb = [math]::Round($_.Length / 1KB, 1)
            Write-Host ("  {0}  {1} KB" -f $_.Name, $kb)
        }
        return
    }
    if ((-not $anyObjNewer) -and $msquicCopyNeeded) {
        Write-Host "Wrapper DLL up to date; refreshing msquic.dll only..."
        Copy-Item -Force $msquicDll $destMsquic
        Set-Content -Path $TlsStamp -Value $Tls -NoNewline
        Write-Host "=== Done ==="
        return
    }
}

$batLines = New-Object System.Collections.Generic.List[string]
$batLines.Add("@echo off")
$batLines.Add("call `"$vsDevCmd`" -arch=amd64 -host_arch=amd64")
$batLines.Add("if errorlevel 1 exit /b 1")
$batLines.Add("set `"INCLUDE=$includePath`"")
$batLines.Add("set `"LIB=$libPath`"")
$batLines.Add("set `"LIBPATH=$(Join-Path $msvcRoot 'lib\x64')`"")

if ($toCompile.Count -eq 0) {
    Write-Host "Objects up to date - linking only..."
} else {
    Write-Host ("Compiling {0}/{1} source(s): {2}" -f $toCompile.Count, $sources.Count, ($toCompile -join ", "))
}

foreach ($s in $toCompile) {
    $batLines.Add("echo Compiling $s.cpp...")
    $batLines.Add("cl /nologo /c /EHsc /std:c++17 /O2 /MD /DWT_BUILDING_DLL /DWT_PLATFORM_WINDOWS /D_CRT_SECURE_NO_WARNINGS /DNDEBUG /I`"$srcDir`" /I`"$msquicInc`" /Fo`"$ObjDir\$s.obj`" `"$srcDir\$s.cpp`"")
    $batLines.Add("if errorlevel 1 exit /b 1")
}

if ($needLink) {
    $objList = ($sources | ForEach-Object { "`"$ObjDir\$_.obj`"" }) -join " "
    $batLines.Add("echo Linking fishmmo_webtransport.dll...")
    $batLines.Add("link /nologo /DLL /OUT:`"$outDll`" /IMPLIB:`"$implib`" $objList `"$msquicLib`" ws2_32.lib bcrypt.lib ncrypt.lib crypt32.lib advapi32.lib ntdll.lib")
    $batLines.Add("if errorlevel 1 exit /b 1")
}

$batLines.Add("copy /Y `"$msquicDll`" `"$UnityDir\msquic.dll`"")
$batLines.Add("if errorlevel 1 exit /b 1")
$batLines.Add("echo.")
$batLines.Add("echo === Done ===")
$batLines.Add("dir /b `"$UnityDir`"")

$batPath = Join-Path $BuildDir "compile_link.bat"
[System.IO.File]::WriteAllLines($batPath, $batLines)

Write-Host "Running MSVC compile/link..."
$proc = Start-Process -FilePath "cmd.exe" -ArgumentList "/c `"$batPath`"" -Wait -PassThru -NoNewWindow
if ($proc.ExitCode -ne 0) {
    throw "Build failed with exit code $($proc.ExitCode). See output above."
}

if (-not (Test-Path $outDll)) {
    throw "Build reported success but $outDll is missing."
}
Set-Content -Path $TlsStamp -Value $Tls -NoNewline

Get-ChildItem $UnityDir | ForEach-Object {
    $kb = [math]::Round($_.Length / 1KB, 1)
    Write-Host ("  {0}  {1} KB" -f $_.Name, $kb)
}

Write-Host ""
if ($Tls -eq "OpenSSL") {
    Write-Host "TLS provider: OpenSSL (quictls inside msquic.dll) - loads PEM certificates, hosts servers."
} else {
    Write-Host "TLS provider: Schannel - client-only; servers will refuse to start (WT_ERR_TLS_BACKEND)."
}
Write-Host "Import the new DLLs in Unity (Assets refresh), then Play MainBootstrap again."
