$ErrorActionPreference = "Stop"

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$installPath = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
$vsDevCmd = Join-Path $installPath "Common7\Tools\VsDevCmd.bat"

# Capture environment variables set by VsDevCmd.bat by running it and dumping `set` afterward.
$tempFile = [System.IO.Path]::GetTempFileName()
cmd /c "`"$vsDevCmd`" -arch=x64 -host_arch=x64 && set > `"$tempFile`""

Get-Content $tempFile | ForEach-Object {
    if ($_ -match '^([^=]+)=(.*)$') {
        [System.Environment]::SetEnvironmentVariable($matches[1], $matches[2], "Process")
    }
}
Remove-Item $tempFile -Force

Write-Host "=== VS Dev environment loaded; INCLUDE has $((($env:INCLUDE -split ';').Count)) entries ==="

& powershell -NoProfile -ExecutionPolicy Bypass -File "D:\WulthOnline\FishMMO\FishMMO-WebTransport\build_windows.ps1" -Static
exit $LASTEXITCODE
