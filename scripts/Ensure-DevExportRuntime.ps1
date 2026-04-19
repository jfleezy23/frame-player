param(
    [string]$ManifestPath = "",
    [switch]$Required
)

$ErrorActionPreference = "Stop"

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

    $stream = [System.IO.File]::OpenRead($FilePath)
    try {
        $sha256 = [System.Security.Cryptography.SHA256]::Create()
        try {
            $hashBytes = $sha256.ComputeHash($stream)
        }
        finally {
            $sha256.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }

    return [System.BitConverter]::ToString($hashBytes).Replace("-", "").ToLowerInvariant()
}

function Test-ManifestLeafName {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    if ([string]::IsNullOrWhiteSpace($Value) -or [System.IO.Path]::IsPathRooted($Value)) {
        return $false
    }

    return [string]::Equals(
        [System.IO.Path]::GetFileName($Value),
        $Value,
        [System.StringComparison]::Ordinal)
}

function Assert-HttpsUrl {
    param(
        [string]$Url,
        [Parameter(Mandatory = $true)]
        [string]$Label
    )

    if ([string]::IsNullOrWhiteSpace($Url)) {
        return
    }

    $parsedUri = $null
    if (-not [System.Uri]::TryCreate($Url, [System.UriKind]::Absolute, [ref]$parsedUri) -or
        -not [string]::Equals($parsedUri.Scheme, [System.Uri]::UriSchemeHttps, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "$Label must be an absolute HTTPS URL."
    }
}

function Test-ExportRuntimeIntegrity {
    param(
        [Parameter(Mandatory = $true)]
        [string]$DirectoryPath,
        [Parameter(Mandatory = $true)]
        [hashtable]$ExpectedHashes
    )

    foreach ($entry in $ExpectedHashes.GetEnumerator()) {
        $filePath = Join-Path $DirectoryPath $entry.Key
        if (-not (Test-Path -LiteralPath $filePath)) {
            return $false
        }

        $actualHash = Get-Sha256 -FilePath $filePath
        if ($actualHash -ne $entry.Value.ToLowerInvariant()) {
            return $false
        }
    }

    return $true
}

function Complete-BestEffort {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    if ($Required) {
        throw $Message
    }

    Write-Warning $Message
    exit 0
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$resolvedManifestPath = if ([string]::IsNullOrWhiteSpace($ManifestPath)) {
    Join-Path $repoRoot "Runtime\export-runtime-manifest.json"
}
else {
    $ManifestPath
}

if (-not (Test-Path -LiteralPath $resolvedManifestPath)) {
    Complete-BestEffort "Export-runtime manifest not found at '$resolvedManifestPath'."
}

$manifest = Get-Content -LiteralPath $resolvedManifestPath -Raw | ConvertFrom-Json
$expectedFileHashes = Get-ManifestFileHashes -Manifest $manifest

if (-not [string]::IsNullOrWhiteSpace($manifest.assetName) -and -not (Test-ManifestLeafName -Value $manifest.assetName)) {
    Complete-BestEffort "Export-runtime manifest assetName must be a leaf filename."
}

$invalidManifestFileEntries = @($expectedFileHashes.Keys | Where-Object { -not (Test-ManifestLeafName -Value $_) })
if ($invalidManifestFileEntries.Count -gt 0) {
    Complete-BestEffort "Export-runtime manifest contains invalid file entries: $($invalidManifestFileEntries -join ', ')."
}

try {
    Assert-HttpsUrl -Url ([string]$manifest.assetUrl) -Label "Export-runtime manifest assetUrl"
}
catch {
    Complete-BestEffort $_.Exception.Message
}

if ($expectedFileHashes.Count -eq 0) {
    Complete-BestEffort "Export-runtime manifest does not yet contain pinned file hashes. Build the runtime locally with .\scripts\ffmpeg\Build-FFmpeg-ExportRuntime-8.1.ps1 and update Runtime\export-runtime-manifest.json."
}

$runtimeRoot = Join-Path $repoRoot "Runtime"
$runtimeDirectory = Join-Path $runtimeRoot "ffmpeg-export"
$candidateRuntimeDirectory = Join-Path $runtimeRoot "ffmpeg-export-8.1-candidate"
$toolsDirectory = Join-Path $runtimeRoot "ffmpeg-tools"
$toolsCandidateDirectory = Join-Path $runtimeRoot "ffmpeg-tools-8.1-candidate"
$artifactsRoot = Join-Path $repoRoot "artifacts"

if ((Test-Path -LiteralPath $runtimeDirectory) -and
    (Test-ExportRuntimeIntegrity -DirectoryPath $runtimeDirectory -ExpectedHashes $expectedFileHashes)) {
    Write-Host "FFmpeg export runtime already present at '$runtimeDirectory'."
    exit 0
}

if ((Test-Path -LiteralPath $candidateRuntimeDirectory) -and
    (Test-ExportRuntimeIntegrity -DirectoryPath $candidateRuntimeDirectory -ExpectedHashes $expectedFileHashes)) {
    Write-Host "Restoring FFmpeg export runtime from local source-built candidate at '$candidateRuntimeDirectory'."
    Remove-Item -LiteralPath $runtimeDirectory -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $runtimeDirectory | Out-Null
    Copy-Item -Path (Join-Path $candidateRuntimeDirectory "*") -Destination $runtimeDirectory -Recurse -Force

    if (-not (Test-ExportRuntimeIntegrity -DirectoryPath $runtimeDirectory -ExpectedHashes $expectedFileHashes)) {
        throw "The local FFmpeg export runtime candidate failed integrity validation after restore."
    }

    Write-Host "Export runtime ready at '$runtimeDirectory'."
    exit 0
}

foreach ($sourceDirectory in @($toolsDirectory, $toolsCandidateDirectory)) {
    if (-not (Test-Path -LiteralPath $sourceDirectory)) {
        continue
    }

    $missingSourceFiles = @($expectedFileHashes.Keys | Where-Object { -not (Test-Path -LiteralPath (Join-Path $sourceDirectory $_)) })
    if ($missingSourceFiles.Count -gt 0) {
        continue
    }

    Write-Host "Restoring FFmpeg export runtime from local FFmpeg tools directory '$sourceDirectory'."
    Remove-Item -LiteralPath $runtimeDirectory -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $runtimeDirectory | Out-Null

    foreach ($fileName in $expectedFileHashes.Keys) {
        Copy-Item -LiteralPath (Join-Path $sourceDirectory $fileName) -Destination (Join-Path $runtimeDirectory $fileName) -Force
    }

    if (-not (Test-ExportRuntimeIntegrity -DirectoryPath $runtimeDirectory -ExpectedHashes $expectedFileHashes)) {
        throw "The FFmpeg tools directory failed export-runtime integrity validation after restore."
    }

    Write-Host "Export runtime ready at '$runtimeDirectory'."
    exit 0
}

if ([string]::IsNullOrWhiteSpace($manifest.assetName)) {
    Complete-BestEffort "Export-runtime manifest is missing the assetName field."
}

$hasRemoteLocation = -not [string]::IsNullOrWhiteSpace($manifest.assetUrl) -or -not [string]::IsNullOrWhiteSpace($manifest.tag)
$archiveCandidatePaths = @(
    (Join-Path $runtimeRoot $manifest.assetName),
    (Join-Path $artifactsRoot $manifest.assetName)
) | Select-Object -Unique
$archivePath = $archiveCandidatePaths | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1

if ($null -eq $archivePath -and -not $hasRemoteLocation) {
    Complete-BestEffort "No pinned FFmpeg export-runtime archive is staged locally. Build it with .\scripts\ffmpeg\Build-FFmpeg-ExportRuntime-8.1.ps1 first."
}

if ([string]::IsNullOrWhiteSpace($manifest.assetSha256)) {
    Complete-BestEffort "Export-runtime manifest is missing assetSha256 for archive validation."
}

$extractRoot = Join-Path $env:TEMP ("frameplayer-export-runtime-" + [Guid]::NewGuid().ToString("N"))

New-Item -ItemType Directory -Force -Path $runtimeRoot | Out-Null
New-Item -ItemType Directory -Force -Path $extractRoot | Out-Null

try {
    if ($null -eq $archivePath) {
        $downloadUrl = if (-not [string]::IsNullOrWhiteSpace($manifest.assetUrl)) {
            [string]$manifest.assetUrl
        }
        else {
            "https://github.com/jfleezy23/frame-player/releases/download/{0}/{1}" -f $manifest.tag, $manifest.assetName
        }

        $archivePath = Join-Path $env:TEMP (([Guid]::NewGuid().ToString("N")) + "-" + $manifest.assetName)
        Write-Host "Downloading FFmpeg export runtime from $downloadUrl"
        Invoke-WebRequest -Uri $downloadUrl -OutFile $archivePath
    }
    else {
        Write-Host "Restoring FFmpeg export runtime from local archive '$archivePath'."
    }

    $archiveHash = Get-Sha256 -FilePath $archivePath
    if ($archiveHash -ne $manifest.assetSha256.ToLowerInvariant()) {
        throw "The FFmpeg export-runtime archive failed SHA256 validation."
    }

    Expand-Archive -Path $archivePath -DestinationPath $extractRoot -Force

    $sourceDirectory = Join-Path $extractRoot "ffmpeg-export"
    if ((Test-Path -LiteralPath $extractRoot) -and
        (Test-ExportRuntimeIntegrity -DirectoryPath $extractRoot -ExpectedHashes $expectedFileHashes)) {
        $sourceDirectory = $extractRoot
    }
    elseif (-not (Test-Path -LiteralPath $sourceDirectory)) {
        $nestedRuntimeDirectory = Get-ChildItem -LiteralPath $extractRoot -Directory -Recurse |
            Where-Object { $_.Name -ieq "ffmpeg-export" } |
            Select-Object -First 1

        if ($null -eq $nestedRuntimeDirectory) {
            throw "The export-runtime archive did not contain an 'ffmpeg-export' directory or the expected files at archive root."
        }

        $sourceDirectory = $nestedRuntimeDirectory.FullName
    }

    Remove-Item -LiteralPath $runtimeDirectory -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $runtimeDirectory | Out-Null
    Copy-Item -Path (Join-Path $sourceDirectory "*") -Destination $runtimeDirectory -Recurse -Force

    if (-not (Test-ExportRuntimeIntegrity -DirectoryPath $runtimeDirectory -ExpectedHashes $expectedFileHashes)) {
        throw "The restored FFmpeg export runtime failed integrity validation."
    }

    Write-Host "Export runtime ready at '$runtimeDirectory'."
}
finally {
    Remove-Item -LiteralPath $extractRoot -Recurse -Force -ErrorAction SilentlyContinue
    if ($archivePath -like (Join-Path $env:TEMP "*")) {
        Remove-Item -LiteralPath $archivePath -Force -ErrorAction SilentlyContinue
    }
}
