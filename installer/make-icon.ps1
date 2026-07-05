# Genera el icono de la app (img\app.ico) con el tema "Tactical HUD":
# fondo negro-verdoso, hexagono neon verde con glow y una "P" de Paradox.
# Usa System.Drawing (Windows PowerShell 5.1). Empaqueta PNGs en un .ico multi-res.
Add-Type -AssemblyName System.Drawing

$OutDir = Join-Path $PSScriptRoot '..\src\ParadoxLoLCompanion.App\img'
$OutIco = Join-Path $OutDir 'app.ico'
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

function New-IconBitmap([int]$S) {
    $bmp = New-Object System.Drawing.Bitmap($S, $S, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.Clear([System.Drawing.Color]::Transparent)

    # --- Fondo: rounded-rect oscuro con borde ---
    $pad = [Math]::Max(1, [int]($S * 0.055))
    $r   = [Math]::Max(2, [int]($S * 0.18))
    $rect = New-Object System.Drawing.Rectangle($pad, $pad, ($S - 2*$pad), ($S - 2*$pad))
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()

    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect,
        [System.Drawing.Color]::FromArgb(255, 18, 26, 21),
        [System.Drawing.Color]::FromArgb(255, 8, 12, 10),
        [System.Drawing.Drawing2D.LinearGradientMode]::ForwardDiagonal)
    $g.FillPath($bg, $path)
    $borderPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 44, 59, 50), [Math]::Max(1, $S*0.012))
    $g.DrawPath($borderPen, $path)

    # --- Hexagono (flat-top) con glow ---
    $cx = $S / 2.0; $cy = $S / 2.0
    $rad = $S * 0.34
    $pts = @()
    for ($i = 0; $i -lt 6; $i++) {
        $ang = [Math]::PI / 180.0 * (60 * $i)
        $pts += New-Object System.Drawing.PointF(($cx + $rad * [Math]::Cos($ang)), ($cy + $rad * [Math]::Sin($ang)))
    }
    # glow (verde translucido, grueso)
    $glowPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(70, 59, 227, 139), [Math]::Max(2, $S*0.075))
    $glowPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $g.DrawPolygon($glowPen, $pts)
    # trazo brillante
    $hexPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 59, 227, 139), [Math]::Max(1, $S*0.028))
    $hexPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $g.DrawPolygon($hexPen, $pts)

    # --- "P" central ---
    $fontName = 'Bahnschrift'
    try { $font = New-Object System.Drawing.Font($fontName, ($S * 0.44), [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel) }
    catch { $font = New-Object System.Drawing.Font('Segoe UI', ($S * 0.44), [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel) }
    $sf = New-Object System.Drawing.StringFormat
    $sf.Alignment = [System.Drawing.StringAlignment]::Center
    $sf.LineAlignment = [System.Drawing.StringAlignment]::Center
    $layout = New-Object System.Drawing.RectangleF(0, ($S * -0.02), $S, $S)
    $textBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 234, 251, 241))
    $g.DrawString('P', $font, $textBrush, $layout, $sf)

    $g.Dispose()
    return $bmp
}

$sizes = @(256, 128, 64, 48, 32, 16)
$pngs = @()
foreach ($s in $sizes) {
    $bmp = New-IconBitmap $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += ,($ms.ToArray())
    $bmp.Dispose()
}

# --- Empaquetar ICO (payload PNG) ---
$fs = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([UInt16]0)          # reserved
$bw.Write([UInt16]1)          # type = icon
$bw.Write([UInt16]$sizes.Count)
$offset = 6 + (16 * $sizes.Count)
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $data = $pngs[$i]
    $wh = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([Byte]$wh)      # width
    $bw.Write([Byte]$wh)      # height
    $bw.Write([Byte]0)        # colors
    $bw.Write([Byte]0)        # reserved
    $bw.Write([UInt16]1)      # planes
    $bw.Write([UInt16]32)     # bit count
    $bw.Write([UInt32]$data.Length)
    $bw.Write([UInt32]$offset)
    $offset += $data.Length
}
foreach ($data in $pngs) { $bw.Write($data) }
$bw.Flush()
[System.IO.File]::WriteAllBytes($OutIco, $fs.ToArray())
$bw.Dispose(); $fs.Dispose()

Write-Host "Icono generado: $OutIco ($([Math]::Round((Get-Item $OutIco).Length/1KB,1)) KB)"
