$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$ProjectRoot = Split-Path -Parent $PSScriptRoot
$AssetRoot = Join-Path $ProjectRoot "src\Satl.Gui\Assets"
$SourcePath = Join-Path $AssetRoot "AppIcon.source.png"

if (-not (Test-Path -LiteralPath $SourcePath)) {
    throw "Application icon source image was not found: $SourcePath"
}

function New-RoundedRectanglePath(
    [single] $X,
    [single] $Y,
    [single] $Width,
    [single] $Height,
    [single] $Radius
) {
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $diameter = $Radius * 2
    $path.AddArc($X, $Y, $diameter, $diameter, 180, 90)
    $path.AddArc($X + $Width - $diameter, $Y, $diameter, $diameter, 270, 90)
    $path.AddArc($X + $Width - $diameter, $Y + $Height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($X, $Y + $Height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-AppIconBitmap([int] $Size) {
    $source = [System.Drawing.Bitmap]::FromFile($SourcePath)
    try {
        if ($source.Width -ne $source.Height) {
            throw "Application icon source image must be square"
        }

        # Keep the supplied artwork unchanged and apply the platform-ready
        # transparent corner mask only while exporting icon assets.
        $roundedSource = [System.Drawing.Bitmap]::new(
            $source.Width,
            $source.Height,
            [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $roundedGraphics = [System.Drawing.Graphics]::FromImage($roundedSource)
        $cornerPath = New-RoundedRectanglePath `
            0 `
            0 `
            $source.Width `
            $source.Height `
            ([single]($source.Width * 0.125))
        try {
            $roundedGraphics.Clear([System.Drawing.Color]::Transparent)
            $roundedGraphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
            $roundedGraphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
            $roundedGraphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $roundedGraphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $roundedGraphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
            $roundedGraphics.SetClip($cornerPath)
            $roundedGraphics.DrawImageUnscaled($source, 0, 0)
            $roundedGraphics.ResetClip()
        }
        finally {
            $cornerPath.Dispose()
            $roundedGraphics.Dispose()
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
                $roundedSource,
                [System.Drawing.Rectangle]::new(0, 0, $Size, $Size),
                0,
                0,
                $roundedSource.Width,
                $roundedSource.Height,
                [System.Drawing.GraphicsUnit]::Pixel)
        }
        finally {
            $graphics.Dispose()
            $roundedSource.Dispose()
        }

        # Bicubic downscaling can leave a faint fractional-alpha value in the
        # extreme corner pixels at 16px and 24px. Keep every exported frame's
        # four outer corners fully transparent.
        $transparent = [System.Drawing.Color]::Transparent
        $bitmap.SetPixel(0, 0, $transparent)
        $bitmap.SetPixel($Size - 1, 0, $transparent)
        $bitmap.SetPixel(0, $Size - 1, $transparent)
        $bitmap.SetPixel($Size - 1, $Size - 1, $transparent)
        return $bitmap
    }
    finally {
        $source.Dispose()
    }
}

function Save-AppIconPng([string] $Path, [int] $Size) {
    $bitmap = New-AppIconBitmap $Size
    try {
        $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $bitmap.Dispose()
    }
}

function Save-AppIconIco([string] $Path) {
    $images = @()
    foreach ($size in @(16, 24, 32, 48, 64, 128, 256)) {
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
Save-AppIconPng (Join-Path $AssetRoot "AppIcon.preview.png") 512
Save-AppIconIco (Join-Path $AssetRoot "AppIcon.ico")

Write-Host "Generated SATL application icon from $SourcePath"
