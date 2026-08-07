[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $OutputDirectory
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$SourceRoot = Join-Path $ProjectRoot "src\satli"
$BuildRoot = Join-Path $ProjectRoot "build"
$DownloadRoot = Join-Path $BuildRoot "downloads"
$RuntimeRoot = Join-Path $OutputDirectory "_runtime"
$RuntimeMarker = Join-Path $RuntimeRoot ".satli-runtime"
$EmbeddedPythonVersion = "3.13.13"
$EmbeddedPythonArchiveName = "python-$EmbeddedPythonVersion-embed-amd64.zip"
$EmbeddedPythonArchive = Join-Path $DownloadRoot $EmbeddedPythonArchiveName
$EmbeddedPythonPartial = "$EmbeddedPythonArchive.part"
$EmbeddedPythonUrl = "https://www.python.org/ftp/python/$EmbeddedPythonVersion/$EmbeddedPythonArchiveName"
$EmbeddedPythonSha256 = "8766a8775746235e23cf5aee5027ab1060bb981d93110577adcf3508aa0cbd55"
$DulwichVersion = "1.2.12"
$Urllib3Version = "2.7.0"
$VenvPython = Join-Path $ProjectRoot ".venv\Scripts\python.exe"
$Python = if (Test-Path -LiteralPath $VenvPython) { $VenvPython } else { "python" }

function Assert-WithinProject([string] $Path) {
    $ResolvedProject = [System.IO.Path]::GetFullPath($ProjectRoot).TrimEnd('\') + '\'
    $ResolvedTarget = [System.IO.Path]::GetFullPath($Path)
    if (-not $ResolvedTarget.StartsWith($ResolvedProject, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify path outside project: $ResolvedTarget"
    }
}

function Get-BytesHash([byte[]] $Bytes) {
    $Sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        return ([System.BitConverter]::ToString($Sha256.ComputeHash($Bytes))).Replace("-", "").ToLowerInvariant()
    }
    finally {
        $Sha256.Dispose()
    }
}

function Get-FileSha256([string] $Path) {
    $Sha256 = [System.Security.Cryptography.SHA256]::Create()
    $Stream = [System.IO.File]::OpenRead($Path)
    try {
        return ([System.BitConverter]::ToString($Sha256.ComputeHash($Stream))).Replace("-", "").ToLowerInvariant()
    }
    finally {
        $Stream.Dispose()
        $Sha256.Dispose()
    }
}

function Get-RuntimeFingerprint {
    $Parts = [System.Collections.Generic.List[string]]::new()
    $Parts.Add("python=$EmbeddedPythonVersion")
    $Parts.Add("python-sha256=$EmbeddedPythonSha256")
    $Parts.Add("dulwich=$DulwichVersion")
    $Parts.Add("urllib3=$Urllib3Version")
    $FingerprintFiles = @(
        Get-ChildItem -LiteralPath $SourceRoot -File -Recurse
        Get-Item -LiteralPath (Join-Path $ProjectRoot "pyproject.toml")
        Get-Item -LiteralPath (Join-Path $ProjectRoot "src\Satli.Gui\Satli.Gui.csproj")
        Get-Item -LiteralPath $PSCommandPath
    ) | Sort-Object FullName
    foreach ($SourceFile in $FingerprintFiles) {
        $RelativePath = $SourceFile.FullName.Substring($ProjectRoot.Length).TrimStart('\')
        $Parts.Add("$RelativePath=$(Get-FileSha256 $SourceFile.FullName)")
    }
    return (Get-BytesHash ([System.Text.Encoding]::UTF8.GetBytes(($Parts -join "`n"))))
}

Assert-WithinProject $OutputDirectory
Assert-WithinProject $RuntimeRoot
Assert-WithinProject $DownloadRoot

$Fingerprint = Get-RuntimeFingerprint
$PythonExecutable = Join-Path $RuntimeRoot "python.exe"
$ApplicationArchive = Join-Path $RuntimeRoot "satli.pyz"
if ((Test-Path -LiteralPath $PythonExecutable) -and
    (Test-Path -LiteralPath $ApplicationArchive) -and
    (Test-Path -LiteralPath $RuntimeMarker) -and
    ((Get-Content -LiteralPath $RuntimeMarker -Raw -Encoding UTF8).Trim() -eq $Fingerprint)) {
    Write-Host "SATLI local runtime is up to date: $RuntimeRoot"
    exit 0
}

New-Item -ItemType Directory -Path $DownloadRoot -Force | Out-Null
if (Test-Path -LiteralPath $EmbeddedPythonArchive) {
    $CachedHash = Get-FileSha256 $EmbeddedPythonArchive
    if ($CachedHash -ne $EmbeddedPythonSha256) {
        Remove-Item -LiteralPath $EmbeddedPythonArchive -Force
    }
}
if (-not (Test-Path -LiteralPath $EmbeddedPythonArchive)) {
    if (Test-Path -LiteralPath $EmbeddedPythonPartial) {
        Remove-Item -LiteralPath $EmbeddedPythonPartial -Force
    }
    Invoke-WebRequest -Uri $EmbeddedPythonUrl -OutFile $EmbeddedPythonPartial
    $DownloadedHash = Get-FileSha256 $EmbeddedPythonPartial
    if ($DownloadedHash -ne $EmbeddedPythonSha256) {
        Remove-Item -LiteralPath $EmbeddedPythonPartial -Force
        throw "Downloaded embedded Python archive checksum mismatch: $DownloadedHash"
    }
    Move-Item -LiteralPath $EmbeddedPythonPartial -Destination $EmbeddedPythonArchive
}

$StagingRoot = Join-Path $BuildRoot ("local-runtime-" + [guid]::NewGuid().ToString("N"))
$StagedRuntime = Join-Path $StagingRoot "_runtime"
$PayloadRoot = Join-Path $StagingRoot "payload"
$BackupRuntime = "$RuntimeRoot.previous"
Assert-WithinProject $StagingRoot
Assert-WithinProject $BackupRuntime

try {
    New-Item -ItemType Directory -Path $StagedRuntime -Force | Out-Null
    New-Item -ItemType Directory -Path $PayloadRoot -Force | Out-Null
    Expand-Archive -LiteralPath $EmbeddedPythonArchive -DestinationPath $StagedRuntime
    foreach ($UnusedRuntimeFile in @("pythonw.exe", "python.cat")) {
        $UnusedRuntimePath = Join-Path $StagedRuntime $UnusedRuntimeFile
        if (Test-Path -LiteralPath $UnusedRuntimePath) {
            Remove-Item -LiteralPath $UnusedRuntimePath -Force
        }
    }

    Copy-Item -LiteralPath $SourceRoot -Destination $PayloadRoot -Recurse
    Copy-Item -LiteralPath (Join-Path $SourceRoot "__main__.py") -Destination $PayloadRoot
    & $Python -m pip install `
        --disable-pip-version-check `
        --no-compile `
        --no-deps `
        --only-binary=:all: `
        --target $PayloadRoot `
        "dulwich==$DulwichVersion" `
        "urllib3==$Urllib3Version"
    if ($LASTEXITCODE -ne 0) {
        throw "Pinned Python dependencies could not be staged"
    }
    Get-ChildItem -LiteralPath $PayloadRoot -File -Recurse |
        Where-Object { $_.Extension -in @(".pyd", ".dll") } |
        Remove-Item -Force
    Get-ChildItem -LiteralPath $PayloadRoot -Directory -Filter "__pycache__" -Recurse |
        Remove-Item -Recurse -Force
    foreach ($GeneratedEntryPointDirectory in @("bin", "Scripts")) {
        $GeneratedEntryPointPath = Join-Path $PayloadRoot $GeneratedEntryPointDirectory
        if (Test-Path -LiteralPath $GeneratedEntryPointPath) {
            Remove-Item -LiteralPath $GeneratedEntryPointPath -Recurse -Force
        }
    }

    $StagedArchive = Join-Path $StagedRuntime "satli.pyz"
    & $Python -m zipapp $PayloadRoot -o $StagedArchive
    if ($LASTEXITCODE -ne 0) {
        throw "SATLI Python archive build failed"
    }

    $Project = [xml](Get-Content -LiteralPath (Join-Path $ProjectRoot "src\Satli.Gui\Satli.Gui.csproj") -Raw -Encoding UTF8)
    $Version = @($Project.Project.PropertyGroup.Version | Where-Object { $_ })[0]
    $CliVersion = & (Join-Path $StagedRuntime "python.exe") $StagedArchive --version
    if ($LASTEXITCODE -ne 0 -or $CliVersion -ne "satli $Version") {
        throw "Built SATLI Python payload has unexpected version: $CliVersion"
    }
    Set-Content -LiteralPath (Join-Path $StagedRuntime ".satli-runtime") -Value $Fingerprint -Encoding UTF8

    if (Test-Path -LiteralPath $BackupRuntime) {
        Remove-Item -LiteralPath $BackupRuntime -Recurse -Force
    }
    if (Test-Path -LiteralPath $RuntimeRoot) {
        Move-Item -LiteralPath $RuntimeRoot -Destination $BackupRuntime
    }
    try {
        Move-Item -LiteralPath $StagedRuntime -Destination $RuntimeRoot
    }
    catch {
        if ((Test-Path -LiteralPath $BackupRuntime) -and -not (Test-Path -LiteralPath $RuntimeRoot)) {
            Move-Item -LiteralPath $BackupRuntime -Destination $RuntimeRoot
        }
        throw
    }
    if (Test-Path -LiteralPath $BackupRuntime) {
        Remove-Item -LiteralPath $BackupRuntime -Recurse -Force
    }
    Write-Host "Prepared complete SATLI local runtime: $RuntimeRoot"
}
finally {
    if (Test-Path -LiteralPath $StagingRoot) {
        Remove-Item -LiteralPath $StagingRoot -Recurse -Force
    }
}
