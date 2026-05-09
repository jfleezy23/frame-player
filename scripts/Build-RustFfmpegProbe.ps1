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

if (-not $IsWindows) {
    throw "Building the win-x64 Rust FFmpeg probe must run on a Windows host."
}

$cargo = Get-Command cargo -ErrorAction Stop
& $cargo.Source build --manifest-path (Join-Path $crateDir "Cargo.toml") --release

New-Item -ItemType Directory -Force -Path $destDir | Out-Null
$sourceLib = Join-Path $crateDir "target\release\frameplayer_ffmpeg_probe.dll"
$destLib = Join-Path $destDir "frameplayer_ffmpeg_probe.dll"
Copy-Item -Force $sourceLib $destLib

Write-Host "Built Rust FFmpeg probe: $destLib"
