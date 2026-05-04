param(
    [string]$Version = "unified-preview-0.2.0",
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$OutputRoot = "artifacts\$Version"
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$project = Join-Path $repoRoot "src\FramePlayer.Avalonia\FramePlayer.Avalonia.csproj"
$publishDir = Join-Path $repoRoot "$OutputRoot\windows-publish"
$zipPath = Join-Path $repoRoot "$OutputRoot\FramePlayer-Windows-x64-$Version.zip"
$shaPath = "$zipPath.sha256"

New-Item -ItemType Directory -Force -Path (Split-Path $publishDir) | Out-Null
if (Test-Path $publishDir) {
    Remove-Item -Recurse -Force $publishDir
}

dotnet publish $project `
    -c $Configuration `
    -r $RuntimeIdentifier `
    --self-contained true `
    -p:PublishSingleFile=false `
    -o $publishDir

$requiredFiles = @(
    "FramePlayer.Avalonia.exe",
    "avcodec-62.dll",
    "avformat-62.dll",
    "avutil-60.dll",
    "swresample-6.dll",
    "swscale-9.dll",
    "ffmpeg-export\avfilter-11.dll",
    "Runtime\runtime-manifest.json",
    "Runtime\export-runtime-manifest.json",
    "LICENSE",
    "THIRD_PARTY_NOTICES.md"
)

foreach ($relativePath in $requiredFiles) {
    $fullPath = Join-Path $publishDir $relativePath
    if (-not (Test-Path $fullPath)) {
        throw "Expected packaged file missing: $relativePath"
    }
}

if (Test-Path $zipPath) {
    Remove-Item -Force $zipPath
}

Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -Force
$hash = Get-FileHash -Algorithm SHA256 $zipPath
"$($hash.Hash.ToLowerInvariant())  $(Split-Path -Leaf $zipPath)" | Set-Content -NoNewline -Encoding ascii $shaPath

Write-Host "Packaged unified Windows preview:"
Write-Host $zipPath
Get-Content $shaPath
