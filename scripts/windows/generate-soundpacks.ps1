#Requires -Version 7
<#
.SYNOPSIS
Writes Windows/src/Blurt.App/Resources/soundpacks.json from App/Blurt/Blurt/SoundPackCatalog.swift.

.DESCRIPTION
Reads every `SoundPack(id: "...", label: "...", group: "...")` entry and the catalog's
`defaultVoiceID`, in catalog order, and writes them as
`{ "defaultVoiceId": "...", "voices": [ { "id", "label", "group" }, ... ] }`
(UTF-8 without BOM, LF line endings). Re-run it whenever the Swift catalog changes.
#>
[CmdletBinding()]
param(
    [string]$Source = (Join-Path $PSScriptRoot '../../App/Blurt/Blurt/SoundPackCatalog.swift'),
    [string]$Destination = (Join-Path $PSScriptRoot '../../Windows/src/Blurt.App/Resources/soundpacks.json')
)

$ErrorActionPreference = 'Stop'

$text = Get-Content -LiteralPath $Source -Raw -Encoding utf8

$defaultMatch = [regex]::Match($text, 'defaultVoiceID:\s*"(?<id>[^"]+)"')
if (-not $defaultMatch.Success) {
    throw "No defaultVoiceID found in $Source"
}

$pattern = 'SoundPack\(id:\s*"(?<id>[^"]+)",\s*label:\s*"(?<label>[^"]+)",\s*group:\s*"(?<group>[^"]+)"\)'
$voices = foreach ($match in [regex]::Matches($text, $pattern)) {
    [ordered]@{
        id    = $match.Groups['id'].Value
        label = $match.Groups['label'].Value
        group = $match.Groups['group'].Value
    }
}

if (-not $voices) {
    throw "No SoundPack entries found in $Source"
}

$catalog = [ordered]@{
    defaultVoiceId = $defaultMatch.Groups['id'].Value
    voices         = @($voices)
}

$options = [System.Text.Json.JsonSerializerOptions]::new()
$options.WriteIndented = $true
$options.Encoder = [System.Text.Encodings.Web.JavaScriptEncoder]::UnsafeRelaxedJsonEscaping
$json = [System.Text.Json.JsonSerializer]::Serialize($catalog, [System.Collections.Specialized.OrderedDictionary], $options)
$json = ($json -replace "`r`n", "`n") + "`n"

[System.IO.File]::WriteAllText([System.IO.Path]::GetFullPath($Destination), $json, [System.Text.UTF8Encoding]::new($false))
Write-Host "Wrote $(@($voices).Count) voices (default $($catalog.defaultVoiceId)) to $Destination"
