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

function Get-ManifestFileHashes {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Manifest
    )

    if ($null -eq $Manifest.files) {
        return @{}
    }

    $hashes = @{}
    foreach ($property in $Manifest.files.PSObject.Properties) {
        $hashes[$property.Name] = [string]$property.Value
    }

    return $hashes
}

function Get-Sha256 {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath
    )

    return (Get-FileHash -Path $FilePath -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Test-RuntimeIntegrity {
    param(
        [Parameter(Mandatory = $true)]
        [string]$DirectoryPath,
        [Parameter(Mandatory = $true)]
        [hashtable]$ExpectedHashes
    )

    if (-not (Test-RuntimeDirectory -DirectoryPath $DirectoryPath)) {
        return $false
    }

    foreach ($entry in $ExpectedHashes.GetEnumerator()) {
        $filePath = Join-Path $DirectoryPath $entry.Key
        if (-not (Test-Path $filePath)) {
            return $false
        }

        $actualHash = Get-Sha256 -FilePath $filePath
        if ($actualHash -ne $entry.Value.ToLowerInvariant()) {
            return $false
        }
    }

    return $true
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
$expectedFileHashes = Get-ManifestFileHashes -Manifest $manifest

$requiredRuntimeFiles = @(
    "avcodec-62.dll",
    "avformat-62.dll",
    "avutil-60.dll",
    "swresample-6.dll",
    "swscale-9.dll",
    "libwinpthread-1.dll"
)

$missingRequiredManifestFiles = $requiredRuntimeFiles | Where-Object { -not $expectedFileHashes.ContainsKey($_) }
if ($missingRequiredManifestFiles.Count -gt 0) {
    throw "Runtime manifest appears stale or mismatched for the FFmpeg 8.1 runtime. Missing required file hash entries: $($missingRequiredManifestFiles -join ', '). Update Runtime\\runtime-manifest.json to the current FFmpeg 8.1 runtime metadata."
}

$runtimeRoot = Join-Path $repoRoot "Runtime"
$runtimeDirectory = Join-Path $runtimeRoot "ffmpeg"
$candidateRuntimeDirectory = Join-Path $runtimeRoot "ffmpeg-8.1-candidate"
$artifactsRoot = Join-Path $repoRoot "artifacts"

if ((Test-RuntimeDirectory -DirectoryPath $runtimeDirectory) -and (Test-RuntimeIntegrity -DirectoryPath $runtimeDirectory -ExpectedHashes $expectedFileHashes)) {
    Write-Host "FFmpeg runtime already present at '$runtimeDirectory'."
    exit 0
}

if ((Test-RuntimeDirectory -DirectoryPath $candidateRuntimeDirectory) -and (Test-RuntimeIntegrity -DirectoryPath $candidateRuntimeDirectory -ExpectedHashes $expectedFileHashes)) {
    Write-Host "Restoring FFmpeg runtime from local source-built candidate at '$candidateRuntimeDirectory'."
    Remove-Item $runtimeDirectory -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $runtimeDirectory | Out-Null
    Copy-Item (Join-Path $candidateRuntimeDirectory "*.dll") -Destination $runtimeDirectory -Force

    if (-not (Test-RuntimeIntegrity -DirectoryPath $runtimeDirectory -ExpectedHashes $expectedFileHashes)) {
        throw "The local FFmpeg 8.1 candidate runtime failed integrity validation after restore."
    }

    Write-Host "Runtime ready at '$runtimeDirectory'."
    exit 0
}

if ([string]::IsNullOrWhiteSpace($manifest.assetName)) {
    throw "Runtime manifest is missing the assetName field."
}

$hasRemoteLocation = -not [string]::IsNullOrWhiteSpace($manifest.assetUrl) -or -not [string]::IsNullOrWhiteSpace($manifest.tag)
if (-not $hasRemoteLocation) {
    throw "Runtime manifest is missing both tag and assetUrl. CI/bootstrap cannot download the pinned FFmpeg 8.1 runtime archive when local runtime artifacts are absent."
}

$archiveCandidatePaths = @()
if (-not [string]::IsNullOrWhiteSpace($manifest.assetName)) {
    $archiveCandidatePaths += (Join-Path $runtimeRoot $manifest.assetName)
    $archiveCandidatePaths += (Join-Path $artifactsRoot $manifest.assetName)
}
$archiveCandidatePaths = $archiveCandidatePaths | Select-Object -Unique
$archivePath = $archiveCandidatePaths | Where-Object { Test-Path $_ } | Select-Object -First 1

$hasArchiveHash = -not [string]::IsNullOrWhiteSpace($manifest.assetSha256)
$extractRoot = Join-Path $env:TEMP ("frameplayer-runtime-" + [Guid]::NewGuid().ToString("N"))

New-Item -ItemType Directory -Force -Path $runtimeRoot | Out-Null
New-Item -ItemType Directory -Force -Path $extractRoot | Out-Null

try {
    if ($null -eq $archivePath) {
        if (-not $hasRemoteLocation) {
            throw "No valid local FFmpeg runtime candidate or archive was found. Run .\scripts\ffmpeg\Build-FFmpeg-8.1.ps1 first."
        }

        if (-not $hasArchiveHash) {
            throw "Runtime manifest is missing assetSha256 for the runtime archive download."
        }

        $downloadUrl = if (-not [string]::IsNullOrWhiteSpace($manifest.assetUrl)) {
            [string]$manifest.assetUrl
        }
        else {
            "https://github.com/jfleezy23/frame-player/releases/download/{0}/{1}" -f $manifest.tag, $manifest.assetName
        }

        $archivePath = Join-Path $env:TEMP $manifest.assetName
        Write-Host "Downloading FFmpeg runtime from $downloadUrl"
        Invoke-WebRequest -Uri $downloadUrl -OutFile $archivePath
    }
    else {
        Write-Host "Restoring FFmpeg runtime from local archive '$archivePath'."
    }

    if ($hasArchiveHash) {
        $archiveHash = Get-Sha256 -FilePath $archivePath
        if ($archiveHash -ne $manifest.assetSha256.ToLowerInvariant()) {
            throw "The FFmpeg runtime archive failed SHA256 validation."
        }
    }

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

    if (-not (Test-RuntimeIntegrity -DirectoryPath $runtimeDirectory -ExpectedHashes $expectedFileHashes)) {
        throw "The downloaded runtime failed integrity validation."
    }

    Write-Host "Runtime ready at '$runtimeDirectory'."
}
finally {
    Remove-Item $extractRoot -Recurse -Force -ErrorAction SilentlyContinue
    if ($archivePath -like (Join-Path $env:TEMP "*")) {
        Remove-Item $archivePath -Force -ErrorAction SilentlyContinue
    }
}
