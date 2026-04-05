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
$runtimeRoot = Join-Path $repoRoot "Runtime"
$runtimeDirectory = Join-Path $runtimeRoot "ffmpeg"

if ((Test-RuntimeDirectory -DirectoryPath $runtimeDirectory) -and (Test-RuntimeIntegrity -DirectoryPath $runtimeDirectory -ExpectedHashes $expectedFileHashes)) {
    Write-Host "FFmpeg runtime already present at '$runtimeDirectory'."
    exit 0
}

if ([string]::IsNullOrWhiteSpace($manifest.tag) -or [string]::IsNullOrWhiteSpace($manifest.assetName) -or [string]::IsNullOrWhiteSpace($manifest.assetSha256)) {
    throw "Runtime manifest is missing the tag, assetName, or assetSha256 fields."
}

$downloadUrl = if (-not [string]::IsNullOrWhiteSpace($manifest.assetUrl)) {
    [string]$manifest.assetUrl
}
else {
    "https://github.com/jfleezy23/frame-player/releases/download/{0}/{1}" -f $manifest.tag, $manifest.assetName
}
$archivePath = Join-Path $env:TEMP $manifest.assetName
$extractRoot = Join-Path $env:TEMP ("frameplayer-runtime-" + [Guid]::NewGuid().ToString("N"))

New-Item -ItemType Directory -Force -Path $runtimeRoot | Out-Null
New-Item -ItemType Directory -Force -Path $extractRoot | Out-Null

try {
    Write-Host "Downloading FFmpeg runtime from $downloadUrl"
    Invoke-WebRequest -Uri $downloadUrl -OutFile $archivePath

    $archiveHash = Get-Sha256 -FilePath $archivePath
    if ($archiveHash -ne $manifest.assetSha256.ToLowerInvariant()) {
        throw "The downloaded runtime archive failed SHA256 validation."
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
    Remove-Item $archivePath -Force -ErrorAction SilentlyContinue
}
