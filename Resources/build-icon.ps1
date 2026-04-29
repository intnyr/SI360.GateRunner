# Generate multi-resolution .ico for SI360.GateRunner
# Design: navy rounded square + white play triangle (matches Run Gates button)
Add-Type -AssemblyName System.Drawing

$sizes   = @(16, 24, 32, 48, 64, 128, 256)
$navy    = [System.Drawing.Color]::FromArgb(255, 5, 36, 114)      # #052472 (PrimaryBrush light)
$accent  = [System.Drawing.Color]::FromArgb(255, 10, 75, 173)     # #0A4BAD (PrimaryBrush dark)
$fg      = [System.Drawing.Color]::White

$outDir  = Join-Path $PSScriptRoot ""
$icoPath = Join-Path $outDir "gaterunner.ico"

$pngs = New-Object System.Collections.Generic.List[object]

foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

    # rounded rect background (gradient navy -> accent)
    $r = [Math]::Max(2, [int]($s * 0.18))
    $d = $r * 2
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, $d, $d, 180, 90)
    $path.AddArc($s - $d - 1, 0, $d, $d, 270, 90)
    $path.AddArc($s - $d - 1, $s - $d - 1, $d, $d, 0, 90)
    $path.AddArc(0, $s - $d - 1, $d, $d, 90, 90)
    $path.CloseFigure()

    $rectF = New-Object System.Drawing.RectangleF(0, 0, $s, $s)
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $rectF, $navy, $accent, 45.0)
    $g.FillPath($grad, $path)

    # subtle inner ring for depth (>=32px only)
    if ($s -ge 32) {
        $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(60, 255, 255, 255),
            [Math]::Max(1.0, $s / 128.0))
        $g.DrawPath($pen, $path)
        $pen.Dispose()
    }

    # play triangle ▶ centered with optical offset
    $triH = [int]($s * 0.48)
    $triW = [int]($triH * 0.9)
    $cx = [int]($s * 0.52)
    $cy = [int]($s * 0.50)
    $p1 = New-Object System.Drawing.Point(($cx - [int]($triW / 2)), ($cy - [int]($triH / 2)))
    $p2 = New-Object System.Drawing.Point(($cx - [int]($triW / 2)), ($cy + [int]($triH / 2)))
    $p3 = New-Object System.Drawing.Point(($cx + [int]($triW / 2)), $cy)
    $tri = @($p1, $p2, $p3)

    $wBrush = New-Object System.Drawing.SolidBrush($fg)
    $g.FillPolygon($wBrush, $tri)

    $wBrush.Dispose()
    $grad.Dispose()
    $path.Dispose()
    $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs.Add(@{ Size = $s; Data = $ms.ToArray() })
    $bmp.Dispose()
    $ms.Dispose()
}

# Write ICO container
if (Test-Path $icoPath) { Remove-Item $icoPath -Force }
$fs = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter($fs)

# ICONDIR
$bw.Write([uint16]0)               # reserved
$bw.Write([uint16]1)               # type: 1 = icon
$bw.Write([uint16]$pngs.Count)     # image count

$offset = 6 + (16 * $pngs.Count)

# ICONDIRENTRY per image
foreach ($p in $pngs) {
    $sz = $p.Size
    $w = if ($sz -ge 256) { 0 } else { $sz }
    $h = if ($sz -ge 256) { 0 } else { $sz }
    $bw.Write([byte]$w)
    $bw.Write([byte]$h)
    $bw.Write([byte]0)              # palette size
    $bw.Write([byte]0)              # reserved
    $bw.Write([uint16]1)            # color planes
    $bw.Write([uint16]32)           # bits per pixel
    $bw.Write([uint32]$p.Data.Length)
    $bw.Write([uint32]$offset)
    $offset += $p.Data.Length
}

# image data (PNG-encoded)
foreach ($p in $pngs) { $bw.Write($p.Data) }

$bw.Flush()
$bw.Close()
$fs.Close()

Write-Host "Wrote $icoPath ($((Get-Item $icoPath).Length) bytes, $($pngs.Count) sizes: $($sizes -join ', '))"
