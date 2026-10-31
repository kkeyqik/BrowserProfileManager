# make-icon.ps1
# Draws the Browser Profile Manager icon with System.Drawing and writes:
#   app.ico      - multi-image ICO, every image stored as PNG
#   app-256.png  - 256 px image on its own
# Windows PowerShell 5.1, no downloads, no modules. Safe to re-run (overwrites outputs).

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$outDir   = $PSScriptRoot
$icoPath  = Join-Path $outDir 'app.ico'
$pngPath  = Join-Path $outDir 'app-256.png'
$sizes    = @(16, 24, 32, 48, 64, 128, 256)

$colBlue   = [System.Drawing.ColorTranslator]::FromHtml('#3B82F6')
$colIndigo = [System.Drawing.ColorTranslator]::FromHtml('#4F46E5')
$colWhite  = [System.Drawing.Color]::White

# Rounded-rectangle path (arcs at corners). Returned unwrapped via the comma operator.
function New-RoundRectPath {
    param([double]$x, [double]$y, [double]$w, [double]$h, [double]$r)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    if ($w -le 0 -or $h -le 0) { return ,$path }
    $r = [Math]::Max(0, [Math]::Min($r, [Math]::Min($w, $h) / 2))
    if ($r -le 0) {
        $path.AddRectangle((New-Object System.Drawing.RectangleF ([single]$x), ([single]$y), ([single]$w), ([single]$h)))
        return ,$path
    }
    $d = 2 * $r
    $path.AddArc([single]$x, [single]$y, [single]$d, [single]$d, 180, 90)
    $path.AddArc([single]($x + $w - $d), [single]$y, [single]$d, [single]$d, 270, 90)
    $path.AddArc([single]($x + $w - $d), [single]($y + $h - $d), [single]$d, [single]$d, 0, 90)
    $path.AddArc([single]$x, [single]($y + $h - $d), [single]$d, [single]$d, 90, 90)
    $path.CloseFigure()
    return ,$path
}

function New-IconBitmap {
    param([int]$S)

    $bmp = New-Object System.Drawing.Bitmap $S, $S, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic

    # Background: rounded square with a small transparent margin, vertical gradient.
    $m  = [Math]::Max(1, [Math]::Round($S * 0.03))
    $bs = $S - 2 * $m
    $bgPath = New-RoundRectPath $m $m $bs $bs (0.22 * $bs)
    $bgRect = New-Object System.Drawing.RectangleF ([single]$m), ([single]$m), ([single]$bs), ([single]$bs)
    $bgBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $bgRect, $colBlue, $colIndigo, ([System.Drawing.Drawing2D.LinearGradientMode]::Vertical)
    $g.FillPath($bgBrush, $bgPath)

    $whiteBrush = New-Object System.Drawing.SolidBrush $colWhite
    $blueBrush  = New-Object System.Drawing.SolidBrush $colBlue

    # Browser window geometry: 64% wide, 52% tall, centred slightly above middle.
    $ww = 0.64 * $S
    $wh = 0.52 * $S
    $wx = ($S - $ww) / 2
    $wy = ($S - $wh) / 2 - 0.02 * $S
    $wr = 0.09 * $S
    $sw = [Math]::Max(1.0, $S / 32.0)
    $pen = New-Object System.Drawing.Pen $colWhite, ([single]$sw)

    # Outline (inset by half the stroke so it stays inside the box).
    $outline = New-RoundRectPath ($wx + $sw / 2) ($wy + $sw / 2) ($ww - $sw) ($wh - $sw) ([Math]::Max(0, $wr - $sw / 2))
    $g.DrawPath($pen, $outline)

    # Filled title bar: fill the window shape clipped to the top 18% strip.
    $tb = 0.18 * $wh
    $winPath = New-RoundRectPath $wx $wy $ww $wh $wr
    $g.SetClip((New-Object System.Drawing.RectangleF ([single]$wx), ([single]$wy), ([single]$ww), ([single]$tb)))
    $g.FillPath($whiteBrush, $winPath)
    $g.ResetClip()

    # Three dots in the title bar (48 px and up), drawn in the background blue.
    if ($S -ge 48) {
        $d = [Math]::Max(1.5, $tb * 0.5)
        $cy = $wy + $tb / 2
        for ($i = 0; $i -lt 3; $i++) {
            $cx = $wx + $tb * 0.55 + $i * $d * 1.9
            $g.FillEllipse($blueBrush, [single]($cx - $d / 2), [single]($cy - $d / 2), [single]$d, [single]$d)
        }
    }

    $bodyTop = $wy + $tb
    $bodyH   = $wh - $tb
    $tw      = $ww * 0.6
    $tx      = $wx + ($ww - $tw) / 2

    if ($S -ge 32) {
        # Two toggles: top "on" (filled, knob right), bottom "off" (outlined, knob left).
        $pad = $bodyH * 0.14
        $gap = $bodyH * 0.12
        $th  = ($bodyH - 2 * $pad - $gap) / 2
        $y1  = $bodyTop + $pad
        $y2  = $y1 + $th + $gap

        $inset = [Math]::Max(0.5, $th * 0.14)
        $kd = [Math]::Max(1.0, $th - 2 * $inset)
        $onPath = New-RoundRectPath $tx $y1 $tw $th ($th / 2)
        $g.FillPath($whiteBrush, $onPath)
        $g.FillEllipse($blueBrush, [single]($tx + $tw - $inset - $kd), [single]($y1 + $inset), [single]$kd, [single]$kd)

        $offPen = New-Object System.Drawing.Pen $colWhite, ([single]$sw)
        $offPath = New-RoundRectPath ($tx + $sw / 2) ($y2 + $sw / 2) ($tw - $sw) ($th - $sw) (($th - $sw) / 2)
        $g.DrawPath($offPen, $offPath)
        $offInset = $sw + $inset
        $kd2 = [Math]::Max(1.0, $th - 2 * $offInset)
        $g.FillEllipse($whiteBrush, [single]($tx + $offInset), [single]($y2 + $offInset), [single]$kd2, [single]$kd2)
        $offPen.Dispose()
        $onPath.Dispose(); $offPath.Dispose()
    }
    else {
        # 16 and 24 px: one filled "on" toggle only.
        $th  = $bodyH * 0.42
        $ty  = $bodyTop + ($bodyH - $th) / 2
        $inset = [Math]::Max(0.5, $th * 0.14)
        $kd = [Math]::Max(1.0, $th - 2 * $inset)
        $onPath = New-RoundRectPath $tx $ty $tw $th ($th / 2)
        $g.FillPath($whiteBrush, $onPath)
        $g.FillEllipse($blueBrush, [single]($tx + $tw - $inset - $kd), [single]($ty + $inset), [single]$kd, [single]$kd)
        $onPath.Dispose()
    }

    $bgPath.Dispose(); $bgBrush.Dispose(); $outline.Dispose(); $winPath.Dispose()
    $pen.Dispose(); $whiteBrush.Dispose(); $blueBrush.Dispose()
    $g.Dispose()
    return ,$bmp
}

# Render every size to PNG bytes.
$pngs = @{}
foreach ($s in $sizes) {
    $bmp = New-IconBitmap $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs[$s] = $ms.ToArray()
    $ms.Dispose()
    $bmp.Dispose()
}

# Standalone 256 px PNG.
[System.IO.File]::WriteAllBytes($pngPath, [byte[]]$pngs[256])

# ICO container: ICONDIR + one ICONDIRENTRY per image + PNG payloads in the same order.
$ico = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $ico
$bw.Write([uint16]0)                 # reserved
$bw.Write([uint16]1)                 # type 1 = icon
$bw.Write([uint16]$sizes.Count)      # image count
$offset = 6 + 16 * $sizes.Count
foreach ($s in $sizes) {
    $dim = if ($s -eq 256) { 0 } else { $s }
    $bw.Write([byte]$dim)                       # width (0 = 256)
    $bw.Write([byte]$dim)                       # height (0 = 256)
    $bw.Write([byte]0)                          # colorCount
    $bw.Write([byte]0)                          # reserved
    $bw.Write([uint16]1)                        # planes
    $bw.Write([uint16]32)                       # bitCount
    $bw.Write([uint32]$pngs[$s].Length)         # bytesInRes
    $bw.Write([uint32]$offset)                  # imageOffset
    $offset += $pngs[$s].Length
}
foreach ($s in $sizes) {
    $bw.Write([byte[]]$pngs[$s])
}
$bw.Flush()
[System.IO.File]::WriteAllBytes($icoPath, $ico.ToArray())
$bw.Close()

Write-Output "Wrote $icoPath"
Write-Output "Wrote $pngPath"
