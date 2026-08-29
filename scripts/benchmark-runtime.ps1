[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $CliPath,
    [ValidateRange(1, 50)]
    [int] $Iterations = 5
)

$ErrorActionPreference = "Stop"
$Executable = [IO.Path]::GetFullPath($CliPath)
if (-not (Test-Path -LiteralPath $Executable -PathType Leaf)) {
    throw "C# SATLI command line was not found: $Executable"
}

$WorkRoot = Join-Path $env:TEMP ("satli-csharp-benchmark-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $WorkRoot | Out-Null

function Measure-Command([scriptblock] $Action) {
    $Watch = [Diagnostics.Stopwatch]::StartNew()
    & $Action
    if ($LASTEXITCODE -ne 0) {
        throw "Benchmark command failed with exit code $LASTEXITCODE"
    }
    $Watch.Stop()
    return [math]::Round($Watch.Elapsed.TotalMilliseconds, 2)
}

try {
    $Startup = @(
        for ($Index = 0; $Index -lt $Iterations; $Index++) {
            Measure-Command { & $Executable --version | Out-Null }
        }
    )
    $Refresh = Measure-Command {
        & $Executable cache refresh --data-dir $WorkRoot --jsonl | Out-Null
    }
    $Scan = Measure-Command {
        & $Executable scan --scope cloud --offline --data-dir $WorkRoot --jsonl |
            Out-Null
    }
    $Revisions = Measure-Command {
        & $Executable schema revisions verify --data-dir $WorkRoot --jsonl |
            Out-Null
    }
    $ExecutableInfo = Get-Item -LiteralPath $Executable
    [pscustomobject] @{
        runtime = "self-contained-csharp"
        executable = $Executable
        executable_bytes = $ExecutableInfo.Length
        startup_ms = $Startup
        startup_median_ms = ($Startup | Sort-Object)[[math]::Floor($Startup.Count / 2)]
        catalog_refresh_ms = $Refresh
        offline_cloud_scan_ms = $Scan
        revision_verify_ms = $Revisions
    } | ConvertTo-Json -Depth 4
}
finally {
    if (Test-Path -LiteralPath $WorkRoot) {
        Add-Type -AssemblyName Microsoft.VisualBasic
        [Microsoft.VisualBasic.FileIO.FileSystem]::DeleteDirectory(
            [IO.Path]::GetFullPath($WorkRoot),
            [Microsoft.VisualBasic.FileIO.UIOption]::OnlyErrorDialogs,
            [Microsoft.VisualBasic.FileIO.RecycleOption]::SendToRecycleBin,
            [Microsoft.VisualBasic.FileIO.UICancelOption]::ThrowException)
    }
}
