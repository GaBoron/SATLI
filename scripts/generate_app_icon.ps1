$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$ProjectRoot = Split-Path -Parent $PSScriptRoot
$AssetRoot = Join-Path $ProjectRoot "src\Satli.Gui\Assets"
$SourcePath = Join-Path $AssetRoot "AppIcon.source.png"

if (-not (Test-Path -LiteralPath $SourcePath)) {
    throw "Application icon source image was not found: $SourcePath"
}

function New-AppIconBitmap([int] $Size) {
    $source = [System.Drawing.Bitmap]::FromFile($SourcePath)
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
Copy-Item -LiteralPath $SourcePath -Destination (Join-Path $AssetRoot "AppIcon.preview.png") -Force
Save-AppIconIco (Join-Path $AssetRoot "AppIcon.ico")

Write-Host "Generated SATLI application icon from $SourcePath"
