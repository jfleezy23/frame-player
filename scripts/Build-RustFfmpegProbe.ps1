param(
    [string]$RuntimeIdentifier = "win-x64"
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$crateDir = Join-Path $repoRoot "native\frameplayer_ffmpeg_probe"
$destDir = Join-Path $repoRoot "Runtime\rust\$RuntimeIdentifier"

if ($RuntimeIdentifier -ne "win-x64") {
    throw "Unsupported Rust FFmpeg probe runtime identifier for this script: $RuntimeIdentifier"
}

if (-not [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
        [System.Runtime.InteropServices.OSPlatform]::Windows)) {
    throw "Building the win-x64 Rust FFmpeg probe must run on a Windows host."
}

$cargoCommand = Get-Command cargo -ErrorAction SilentlyContinue
$cargoPath = if ($null -ne $cargoCommand) { $cargoCommand.Source } else { $null }
if ([string]::IsNullOrWhiteSpace($cargoPath)) {
    $rustupCargoPath = Join-Path $env:USERPROFILE ".cargo\bin\cargo.exe"
    if (Test-Path -LiteralPath $rustupCargoPath) {
        $cargoPath = $rustupCargoPath
    }
}

if ([string]::IsNullOrWhiteSpace($cargoPath)) {
    throw "Cargo was not found on PATH or at the default rustup location. Install Rust with rustup and rerun this script."
}

& $cargoPath build --manifest-path (Join-Path $crateDir "Cargo.toml") --release

New-Item -ItemType Directory -Force -Path $destDir | Out-Null
$sourceLib = Join-Path $crateDir "target\release\frameplayer_ffmpeg_probe.dll"
$destLib = Join-Path $destDir "frameplayer_ffmpeg_probe.dll"
Copy-Item -Force $sourceLib $destLib

Write-Host "Built Rust FFmpeg probe: $destLib"
