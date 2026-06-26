# png_to_ico.ps1 - convert icon.png (256x256, transparent) into a multi-size icon.ico
# (256/48/32/16, PNG-compressed frames, alpha preserved). Run this if you change icon.png.
param([string]$In = "$PSScriptRoot\icon.png", [string]$Out = "$PSScriptRoot\icon.ico")
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$src = New-Object System.Drawing.Bitmap $In
$sizes = @(256,48,32,16)
$frames = @()
foreach ($s in $sizes) {
  if ($s -eq $src.Width -and $s -eq $src.Height) { $bmp = $src }
  else {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = 'HighQualityBicubic'; $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'; $g.CompositingQuality = 'HighQuality'
    $g.DrawImage($src, (New-Object System.Drawing.Rectangle 0,0,$s,$s))
    $g.Dispose()
  }
  $ms = New-Object System.IO.MemoryStream
  $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
  $frames += ,($ms.ToArray())
  if ($bmp -ne $src) { $bmp.Dispose() }
  $ms.Dispose()
}
$fs = [System.IO.File]::Create($Out); $bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$frames.Count)
$offset = 6 + 16 * $frames.Count
for ($i = 0; $i -lt $frames.Count; $i++) {
  $d = $sizes[$i]; $wb = if ($d -ge 256) { 0 } else { $d }
  $bw.Write([byte]$wb); $bw.Write([byte]$wb); $bw.Write([byte]0); $bw.Write([byte]0)
  $bw.Write([uint16]1); $bw.Write([uint16]32)
  $bw.Write([uint32]$frames[$i].Length); $bw.Write([uint32]$offset); $offset += $frames[$i].Length
}
foreach ($f in $frames) { $bw.Write($f) }
$bw.Close(); $fs.Close(); $src.Dispose()
Write-Output ("wrote {0}" -f $Out)
