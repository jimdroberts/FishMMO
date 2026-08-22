<#
.SYNOPSIS
    Publishes the standalone Updater for every platform FishMMO ships a client on.

.DESCRIPTION
    The .NET apphost is platform- and architecture-specific, so a Windows client and a
    Linux client each need their own binary; there is no portable one. All targets
    cross-publish from any host, so a single machine can produce the whole set.

    Output, one directory per RID:
        Updater\bin\Release\net8.0\<rid>\publish\Updater[.exe]

    That layout is exactly what the FishMMO Dashboard's BuildExecutor.CopyUpdaterToBuild
    looks for when it copies the updater into a client build, so publishing here is what
    makes a client build shippable.

.EXAMPLE
    .\publish-updater.ps1                 # win-x64 and linux-x64 (the shipped defaults)
.EXAMPLE
    .\publish-updater.ps1 -All            # every RID declared in Updater.csproj
.EXAMPLE
    .\publish-updater.ps1 linux-arm64     # an explicit list
#>
[CmdletBinding()]
param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $Rid,

    [switch] $All
)

$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $scriptDir 'Updater/Updater.csproj'

# Defaults: the two targets Unity can actually produce a standalone client for.
$defaultRids = @('win-x64', 'linux-x64')
# Everything Updater.csproj declares, for shipping to hosts Unity does not target directly.
$allRids = @('win-x64', 'win-x86', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')

if ($All) {
    $targets = $allRids
} elseif ($Rid -and $Rid.Count -gt 0) {
    $targets = $Rid
} else {
    $targets = $defaultRids
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "'dotnet' is not on PATH. Install the .NET 8 SDK (or newer) first."
}

Write-Host "Publishing Updater for: $($targets -join ', ')"
Write-Host ''

foreach ($target in $targets) {
    Write-Host "=== $target ==="

    # Self-contained + single-file + compression come from Updater.csproj, which applies
    # them whenever a RuntimeIdentifier is set. Passing them here too would just be a
    # second place to keep in sync.
    & dotnet publish $project -c Release -r $target --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $target (exit code $LASTEXITCODE)."
    }

    $publishDir = Join-Path $scriptDir "Updater/bin/Release/net8.0/$target/publish"
    $exe = if ($target -like 'win-*') { 'Updater.exe' } else { 'Updater' }
    $exePath = Join-Path $publishDir $exe

    if (-not (Test-Path $exePath)) {
        throw "Expected '$exePath' but it was not produced."
    }

    Write-Host "    -> $exePath"
    Write-Host ''
}

Write-Host 'Done. The FishMMO Dashboard copies these into client builds automatically.'
