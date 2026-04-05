param(
    [string]$ManifestPath = ""
)

$ErrorActionPreference = "Stop"

function Test-RuntimeDirectory {
    param(
        [Parameter(Mandatory = $true)]
        [string]$DirectoryPath
    )

    if (-not (Test-Path $DirectoryPath)) {
        return $false
    }

    return (Get-ChildItem $DirectoryPath -Filter "avcodec*.dll" -ErrorAction SilentlyContinue | Select-Object -First 1) -and
        (Get-ChildItem $DirectoryPath -Filter "avformat*.dll" -ErrorAction SilentlyContinue | Select-Object -First 1) -and
        (Get-ChildItem $DirectoryPath -Filter "avutil*.dll" -ErrorAction SilentlyContinue | Select-Object -First 1) -and
        (Get-ChildItem $DirectoryPath -Filter "swresample*.dll" -ErrorAction SilentlyContinue | Select-Object -First 1) -and
        (Get-ChildItem $DirectoryPath -Filter "swscale*.dll" -ErrorAction SilentlyContinue | Select-Object -First 1)
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$resolvedManifestPath = if ([string]::IsNullOrWhiteSpace($ManifestPath)) {
    Join-Path $repoRoot "Runtime\runtime-manifest.json"
}
else {
    $ManifestPath
}

if (-not (Test-Path $resolvedManifestPath)) {
    throw "Runtime manifest not found at '$resolvedManifestPath'."
}

$manifest = Get-Content $resolvedManifestPath -Raw | ConvertFrom-Json
$runtimeRoot = Join-Path $repoRoot "Runtime"
$runtimeDirectory = Join-Path $runtimeRoot "ffmpeg"

if (Test-RuntimeDirectory -DirectoryPath $runtimeDirectory) {
    Write-Host "FFmpeg runtime already present at '$runtimeDirectory'."
    exit 0
}

if ([string]::IsNullOrWhiteSpace($manifest.tag) -or [string]::IsNullOrWhiteSpace($manifest.assetName)) {
    throw "Runtime manifest is missing the tag or assetName fields."
}

$downloadUrl = "https://github.com/jfleezy23/frame-player/releases/download/{0}/{1}" -f $manifest.tag, $manifest.assetName
$archivePath = Join-Path $env:TEMP $manifest.assetName
$extractRoot = Join-Path $env:TEMP ("frameplayer-runtime-" + [Guid]::NewGuid().ToString("N"))

New-Item -ItemType Directory -Force -Path $runtimeRoot | Out-Null
New-Item -ItemType Directory -Force -Path $extractRoot | Out-Null

try {
    Write-Host "Downloading FFmpeg runtime from $downloadUrl"
    Invoke-WebRequest -Uri $downloadUrl -OutFile $archivePath

    Write-Host "Extracting runtime archive"
    Expand-Archive -Path $archivePath -DestinationPath $extractRoot -Force

    $sourceDirectory = Join-Path $extractRoot "ffmpeg"
    if (-not (Test-Path $sourceDirectory)) {
        $nestedFfmpegDirectory = Get-ChildItem $extractRoot -Directory -Recurse |
            Where-Object { $_.Name -ieq "ffmpeg" } |
            Select-Object -First 1

        if ($null -eq $nestedFfmpegDirectory) {
            throw "The runtime archive did not contain an 'ffmpeg' directory."
        }

        $sourceDirectory = $nestedFfmpegDirectory.FullName
    }

    Remove-Item $runtimeDirectory -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $runtimeDirectory | Out-Null
    Copy-Item (Join-Path $sourceDirectory "*.dll") -Destination $runtimeDirectory -Force

    if (-not (Test-RuntimeDirectory -DirectoryPath $runtimeDirectory)) {
        throw "The downloaded runtime is incomplete."
    }

    Write-Host "Runtime ready at '$runtimeDirectory'."
}
finally {
    Remove-Item $extractRoot -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item $archivePath -Force -ErrorAction SilentlyContinue
}
