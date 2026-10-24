# Gera o ícone da Bergamota (bergamota cortada: casca, gomos e folha) em vários tamanhos.
# Saída: src/Bergamota/Assets/bergamota.ico (PNG dentro do ICO) e bergamota.png (256 px).
# Uso: powershell -ExecutionPolicy Bypass -File tools/make-icon.ps1
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = "Stop"
$out = Join-Path $PSScriptRoot "..\src\Bergamota\Assets"
New-Item -ItemType Directory -Force $out | Out-Null

function C([string]$hex) { [System.Drawing.ColorTranslator]::FromHtml($hex) }
function P([double]$cx, [double]$cy, [double]$r, [double]$deg) {
    $a = $deg * [Math]::PI / 180
    New-Object System.Drawing.PointF ([float]($cx + $r * [Math]::Cos($a))), ([float]($cy + $r * [Math]::Sin($a)))
}

function Render([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = "AntiAlias"; $g.PixelOffsetMode = "HighQuality"; $g.CompositingQuality = "HighQuality"
    $g.ScaleTransform($size / 256.0, $size / 256.0)
    $small = $size -le 24
    $cx = 122; $cy = 142; $R = 104

    # casca
    $peel = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 20, 40), (New-Object System.Drawing.PointF 230, 250), (C "#DDEC74"), (C "#93A92F")
    $g.FillEllipse($peel, $cx - $R, $cy - $R, 2 * $R, 2 * $R)
    # parte branca
    $pithR = if ($small) { 86 } else { 90 }
    $g.FillEllipse((New-Object System.Drawing.SolidBrush (C "#F4F7DA")), $cx - $pithR, $cy - $pithR, 2 * $pithR, 2 * $pithR)

    # gomos (como as fatias da roda)
    $segOuter = $pithR - 9; $segInner = 12; $gap = if ($small) { 18 } else { 7 }
    $seg = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 40, 60), (New-Object System.Drawing.PointF 210, 230), (C "#E9F28F"), (C "#C2D652")
    for ($i = 0; $i -lt 8; $i++) {
        $a0 = -90 + $i * 45 - 22.5; $a1 = $a0 + 45
        $go = ($gap / 2) / $segOuter * 180 / [Math]::PI
        $gi = [Math]::Min(20, ($gap / 2) / $segInner * 180 / [Math]::PI)
        $pts = New-Object System.Collections.Generic.List[System.Drawing.PointF]
        for ($t = 0; $t -le 12; $t++) { $pts.Add((P $cx $cy $segOuter ($a0 + $go + ($a1 - $a0 - 2 * $go) * $t / 12))) }
        for ($t = 12; $t -ge 0; $t--) { $pts.Add((P $cx $cy $segInner ($a0 + $gi + ($a1 - $a0 - 2 * $gi) * $t / 12))) }
        $g.FillPolygon($seg, $pts.ToArray())
    }
    $g.FillEllipse((New-Object System.Drawing.SolidBrush (C "#F4F7DA")), $cx - 7, $cy - 7, 14, 14)

    # contorno sutil da casca (dá definição em fundo claro)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(90, 60, 80, 20)), ([float]($(if ($small) { 8 } else { 3 })))
    $g.DrawEllipse($pen, $cx - $R, $cy - $R, 2 * $R, 2 * $R)

    # folha
    $leaf = New-Object System.Drawing.Drawing2D.GraphicsPath
    $leaf.AddBezier(158, 54, 168, 18, 206, 6, 244, 12)
    $leaf.AddBezier(244, 12, 232, 50, 196, 68, 158, 54)
    $leafBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 160, 60), (New-Object System.Drawing.PointF 240, 10), (C "#5E8F3A"), (C "#93C65A")
    $g.FillPath($leafBrush, $leaf)
    if (-not $small) {
        $rib = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(120, 40, 70, 25)), 3
        $rib.StartCap = "Round"; $rib.EndCap = "Round"
        $g.DrawBezier($rib, 164, 52, 186, 38, 210, 26, 236, 16)
    }
    $g.Dispose()
    return $bmp
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 96, 128, 256
$frames = @()
foreach ($s in $sizes) {
    $bmp = Render $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    if ($s -eq 256) { $bmp.Save((Join-Path $out "bergamota.png"), [System.Drawing.Imaging.ImageFormat]::Png) }
    $bmp.Dispose()
    $frames += , @($s, $ms.ToArray())
}

# ICO: cabeçalho + entradas de 16 bytes + PNGs
$fs = [System.IO.File]::Create((Join-Path $out "bergamota.ico"))
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
    $s = $f[0]; $data = $f[1]
    $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s }))); $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $w.Write([byte]0); $w.Write([byte]0); $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$data.Length); $w.Write([uint32]$offset)
    $offset += $data.Length
}
foreach ($f in $frames) { $w.Write([byte[]]$f[1]) }
$w.Close()
Write-Output "ok: $out\bergamota.ico ($($frames.Count) tamanhos)"
