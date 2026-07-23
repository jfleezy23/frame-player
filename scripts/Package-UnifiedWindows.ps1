param(
    [string]$Version = "2.1.0-rc.3",
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$OutputRoot = "artifacts\$Version"
)

$ErrorActionPreference = "Stop"

function Get-AssemblyVersionFromPackageVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PackageVersion
    )

    $versionMatch = [regex]::Match(
        $PackageVersion,
        "(?<!\d)(?<major>\d+)(?:\.(?<minor>\d+))?(?:\.(?<patch>\d+))?(?:\.(?<revision>\d+))?")
    if (-not $versionMatch.Success) {
        throw "Package version '$PackageVersion' must contain a numeric version segment for assembly metadata."
    }

    $major = [int]$versionMatch.Groups["major"].Value
    $minor = if ($versionMatch.Groups["minor"].Success) { [int]$versionMatch.Groups["minor"].Value } else { 0 }
    $patch = if ($versionMatch.Groups["patch"].Success) { [int]$versionMatch.Groups["patch"].Value } else { 0 }
    $revision = if ($versionMatch.Groups["revision"].Success) { [int]$versionMatch.Groups["revision"].Value } else { 0 }

    return "{0}.{1}.{2}.{3}" -f $major, $minor, $patch, $revision
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$project = Join-Path $repoRoot "src\FramePlayer.Avalonia\FramePlayer.Avalonia.csproj"
$publishDir = Join-Path $repoRoot "$OutputRoot\windows-publish"
$zipPath = Join-Path $repoRoot "$OutputRoot\FramePlayer-Windows-x64-$Version.zip"
$shaPath = "$zipPath.sha256"
$assemblyVersion = Get-AssemblyVersionFromPackageVersion -PackageVersion $Version

New-Item -ItemType Directory -Force -Path (Split-Path $publishDir) | Out-Null
if (Test-Path $publishDir) {
    Remove-Item -Recurse -Force $publishDir
}

& (Join-Path $repoRoot "scripts\Build-RustFfmpegProbe.ps1") -RuntimeIdentifier $RuntimeIdentifier

dotnet publish $project `
    -c $Configuration `
    -r $RuntimeIdentifier `
    --self-contained true `
    -p:PublishSingleFile=false `
    "-p:Version=$assemblyVersion" `
    "-p:AssemblyVersion=$assemblyVersion" `
    "-p:FileVersion=$assemblyVersion" `
    "-p:InformationalVersion=$Version" `
    -p:IncludeSourceRevisionInInformationalVersion=false `
    -o $publishDir

$requiredFiles = @(
    "FramePlayer.Avalonia.exe",
    "frameplayer_ffmpeg_probe.dll",
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

$exePath = Join-Path $publishDir "FramePlayer.Avalonia.exe"
$productVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exePath).ProductVersion
if ($productVersion -ne $Version) {
    throw "Published executable product version '$productVersion' did not match package version '$Version'."
}

if (Test-Path $zipPath) {
    Remove-Item -Force $zipPath
}

Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -Force
$hash = Get-FileHash -Algorithm SHA256 $zipPath
"$($hash.Hash.ToLowerInvariant())  $(Split-Path -Leaf $zipPath)" | Set-Content -NoNewline -Encoding ascii $shaPath

Write-Host "Packaged Frame Player for Windows:"
Write-Host $zipPath
Get-Content $shaPath
