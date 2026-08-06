param(
    [string]$OutputIcon = (Join-Path $PSScriptRoot '..\assets\Markgig.ico'),
    [string]$OutputPreview = (Join-Path $PSScriptRoot '..\assets\Markgig-preview.png')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-RoundedRectanglePath {
    param([System.Drawing.RectangleF]$Rectangle, [float]$Radius)
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $diameter = $Radius * 2
    $arc = [System.Drawing.RectangleF]::new($Rectangle.X, $Rectangle.Y, $diameter, $diameter)
    $path.AddArc($arc, 180, 90)
    $arc.X = $Rectangle.Right - $diameter
    $path.AddArc($arc, 270, 90)
    $arc.Y = $Rectangle.Bottom - $diameter
    $path.AddArc($arc, 0, 90)
    $arc.X = $Rectangle.X
    $path.AddArc($arc, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-MarkgigBitmap {
    param([int]$Size)

    $bitmap = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $bitmap.SetResolution(96, 96)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.Clear([System.Drawing.Color]::Transparent)

        $tile = [System.Drawing.RectangleF]::new($Size * 0.03125, $Size * 0.03125, $Size * 0.9375, $Size * 0.9375)
        $tilePath = New-RoundedRectanglePath -Rectangle $tile -Radius ($Size * 0.203125)
        $tileBrush = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#1c1e1b'))
        try { $graphics.FillPath($tileBrush, $tilePath) } finally { $tileBrush.Dispose(); $tilePath.Dispose() }

        $paperBrush = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#f6f1e6'))
        $foldBrush = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#d9d1c2'))
        try {
            [System.Drawing.PointF[]]$paper = @(
                [System.Drawing.PointF]::new($Size * 0.2695, $Size * 0.1680),
                [System.Drawing.PointF]::new($Size * 0.6250, $Size * 0.1680),
                [System.Drawing.PointF]::new($Size * 0.7969, $Size * 0.3398),
                [System.Drawing.PointF]::new($Size * 0.7969, $Size * 0.8320),
                [System.Drawing.PointF]::new($Size * 0.2695, $Size * 0.8320)
            )
            $graphics.FillPolygon($paperBrush, $paper)
            [System.Drawing.PointF[]]$fold = @(
                [System.Drawing.PointF]::new($Size * 0.6250, $Size * 0.1680),
                [System.Drawing.PointF]::new($Size * 0.6250, $Size * 0.3398),
                [System.Drawing.PointF]::new($Size * 0.7969, $Size * 0.3398)
            )
            $graphics.FillPolygon($foldBrush, $fold)
        }
        finally {
            $paperBrush.Dispose()
            $foldBrush.Dispose()
        }

        $markPen = [System.Drawing.Pen]::new([System.Drawing.ColorTranslator]::FromHtml('#daa641'), [Math]::Max(1.5, $Size * 0.0664))
        try {
            $markPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
            $markPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
            $markPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
            [System.Drawing.PointF[]]$mark = @(
                [System.Drawing.PointF]::new($Size * 0.3828, $Size * 0.6406),
                [System.Drawing.PointF]::new($Size * 0.3828, $Size * 0.4297),
                [System.Drawing.PointF]::new($Size * 0.5000, $Size * 0.5547),
                [System.Drawing.PointF]::new($Size * 0.6172, $Size * 0.4297),
                [System.Drawing.PointF]::new($Size * 0.6172, $Size * 0.6406)
            )
            $graphics.DrawLines($markPen, $mark)
        }
        finally { $markPen.Dispose() }
    }
    finally { $graphics.Dispose() }
    return $bitmap
}

$iconPath = [System.IO.Path]::GetFullPath($OutputIcon)
$previewPath = [System.IO.Path]::GetFullPath($OutputPreview)
$iconDirectory = [System.IO.Path]::GetDirectoryName($iconPath)
if (-not (Test-Path -LiteralPath $iconDirectory -PathType Container)) {
    throw "Icon output directory does not exist: $iconDirectory"
}

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$images = [System.Collections.Generic.List[object]]::new()
try {
    foreach ($size in $sizes) {
        $bitmap = New-MarkgigBitmap -Size $size
        try {
            $stream = [System.IO.MemoryStream]::new()
            $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            $images.Add([pscustomobject]@{ Size = $size; Bytes = $stream.ToArray() })
            $stream.Dispose()
            if ($size -eq 256) {
                $bitmap.Save($previewPath, [System.Drawing.Imaging.ImageFormat]::Png)
            }
        }
        finally { $bitmap.Dispose() }
    }

    $file = [System.IO.File]::Open($iconPath, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write)
    $writer = [System.IO.BinaryWriter]::new($file)
    try {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$images.Count)
        $offset = 6 + (16 * $images.Count)
        foreach ($image in $images) {
            $dimension = if ($image.Size -eq 256) { [byte]0 } else { [byte]$image.Size }
            $writer.Write($dimension)
            $writer.Write($dimension)
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$image.Bytes.Length)
            $writer.Write([uint32]$offset)
            $offset += $image.Bytes.Length
        }
        foreach ($image in $images) { $writer.Write($image.Bytes) }
    }
    finally {
        $writer.Dispose()
        $file.Dispose()
    }
}
finally {
    $images.Clear()
}

Write-Host "Created: $iconPath"
Write-Host "Preview: $previewPath"
