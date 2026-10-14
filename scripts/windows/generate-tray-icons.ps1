#Requires -Version 7
<#
.SYNOPSIS
Writes the tray icons (idle, recording, transcribing) to Windows/src/Blurt.App/Resources/Icons/.

.DESCRIPTION
Draws each glyph with System.Drawing at 16 and 32 px and writes a two-image .ico of 32-bit
uncompressed DIB entries. Re-run it to regenerate the committed icons.
#>
[CmdletBinding()]
param(
    [string]$Destination = (Join-Path $PSScriptRoot '../../Windows/src/Blurt.App/Resources/Icons')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$green = [System.Drawing.Color]::FromArgb(255, 103, 173, 130)
$red = [System.Drawing.Color]::FromArgb(255, 229, 72, 77)
$orange = [System.Drawing.Color]::FromArgb(255, 230, 127, 54)
$white = [System.Drawing.Color]::White

function Draw-Glyph([string]$kind, [int]$size) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 16.0
    switch ($kind) {
        'idle' {
            $pen = [System.Drawing.Pen]::new($green, 2 * $s)
            $g.DrawEllipse($pen, 2 * $s, 2 * $s, 12 * $s, 12 * $s)
            $g.FillEllipse([System.Drawing.SolidBrush]::new($green), 6 * $s, 6 * $s, 4 * $s, 4 * $s)
        }
        'recording' {
            $g.FillEllipse([System.Drawing.SolidBrush]::new($red), 1 * $s, 1 * $s, 14 * $s, 14 * $s)
            $g.FillEllipse([System.Drawing.SolidBrush]::new($white), 5.5 * $s, 5.5 * $s, 5 * $s, 5 * $s)
        }
        'transcribing' {
            $pen = [System.Drawing.Pen]::new($orange, 2 * $s)
            $g.DrawEllipse($pen, 1.5 * $s, 1.5 * $s, 13 * $s, 13 * $s)
            $dot = [System.Drawing.SolidBrush]::new($orange)
            foreach ($x in 4, 7, 10) {
                $g.FillEllipse($dot, $x * $s, 7 * $s, 2 * $s, 2 * $s)
            }
        }
    }
    $g.Dispose()
    return $bitmap
}

function Get-DibBytes([System.Drawing.Bitmap]$bitmap) {
    $size = $bitmap.Width
    $stream = [System.IO.MemoryStream]::new()
    $writer = [System.IO.BinaryWriter]::new($stream)
    $maskStride = [int]([math]::Ceiling($size / 32.0) * 4)
    # BITMAPINFOHEADER: the height covers the XOR image and the AND mask stacked.
    $writer.Write([int]40)
    $writer.Write([int]$size)
    $writer.Write([int]($size * 2))
    $writer.Write([int16]1)
    $writer.Write([int16]32)
    $writer.Write([int]0)
    $writer.Write([int]($size * $size * 4 + $maskStride * $size))
    $writer.Write([int]0)
    $writer.Write([int]0)
    $writer.Write([int]0)
    $writer.Write([int]0)
    for ($y = $size - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $size; $x++) {
            $p = $bitmap.GetPixel($x, $y)
            $writer.Write([byte]$p.B)
            $writer.Write([byte]$p.G)
            $writer.Write([byte]$p.R)
            $writer.Write([byte]$p.A)
        }
    }
    # An all-zero AND mask: transparency comes from the alpha channel.
    $writer.Write([byte[]]::new($maskStride * $size))
    $writer.Flush()
    return $stream.ToArray()
}

function Write-Icon([string]$kind, [string]$path) {
    $sizes = 16, 32
    $images = foreach ($size in $sizes) {
        $bitmap = Draw-Glyph $kind $size
        , (Get-DibBytes $bitmap)
        $bitmap.Dispose()
    }
    $stream = [System.IO.MemoryStream]::new()
    $writer = [System.IO.BinaryWriter]::new($stream)
    $writer.Write([int16]0)
    $writer.Write([int16]1)
    $writer.Write([int16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $writer.Write([byte]$sizes[$i])
        $writer.Write([byte]$sizes[$i])
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([int16]1)
        $writer.Write([int16]32)
        $writer.Write([int]$images[$i].Length)
        $writer.Write([int]$offset)
        $offset += $images[$i].Length
    }
    foreach ($image in $images) {
        $writer.Write([byte[]]$image)
    }
    $writer.Flush()
    [System.IO.File]::WriteAllBytes($path, $stream.ToArray())
}

$directory = [System.IO.Path]::GetFullPath($Destination)
[System.IO.Directory]::CreateDirectory($directory) | Out-Null
foreach ($kind in 'idle', 'recording', 'transcribing') {
    $path = Join-Path $directory "$kind.ico"
    Write-Icon $kind $path
    Write-Host "Wrote $path"
}
