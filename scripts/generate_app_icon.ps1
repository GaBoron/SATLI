$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName PresentationCore, WindowsBase

$ProjectRoot = Split-Path -Parent $PSScriptRoot
$AssetRoot = Join-Path $ProjectRoot "src\Satli.Gui\Assets"
$VectorPath = Join-Path $AssetRoot "AppIcon.source.svg"
$SourcePath = Join-Path $AssetRoot "AppIcon.source.png"
$SmallVectorPath = Join-Path $AssetRoot "AppIcon.small.source.svg"
$SmallSourcePath = Join-Path $AssetRoot "AppIcon.small.source.png"

if (-not (Test-Path -LiteralPath $VectorPath)) {
    throw "Application icon vector source was not found: $VectorPath"
}

function Save-AppIconSource([string] $Path, [string] $DestinationPath, [int] $Size) {
    # The master uses only flat paths and circles. WPF renders these SVG primitives
    # without a browser, external graphics tool, or machine-dependent font glyphs.
    [xml] $document = Get-Content -LiteralPath $Path -Raw -Encoding UTF8
    $culture = [Globalization.CultureInfo]::InvariantCulture
    $viewBox = @($document.DocumentElement.GetAttribute("viewBox") -split '\s+' |
        ForEach-Object { [double]::Parse($_, $culture) })
    if ($viewBox.Count -ne 4 -or $viewBox[2] -le 0 -or $viewBox[2] -ne $viewBox[3]) {
        throw "Application icon viewBox must be square"
    }

    $visual = [System.Windows.Media.DrawingVisual]::new()
    $context = $visual.RenderOpen()
    try {
        $scale = [double]$Size / $viewBox[2]
        $matrix = [System.Windows.Media.Matrix]::new(
            $scale, 0, 0, $scale, -$viewBox[0] * $scale, -$viewBox[1] * $scale)
        $context.PushTransform([System.Windows.Media.MatrixTransform]::new($matrix))
        foreach ($element in $document.DocumentElement.ChildNodes) {
            if ($element.NodeType -ne [Xml.XmlNodeType]::Element -or
                $element.LocalName -in @("title", "desc")) {
                continue
            }
            $geometry = switch ($element.LocalName) {
                "path" {
                    $rule = if ($element.GetAttribute("fill-rule") -eq "evenodd") { "F0 " } else { "F1 " }
                    [System.Windows.Media.Geometry]::Parse($rule + $element.GetAttribute("d"))
                }
                "circle" {
                    $center = [System.Windows.Point]::new(
                        [double]::Parse($element.GetAttribute("cx"), $culture),
                        [double]::Parse($element.GetAttribute("cy"), $culture))
                    $radius = [double]::Parse($element.GetAttribute("r"), $culture)
                    [System.Windows.Media.EllipseGeometry]::new($center, $radius, $radius)
                }
                default { throw "Unsupported application icon primitive: $($element.LocalName)" }
            }
            $fill = $null
            if ($element.GetAttribute("fill") -ne "none") {
                $fill = [System.Windows.Media.BrushConverter]::new().ConvertFromInvariantString(
                    $element.GetAttribute("fill"))
            }
            $pen = $null
            if ($element.HasAttribute("stroke")) {
                $brush = [System.Windows.Media.BrushConverter]::new().ConvertFromInvariantString(
                    $element.GetAttribute("stroke"))
                $pen = [System.Windows.Media.Pen]::new(
                    $brush, [double]::Parse($element.GetAttribute("stroke-width"), $culture))
                $pen.StartLineCap = [System.Windows.Media.PenLineCap]::Round
                $pen.EndLineCap = [System.Windows.Media.PenLineCap]::Round
                $pen.LineJoin = [System.Windows.Media.PenLineJoin]::Round
            }
            $context.DrawGeometry($fill, $pen, $geometry)
        }
        $context.Pop()
    }
    finally {
        $context.Close()
    }
    $bitmap = [System.Windows.Media.Imaging.RenderTargetBitmap]::new(
        $Size, $Size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = [System.Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [IO.File]::Create($DestinationPath)
    try {
        $encoder.Save($stream)
    }
    finally {
        $stream.Dispose()
    }
}

function New-AppIconBitmap([int] $Size) {
    # At 16–24 px, a single larger Han glyph and thicker geometry remain readable.
    $rasterPath = if ($Size -le 24) { $SmallSourcePath } else { $SourcePath }
    $source = [System.Drawing.Bitmap]::FromFile($rasterPath)
    try {
        if ($source.Width -ne $source.Height) {
            throw "Application icon source image must be square"
        }

        $bitmap = [System.Drawing.Bitmap]::new(
            $Size,
            $Size,
            [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.Clear([System.Drawing.Color]::Transparent)
            $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
            $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
            $graphics.DrawImage(
                $source,
                [System.Drawing.Rectangle]::new(0, 0, $Size, $Size),
                0,
                0,
                $source.Width,
                $source.Height,
                [System.Drawing.GraphicsUnit]::Pixel)
        }
        finally {
            $graphics.Dispose()
        }
        return $bitmap
    }
    finally {
        $source.Dispose()
    }
}

function Save-AppIconIco([string] $Path) {
    $images = @()
    foreach ($size in @(16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 128, 256)) {
        $bitmap = New-AppIconBitmap $size
        $stream = [System.IO.MemoryStream]::new()
        try {
            $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            $images += [pscustomobject]@{ Size = $size; Bytes = $stream.ToArray() }
        }
        finally {
            $stream.Dispose()
            $bitmap.Dispose()
        }
    }

    $file = [System.IO.File]::Create($Path)
    $writer = [System.IO.BinaryWriter]::new($file)
    try {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$images.Count)
        $offset = 6 + (16 * $images.Count)
        foreach ($image in $images) {
            $dimension = if ($image.Size -eq 256) { 0 } else { $image.Size }
            $writer.Write([byte]$dimension)
            $writer.Write([byte]$dimension)
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$image.Bytes.Length)
            $writer.Write([uint32]$offset)
            $offset += $image.Bytes.Length
        }
        foreach ($image in $images) {
            $writer.Write([byte[]]$image.Bytes)
        }
    }
    finally {
        $writer.Dispose()
        $file.Dispose()
    }
}

New-Item -ItemType Directory -Path $AssetRoot -Force | Out-Null
Save-AppIconSource $VectorPath $SourcePath 1024
Save-AppIconSource $SmallVectorPath $SmallSourcePath 128
$preview = New-AppIconBitmap 256
try {
    $preview.Save((Join-Path $AssetRoot "AppIcon.preview.png"), [System.Drawing.Imaging.ImageFormat]::Png)
}
finally {
    $preview.Dispose()
}
Save-AppIconIco (Join-Path $AssetRoot "AppIcon.ico")

Write-Host "Generated SATLI PNG and 15-size ICO assets from $VectorPath"
