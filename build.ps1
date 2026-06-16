<#
.SYNOPSIS
    Builds / publishes the AOE agents (Aoe.Alpha and Aoe.Beta).

.DESCRIPTION
    By default this produces framework-dependent, single-file win-x64 executables in
    .\publish\<Project>\. Framework-dependent builds are small (a few MB) but require the
    .NET 8 Desktop Runtime to be installed on the target machine:
        winget install Microsoft.DotNet.DesktopRuntime.8

    Use -SelfContained to bundle the runtime instead (no install needed on the target,
    but the exe is ~70-150 MB).

.PARAMETER Project
    Which agent to publish: Alpha, Beta, or All (default).

.PARAMETER Configuration
    Build configuration: Release (default) or Debug.

.PARAMETER SelfContained
    Bundle the .NET runtime into the exe (no runtime install needed on the target).

.PARAMETER Compress
    With -SelfContained, compress the single-file payload (smaller, slightly slower start).

.PARAMETER Output
    Root output folder (default: .\publish).

.EXAMPLE
    .\build.ps1
    Publishes both agents, framework-dependent, to .\publish\Alpha and .\publish\Beta.

.EXAMPLE
    .\build.ps1 -Project Beta -SelfContained -Compress
    Publishes a standalone, compressed Beta.exe that runs without installing .NET.
#>
[CmdletBinding()]
param(
    [ValidateSet('Alpha', 'Beta', 'All')]
    [string]$Project = 'All',

    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',

    [switch]$SelfContained,

    [switch]$Compress,

    [string]$Output
)

$ErrorActionPreference = 'Stop'
$runtime = 'win-x64'

$root = if ($PSScriptRoot) { $PSScriptRoot }
        elseif ($MyInvocation.MyCommand.Path) { Split-Path -Parent $MyInvocation.MyCommand.Path }
        else { (Get-Location).Path }

if (-not $Output) { $Output = Join-Path $root 'publish' }

function Publish-Agent {
    param([string]$Name)

    $proj = Join-Path $root "Aoe.$Name\Aoe.$Name.csproj"
    $dest = Join-Path $Output $Name

    Write-Host "==> Publishing Aoe.$Name ($Configuration, $(if ($SelfContained) {'self-contained'} else {'framework-dependent'}))" -ForegroundColor Cyan

    $publishArgs = @(
        'publish', $proj,
        '-c', $Configuration,
        '-r', $runtime,
        '--self-contained', $(if ($SelfContained) { 'true' } else { 'false' }),
        '-p:PublishSingleFile=true',
        '-p:DebugType=none',
        '-o', $dest,
        '--nologo'
    )

    if ($SelfContained) {
        $publishArgs += '-p:IncludeNativeLibrariesForSelfExtract=true'
        if ($Compress) { $publishArgs += '-p:EnableCompressionInSingleFile=true' }
    }

    dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) { throw "Publish failed for Aoe.$Name" }

    $exe = Join-Path $dest "Aoe.$Name.exe"
    if (Test-Path $exe) {
        $mb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
        Write-Host "    $exe  ($mb MB)" -ForegroundColor Green
    }
}

$targets = if ($Project -eq 'All') { @('Alpha', 'Beta') } else { @($Project) }
foreach ($t in $targets) { Publish-Agent -Name $t }

Write-Host ""
Write-Host "Done. Output in: $Output" -ForegroundColor Green
if (-not $SelfContained) {
    Write-Host "Framework-dependent build: target needs the .NET 8 Desktop Runtime:" -ForegroundColor Yellow
    Write-Host "    winget install Microsoft.DotNet.DesktopRuntime.8" -ForegroundColor Yellow
}
