<#
.SYNOPSIS
    Publishes Blurt.App for Windows.
.DESCRIPTION
    Builds and publishes Windows/src/Blurt.App as either a framework-dependent
    build (default) or a self-contained single-file build (-SelfContained).
.PARAMETER Configuration
    The build configuration to publish. Defaults to Release.
.PARAMETER SelfContained
    Publishes a self-contained, single-file win-x64 build instead of a
    framework-dependent one.
#>
param(
    [string]$Configuration = 'Release',
    [switch]$SelfContained
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$project = Join-Path $repoRoot 'Windows/src/Blurt.App/Blurt.App.csproj'

if ($SelfContained) {
    $outputDir = Join-Path $repoRoot 'Windows/publish/sc'
    dotnet publish $project `
        -c $Configuration `
        -r win-x64 `
        -o $outputDir `
        -p:SelfContained=true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true
} else {
    $outputDir = Join-Path $repoRoot 'Windows/publish/fd'
    dotnet publish $project `
        -c $Configuration `
        -r win-x64 `
        -o $outputDir
}

Write-Host "Published to: $outputDir"
