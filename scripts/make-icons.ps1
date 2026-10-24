<#
.SYNOPSIS
    Generates the application icon (.ico), its large in-app image, the README icon (.png) and an SVG copy of the
    design (docs\icons).
.DESCRIPTION
    The generated files are committed: run this script again only to change the design.
    Design (chosen by the author among three rounds of proposals, October 21, 2026): four white bars of a visualizer,
    of different heights and centred on the middle line, on a rounded square with an indigo gradient (#6C7BFF to
    #2A2F9B).
    Small sizes (16 to 40 px, the taskbar, the notification area and File Explorer) are drawn on whole pixels to stay
    sharp.
    Requires Windows PowerShell 5.1 (System.Drawing).
#>
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot

$gradientStart = [System.Drawing.Color]::FromArgb(255, 0x6C, 0x7B, 0xFF)
$gradientEnd = [System.Drawing.Color]::FromArgb(255, 0x2A, 0x2F, 0x9B)

# The 256 px design: four bars 30 px wide, 16 px apart, fully rounded ends, centred on the middle line; background
# radius 23 % of its side, inside a 4 % transparent margin.
$design = @{
    Xs = @(44, 90, 136, 182); Heights = @(92, 156, 124, 68); W = 30
    Bg = 0.23; Margin = 0.04
}

# Under this size (taskbar, notification area, File Explorer lists), the design is put on whole pixels so that its
# bars stay sharp.
$smallUnder = 48

# The proportions of the 256 px design, on whole pixels for a small size: bars 11.7 % wide (2 px at least), 6.25 %
# apart (1 px at least), even heights so that they centre on a pixel edge, the group centred (half a pixel to the
# right when it cannot be exactly). Background radius 23 % and no transparent margin, as in the sister apps.
function Get-SmallSpec([int]$size) {
    $w = [Math]::Max(2, [Math]::Round($size * 30 / 256))
    $gap = [Math]::Max(1, [Math]::Round($size * 16 / 256))
    $x0 = [Math]::Ceiling(($size - (4 * $w + 3 * $gap)) / 2)
    @{
        Bg = [Math]::Round($size * 0.23 * 2) / 2
        Xs = @(0, 1, 2, 3 | ForEach-Object { $x0 + $_ * ($w + $gap) })
        Heights = @($design.Heights | ForEach-Object { 2 * [Math]::Round($size * $_ / 256 / 2) })
        W = $w
        R = [Math]::Min($w / 2, [Math]::Max(1, [Math]::Round($size * 0.04 * 2) / 2))
    }
}

function New-RoundedRectPath([System.Drawing.RectangleF]$r, [float]$radius) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    if ($radius -le 0) { $p.AddRectangle($r); return $p }
    $d = [Math]::Min($radius * 2, [Math]::Min($r.Width, $r.Height))
    $p.AddArc($r.X, $r.Y, $d, $d, 180, 90)
    $p.AddArc($r.Right - $d, $r.Y, $d, $d, 270, 90)
    $p.AddArc($r.Right - $d, $r.Bottom - $d, $d, $d, 0, 90)
    $p.AddArc($r.X, $r.Bottom - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

# The four bars, centred on the line $middle.
function Fill-Bars($g, $brush, $xs, $heights, [float]$w, [float]$middle, [float]$radius) {
    for ($i = 0; $i -lt 4; $i++) {
        $h = [float]$heights[$i]
        $bar = New-RoundedRectPath (New-Object System.Drawing.RectangleF ([float]$xs[$i]), ($middle - $h / 2), $w, $h) $radius
        $g.FillPath($brush, $bar)
        $bar.Dispose()
    }
}

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)

    if ($size -lt $smallUnder) {
        $spec = Get-SmallSpec $size
        $rect = New-Object System.Drawing.RectangleF 0, 0, $size, $size
        $bg = New-RoundedRectPath $rect ([float]$spec.Bg)
    }
    else {
        $m = [float]($size * $design.Margin)
        $s = [float]($size - 2 * $m)
        $rect = New-Object System.Drawing.RectangleF $m, $m, $s, $s
        $bg = New-RoundedRectPath $rect ([float]($s * $design.Bg))
    }
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, $gradientStart, $gradientEnd, ([float]45)
    $g.FillPath($brush, $bg)

    if ($size -lt $smallUnder) {
        Fill-Bars $g $white $spec.Xs $spec.Heights $spec.W ($size / 2) $spec.R
    }
    else {
        $k = $s / 256
        $g.TranslateTransform($m, $m)
        $g.ScaleTransform($k, $k)
        Fill-Bars $g $white $design.Xs $design.Heights $design.W 128 ($design.W / 2)
    }

    $brush.Dispose(); $bg.Dispose(); $white.Dispose(); $g.Dispose()
    return $bmp
}

# ---------------------------------------------------------------- Files

function Get-PngBytes([System.Drawing.Bitmap]$bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    return $ms.ToArray()
}

function Save-Png([int]$size, [string]$path) {
    New-Item -ItemType Directory -Force (Split-Path -Parent $path) | Out-Null
    $bmp = New-IconBitmap $size
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "  $path"
}

# ICO file made of PNG-compressed entries (supported since Windows Vista).
function Save-Ico([int[]]$sizes, [string]$path) {
    New-Item -ItemType Directory -Force (Split-Path -Parent $path) | Out-Null
    $images = foreach ($size in $sizes) { $b = New-IconBitmap $size; , (Get-PngBytes $b); $b.Dispose() }
    $fs = [System.IO.File]::Create($path)
    $bw = New-Object System.IO.BinaryWriter $fs
    $bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
        $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
        $bw.Write([UInt16]1); $bw.Write([UInt16]32)
        $bw.Write([UInt32]$images[$i].Length); $bw.Write([UInt32]$offset)
        $offset += $images[$i].Length
    }
    foreach ($img in $images) { $bw.Write([byte[]]$img) }
    $bw.Dispose()
    Write-Host "  $path"
}

# The 256 px design as SVG (docs\icons), to rework the icon in any vector editor. The app does not use it, and the
# small sizes stay drawn on whole pixels by this script.
function Get-Hex([System.Drawing.Color]$c) { '#' + $c.R.ToString('X2') + $c.G.ToString('X2') + $c.B.ToString('X2') }

function Get-AppSvg {
    $m = 256 * $design.Margin
    $k = (256 - 2 * $m) / 256
    $r = $design.W / 2
    $bars = for ($i = 0; $i -lt 4; $i++) {
        $h = $design.Heights[$i]
        "    <rect x=`"$($design.Xs[$i])`" y=`"$(128 - $h / 2)`" width=`"$($design.W)`" height=`"$h`" rx=`"$r`" fill=`"#FFFFFF`" />"
    }
    @"
<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 256 256">
  <!-- NowPlaying app icon, 256 px design. Written by scripts/make-icons.ps1: change the design there. -->
  <defs>
    <linearGradient id="gradient" x1="0" y1="0" x2="1" y2="1">
      <stop offset="0" stop-color="$(Get-Hex $gradientStart)" />
      <stop offset="1" stop-color="$(Get-Hex $gradientEnd)" />
    </linearGradient>
  </defs>
  <g transform="translate($m $m) scale($k)">
    <rect width="256" height="256" rx="$(256 * $design.Bg)" fill="url(#gradient)" />
$($bars -join "`n")
  </g>
</svg>
"@
}

function Save-Svg([string]$path, [string]$svg) {
    New-Item -ItemType Directory -Force (Split-Path -Parent $path) | Out-Null
    [System.IO.File]::WriteAllText($path, $svg.Replace("`r`n", "`n") + "`n", (New-Object System.Text.UTF8Encoding $false))
    Write-Host "  $path"
}

Write-Host 'Generated icons:'
Save-Ico @(16, 20, 24, 30, 32, 36, 40, 48, 64, 256) (Join-Path $root 'src\NowPlaying\Assets\NowPlaying.ico')
Save-Png 256 (Join-Path $root 'docs\icon-256.png')
# Large image shown inside the app (About card, Settings title): an .ico would display its first, 16 px, frame.
Save-Png 256 (Join-Path $root 'src\NowPlaying\Assets\NowPlaying.png')
Save-Svg (Join-Path $root 'docs\icons\NowPlaying.svg') (Get-AppSvg)
# Logos of the package that brings the widget to the Windows Widgets board (src\NowPlaying.Widgets\package).
$package = Join-Path $root 'src\NowPlaying.Widgets\package\Assets'
New-Item -ItemType Directory -Force $package | Out-Null
Save-Png 50 (Join-Path $package 'StoreLogo.png')
Save-Png 44 (Join-Path $package 'Square44x44Logo.png')
Save-Png 150 (Join-Path $package 'Square150x150Logo.png')
Save-Png 256 (Join-Path $package 'Widget.png')
# Logos of the Store package (store\AppxManifest.xml, scripts\build-store.ps1). Windows picks the size it needs: a
# scale for the Start menu and the Store, a target size for the taskbar and File Explorer ("unplated": the icon as it
# is, on no colored plate).
$store = Join-Path $root 'store\Assets'
New-Item -ItemType Directory -Force $store | Out-Null
foreach ($scale in 100, 200) {
    Save-Png (50 * $scale / 100) (Join-Path $store "StoreLogo.scale-$scale.png")
    Save-Png (44 * $scale / 100) (Join-Path $store "Square44x44Logo.scale-$scale.png")
    Save-Png (150 * $scale / 100) (Join-Path $store "Square150x150Logo.scale-$scale.png")
}
foreach ($size in 16, 20, 24, 30, 32, 36, 40, 48, 64, 256) {
    Save-Png $size (Join-Path $store "Square44x44Logo.targetsize-$size.png")
    Save-Png $size (Join-Path $store "Square44x44Logo.targetsize-$($size)_altform-unplated.png")
}
