Set-StrictMode -Version Latest

function Find-SignTool {
    $Command = Get-Command "signtool.exe" -ErrorAction SilentlyContinue
    if ($Command) {
        return $Command.Source
    }

    $WindowsKitsRoot = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
    if (-not (Test-Path -LiteralPath $WindowsKitsRoot)) {
        throw "SignTool was not found. Install the Windows SDK signing tools."
    }

    $Candidates = @(
        Get-ChildItem -LiteralPath $WindowsKitsRoot -Filter "signtool.exe" -File -Recurse |
            Where-Object {
                $_.FullName -match '\\x64\\signtool\.exe$' -and
                $_.Directory.Parent.Name -match '^\d+\.\d+\.\d+\.\d+$'
            } |
            Sort-Object { [version] $_.Directory.Parent.Name } -Descending
    )
    if ($Candidates.Count -eq 0) {
        throw "The Windows SDK does not contain an x64 SignTool executable."
    }

    return $Candidates[0].FullName
}

function Find-CodeSigningCertificate {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Thumbprint
    )

    $NormalizedThumbprint = ($Thumbprint -replace "\s", "").ToUpperInvariant()
    if ($NormalizedThumbprint -notmatch "^[0-9A-F]{40}$") {
        throw "The code-signing certificate thumbprint must be a 40-character SHA-1 value."
    }

    foreach ($StoreScope in @("CurrentUser", "LocalMachine")) {
        $StorePath = "Cert:\$StoreScope\My"
        $Certificate = @(
            Get-ChildItem -Path $StorePath -CodeSigningCert -ErrorAction Stop |
                Where-Object { $_.Thumbprint -eq $NormalizedThumbprint }
        ) | Select-Object -First 1
        if (-not $Certificate) {
            continue
        }
        if (-not $Certificate.HasPrivateKey) {
            throw "Code-signing certificate $NormalizedThumbprint does not have an accessible private key."
        }
        if ($Certificate.NotBefore -gt (Get-Date) -or $Certificate.NotAfter -lt (Get-Date)) {
            throw "Code-signing certificate $NormalizedThumbprint is not currently valid."
        }

        return [pscustomobject] @{
            Certificate = $Certificate
            StoreName  = "My"
            StoreScope = $StoreScope
        }
    }

    throw "Code-signing certificate $NormalizedThumbprint was not found in the CurrentUser or LocalMachine personal store."
}

function New-CodeSigningContext {
    param(
        [Parameter(Mandatory = $true)]
        [string] $CertificateThumbprint,

        [Parameter(Mandatory = $true)]
        [string] $TimestampServer,

        [switch] $RequirePublicTrust
    )

    $TimestampUri = $null
    if (-not [uri]::TryCreate($TimestampServer, [System.UriKind]::Absolute, [ref] $TimestampUri) -or
        $TimestampUri.Scheme -notin @("http", "https")) {
        throw "The timestamp server must be an absolute HTTP or HTTPS URL."
    }

    $CertificateStore = Find-CodeSigningCertificate -Thumbprint $CertificateThumbprint
    $Certificate = $CertificateStore.Certificate
    $Chain = [System.Security.Cryptography.X509Certificates.X509Chain]::new()
    try {
        $Chain.ChainPolicy.RevocationMode = [System.Security.Cryptography.X509Certificates.X509RevocationMode]::Online
        $ChainTrusted = $Chain.Build($Certificate)
        $RootCertificate = $Chain.ChainElements[$Chain.ChainElements.Count - 1].Certificate
        $IsSelfSigned = $RootCertificate.Thumbprint -eq $Certificate.Thumbprint
        $ChainStatus = ($Chain.ChainStatus | ForEach-Object {
            "$($_.Status): $($_.StatusInformation.Trim())"
        }) -join "; "
    }
    finally {
        $Chain.Dispose()
    }
    if ($RequirePublicTrust -and -not $ChainTrusted) {
        throw "The code-signing certificate does not build to a trusted Windows root: $ChainStatus"
    }
    if ($RequirePublicTrust -and $IsSelfSigned) {
        throw "A self-signed certificate cannot be used for a publicly trusted release."
    }

    return [pscustomobject] @{
        CertificateThumbprint = $Certificate.Thumbprint
        ChainTrusted          = $ChainTrusted
        IsSelfSigned          = $IsSelfSigned
        Publisher             = $Certificate.Subject
        RootPublisher         = $RootCertificate.Subject
        SignToolPath          = Find-SignTool
        StoreName             = $CertificateStore.StoreName
        StoreScope            = $CertificateStore.StoreScope
        TimestampServer       = $TimestampUri.AbsoluteUri
    }
}

function Get-SignToolArguments {
    param(
        [Parameter(Mandatory = $true)]
        [pscustomobject] $Context,

        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    $Arguments = @(
        "sign",
        "/sha1", $Context.CertificateThumbprint,
        "/s", $Context.StoreName
    )
    if ($Context.StoreScope -eq "LocalMachine") {
        $Arguments += "/sm"
    }
    $Arguments += @(
        "/fd", "SHA256",
        "/tr", $Context.TimestampServer,
        "/td", "SHA256",
        $Path
    )
    return $Arguments
}

function Assert-AuthenticodeSignature {
    param(
        [Parameter(Mandatory = $true)]
        [pscustomobject] $Context,

        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Cannot verify missing signed file: $Path"
    }

    $Signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($Signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
        throw "Authenticode signature verification failed for $Path`: $($Signature.StatusMessage)"
    }
    if (-not $Signature.SignerCertificate -or
        $Signature.SignerCertificate.Thumbprint -ne $Context.CertificateThumbprint) {
        throw "Authenticode signer mismatch for $Path."
    }

    & $Context.SignToolPath verify /pa /all /q $Path
    if ($LASTEXITCODE -ne 0) {
        throw "SignTool verification failed for $Path."
    }
}

function Invoke-AuthenticodeSign {
    param(
        [Parameter(Mandatory = $true)]
        [pscustomobject] $Context,

        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Cannot sign missing file: $Path"
    }

    $Arguments = Get-SignToolArguments -Context $Context -Path $Path
    & $Context.SignToolPath $Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Authenticode signing failed for $Path."
    }
    Assert-AuthenticodeSignature -Context $Context -Path $Path
}

function Get-InnoSignToolCommand {
    param(
        [Parameter(Mandatory = $true)]
        [pscustomobject] $Context
    )

    $StoreScopeArgument = if ($Context.StoreScope -eq "LocalMachine") { " /sm" } else { "" }
    return (
        '$q' + $Context.SignToolPath + '$q sign' +
        " /sha1 $($Context.CertificateThumbprint) /s $($Context.StoreName)$StoreScopeArgument" +
        " /fd SHA256 /tr $($Context.TimestampServer) /td SHA256 " + '$f'
    )
}

Export-ModuleMember -Function @(
    "Assert-AuthenticodeSignature",
    "Get-InnoSignToolCommand",
    "Invoke-AuthenticodeSign",
    "New-CodeSigningContext"
)
