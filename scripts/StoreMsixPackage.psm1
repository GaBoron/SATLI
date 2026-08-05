Set-StrictMode -Version Latest

function ConvertTo-SatlMsixVersion {
    param([Parameter(Mandatory)][string] $Version)

    if ($Version -notmatch '^\d+\.\d+\.\d+$') {
        throw "The public version must use MAJOR.MINOR.PATCH format: $Version"
    }
    $parsed = [Version]::Parse($Version)
    foreach ($component in @($parsed.Major, $parsed.Minor, $parsed.Build)) {
        if ($component -gt 65535) {
            throw "MSIX version components must not exceed 65535: $Version"
        }
    }
    return "$($parsed.Major).$($parsed.Minor).$($parsed.Build).0"
}

function Assert-SatlPackageIdentity {
    param(
        [Parameter(Mandatory)][string] $Name,
        [Parameter(Mandatory)][string] $Publisher,
        [Parameter(Mandatory)][string] $PublisherDisplayName
    )

    if ($Name -notmatch '^[A-Za-z0-9.-]{3,50}$') {
        throw "Package identity name must be 3-50 characters using letters, digits, periods, or hyphens."
    }
    if ($Publisher -notmatch '^CN=.+') {
        throw "Package publisher must be the Partner Center subject, normally beginning with CN=."
    }
    if ([string]::IsNullOrWhiteSpace($PublisherDisplayName)) {
        throw "Publisher display name must not be empty."
    }
}

function Get-SatlWindowsSdkTool {
    param([Parameter(Mandatory)][string] $Name)

    $command = Get-Command $Name -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $kitsRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $candidate = Get-ChildItem -LiteralPath $kitsRoot -Directory -ErrorAction SilentlyContinue |
        Sort-Object { try { [Version]$_.Name } catch { [Version]'0.0.0.0' } } -Descending |
        ForEach-Object { Join-Path $_.FullName "x64\$Name" } |
        Where-Object { Test-Path -LiteralPath $_ } |
        Select-Object -First 1
    if (-not $candidate) {
        throw "$Name was not found. Install the Windows SDK packaging tools."
    }
    return $candidate
}

function New-SatlStoreAsset {
    param(
        [Parameter(Mandatory)][string] $SourcePath,
        [Parameter(Mandatory)][string] $DestinationPath,
        [Parameter(Mandatory)][int] $Size
    )

    Add-Type -AssemblyName System.Drawing
    $source = [System.Drawing.Bitmap]::FromFile($SourcePath)
    try {
        if ($source.Width -ne $source.Height) {
            throw "Store asset source must be square: $SourcePath"
        }
        $bitmap = [System.Drawing.Bitmap]::new(
            $Size,
            $Size,
            [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
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
            $bitmap.Save($DestinationPath, [System.Drawing.Imaging.ImageFormat]::Png)
        }
        finally {
            $bitmap.Dispose()
        }
    }
    finally {
        $source.Dispose()
    }
}

function New-SatlStoreMsix {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $PayloadRoot,
        [Parameter(Mandatory)][string] $OutputPath,
        [Parameter(Mandatory)][string] $ManifestTemplatePath,
        [Parameter(Mandatory)][string] $AssetSourcePath,
        [Parameter(Mandatory)][string] $Version,
        [Parameter(Mandatory)][string] $PackageIdentityName,
        [Parameter(Mandatory)][string] $PackagePublisher,
        [Parameter(Mandatory)][string] $PublisherDisplayName
    )

    Assert-SatlPackageIdentity $PackageIdentityName $PackagePublisher $PublisherDisplayName
    $packageVersion = ConvertTo-SatlMsixVersion $Version
    $manifestPath = Join-Path $PayloadRoot 'AppxManifest.xml'
    $assetRoot = Join-Path $PayloadRoot 'Assets'
    New-Item -ItemType Directory -Path $assetRoot -Force | Out-Null

    $manifest = Get-Content -LiteralPath $ManifestTemplatePath -Raw -Encoding UTF8
    $manifest = $manifest.Replace(
        '{{PACKAGE_IDENTITY_NAME}}',
        [System.Security.SecurityElement]::Escape($PackageIdentityName))
    $manifest = $manifest.Replace(
        '{{PACKAGE_PUBLISHER}}',
        [System.Security.SecurityElement]::Escape($PackagePublisher))
    $manifest = $manifest.Replace(
        '{{PUBLISHER_DISPLAY_NAME}}',
        [System.Security.SecurityElement]::Escape($PublisherDisplayName))
    $manifest = $manifest.Replace('{{PACKAGE_VERSION}}', $packageVersion)
    if ($manifest -match '\{\{[^}]+\}\}') {
        throw "The Store manifest still contains unresolved template values."
    }
    Set-Content -LiteralPath $manifestPath -Value $manifest -Encoding UTF8

    New-SatlStoreAsset $AssetSourcePath (Join-Path $assetRoot 'StoreLogo.png') 50
    New-SatlStoreAsset $AssetSourcePath (Join-Path $assetRoot 'Square44x44Logo.png') 44
    New-SatlStoreAsset $AssetSourcePath (Join-Path $assetRoot 'Square150x150Logo.png') 150

    $makeAppx = Get-SatlWindowsSdkTool 'makeappx.exe'
    if (Test-Path -LiteralPath $OutputPath) {
        Remove-Item -LiteralPath $OutputPath -Force
    }
    & $makeAppx pack /d $PayloadRoot /p $OutputPath /o | Write-Host
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $OutputPath)) {
        throw "MakeAppx did not produce the Store package: $OutputPath"
    }
    return $OutputPath
}

Export-ModuleMember -Function ConvertTo-SatlMsixVersion, New-SatlStoreMsix
