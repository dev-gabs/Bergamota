# Imagens do assistente de instalação (Inno Setup), no visual do app: verde escuro com manchas de cor e a bergamota.
# Saída: installer/art/wizard-*.bmp (painel lateral) e small-*.bmp (canto do cabeçalho), em 100% e 200%.
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = "Stop"
$root = Join-Path $PSScriptRoot ".."
$out = Join-Path $root "installer\art"
New-Item -ItemType Directory -Force $out | Out-Null
$icon = [System.Drawing.Image]::FromFile((Join-Path $root "src\Bergamota\Assets\bergamota.png"))

function C([string]$hex, [int]$a = 255) { $c = [System.Drawing.ColorTranslator]::FromHtml($hex); [System.Drawing.Color]::FromArgb($a, $c) }

function Orb($g, [float]$cx, [float]$cy, [float]$r, $color, [int]$alpha) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddEllipse($cx - $r, $cy - $r, 2 * $r, 2 * $r)
    $b = New-Object System.Drawing.Drawing2D.PathGradientBrush $path
    $b.CenterColor = [System.Drawing.Color]::FromArgb($alpha, $color)
    $b.SurroundColors = @([System.Drawing.Color]::FromArgb(0, $color))
    $g.FillPath($b, $path)
}

function Wizard([int]$w, [int]$h, [string]$file) {
    $s = $w / 164.0
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = "AntiAlias"; $g.InterpolationMode = "HighQualityBicubic"; $g.TextRenderingHint = "AntiAliasGridFit"
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.Point 0, 0), (New-Object System.Drawing.Point $w, $h), (C "#16201B"), (C "#0D1310")
    $g.FillRectangle($bg, 0, 0, $w, $h)
    Orb $g ($w * 0.95) 0 ($w * 0.9) (C "#96B450") 110
    Orb $g ($w * 0.05) ($h * 0.62) ($w * 0.7) (C "#E8923A") 60
    Orb $g ($w * 0.4) ($h * 1.02) ($w * 0.9) (C "#468258") 110
    $is = [int](96 * $s)
    $g.DrawImage($icon, [int](($w - $is) / 2), [int]($h * 0.27), $is, $is)
    $font = New-Object System.Drawing.Font "Segoe UI Semibold", ([float](17 * $s)), ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
    $fmt = New-Object System.Drawing.StringFormat; $fmt.Alignment = "Center"
    $g.DrawString("bergamota", $font, (New-Object System.Drawing.SolidBrush (C "#C9DC5A")), (New-Object System.Drawing.RectangleF 0, ([float]($h * 0.27 + $is + 10 * $s)), $w, (40 * $s)), $fmt)
    $small = New-Object System.Drawing.Font "Segoe UI", ([float](9.5 * $s)), ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
    $g.DrawString("a roda dos seus arquivos", $small, (New-Object System.Drawing.SolidBrush (C "#8FA197")), (New-Object System.Drawing.RectangleF 0, ([float]($h * 0.27 + $is + 34 * $s)), $w, (30 * $s)), $fmt)
    $g.Dispose()
    $bmp.Save((Join-Path $out $file), [System.Drawing.Imaging.ImageFormat]::Bmp)
    $bmp.Dispose()
}

function Small([int]$size, [string]$file) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = "AntiAlias"; $g.InterpolationMode = "HighQualityBicubic"
    $g.Clear([System.Drawing.Color]::White) # o cabeçalho do assistente é claro
    $pad = [int]($size * 0.06)
    $g.DrawImage($icon, $pad, $pad, $size - 2 * $pad, $size - 2 * $pad)
    $g.Dispose()
    $bmp.Save((Join-Path $out $file), [System.Drawing.Imaging.ImageFormat]::Bmp)
    $bmp.Dispose()
}

Wizard 164 314 "wizard-100.bmp"
Wizard 328 628 "wizard-200.bmp"
Small 55 "small-100.bmp"
Small 110 "small-200.bmp"
$icon.Dispose()
Write-Output "ok: $out"
