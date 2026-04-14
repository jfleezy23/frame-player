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

    return (Get-FileHash -Path $FilePath -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Test-ToolsDirectory {
    param(
        [Parameter(Mandatory = $true)]
        [string]$DirectoryPath,
        [Parameter(Mandatory = $true)]
        [string[]]$RequiredFiles
    )

    if (-not (Test-Path -LiteralPath $DirectoryPath)) {
        return $false
    }

    foreach ($fileName in $RequiredFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $DirectoryPath $fileName))) {
            return $false
        }
    }

    return $true
}

function Test-ToolsIntegrity {
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
    Join-Path $repoRoot "Runtime\export-tools-manifest.json"
}
else {
    $ManifestPath
}

if (-not (Test-Path -LiteralPath $resolvedManifestPath)) {
    Complete-BestEffort "Export-tools manifest not found at '$resolvedManifestPath'."
}

$manifest = Get-Content -LiteralPath $resolvedManifestPath -Raw | ConvertFrom-Json
$expectedFileHashes = Get-ManifestFileHashes -Manifest $manifest
$requiredToolFiles = @("ffmpeg.exe", "ffprobe.exe")

if ($expectedFileHashes.Count -eq 0) {
    Complete-BestEffort "Export-tools manifest does not yet contain pinned file hashes. Build the tool bundle locally with .\scripts\ffmpeg\Build-FFmpeg-Tools-8.1.ps1 and update Runtime\export-tools-manifest.json."
}

$missingManifestFiles = $requiredToolFiles | Where-Object { -not $expectedFileHashes.ContainsKey($_) }
if ($missingManifestFiles.Count -gt 0) {
    Complete-BestEffort "Export-tools manifest is missing required file hash entries: $($missingManifestFiles -join ', ')."
}

$runtimeRoot = Join-Path $repoRoot "Runtime"
$toolsDirectory = Join-Path $runtimeRoot "ffmpeg-tools"
$candidateToolsDirectory = Join-Path $runtimeRoot "ffmpeg-tools-8.1-candidate"
$artifactsRoot = Join-Path $repoRoot "artifacts"

if ((Test-ToolsDirectory -DirectoryPath $toolsDirectory -RequiredFiles $requiredToolFiles) -and
    (Test-ToolsIntegrity -DirectoryPath $toolsDirectory -ExpectedHashes $expectedFileHashes)) {
    Write-Host "FFmpeg export tools already present at '$toolsDirectory'."
    exit 0
}

if ((Test-ToolsDirectory -DirectoryPath $candidateToolsDirectory -RequiredFiles $requiredToolFiles) -and
    (Test-ToolsIntegrity -DirectoryPath $candidateToolsDirectory -ExpectedHashes $expectedFileHashes)) {
    Write-Host "Restoring FFmpeg export tools from local source-built candidate at '$candidateToolsDirectory'."
    Remove-Item -LiteralPath $toolsDirectory -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $toolsDirectory | Out-Null
    Copy-Item -Path (Join-Path $candidateToolsDirectory "*") -Destination $toolsDirectory -Recurse -Force

    if (-not (Test-ToolsIntegrity -DirectoryPath $toolsDirectory -ExpectedHashes $expectedFileHashes)) {
        throw "The local FFmpeg export tools candidate failed integrity validation after restore."
    }

    Write-Host "Export tools ready at '$toolsDirectory'."
    exit 0
}

if ([string]::IsNullOrWhiteSpace($manifest.assetName)) {
    Complete-BestEffort "Export-tools manifest is missing the assetName field."
}

$hasRemoteLocation = -not [string]::IsNullOrWhiteSpace($manifest.assetUrl) -or -not [string]::IsNullOrWhiteSpace($manifest.tag)
$archiveCandidatePaths = @(
    (Join-Path $runtimeRoot $manifest.assetName),
    (Join-Path $artifactsRoot $manifest.assetName)
) | Select-Object -Unique
$archivePath = $archiveCandidatePaths | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1

if ($null -eq $archivePath -and -not $hasRemoteLocation) {
    Complete-BestEffort "No pinned FFmpeg export-tools archive is staged locally. Build it with .\scripts\ffmpeg\Build-FFmpeg-Tools-8.1.ps1 first."
}

if ([string]::IsNullOrWhiteSpace($manifest.assetSha256)) {
    Complete-BestEffort "Export-tools manifest is missing assetSha256 for archive validation."
}

$extractRoot = Join-Path $env:TEMP ("frameplayer-export-tools-" + [Guid]::NewGuid().ToString("N"))

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
        Write-Host "Downloading FFmpeg export tools from $downloadUrl"
        Invoke-WebRequest -Uri $downloadUrl -OutFile $archivePath
    }
    else {
        Write-Host "Restoring FFmpeg export tools from local archive '$archivePath'."
    }

    $archiveHash = Get-Sha256 -FilePath $archivePath
    if ($archiveHash -ne $manifest.assetSha256.ToLowerInvariant()) {
        throw "The FFmpeg export-tools archive failed SHA256 validation."
    }

    Expand-Archive -Path $archivePath -DestinationPath $extractRoot -Force

    $sourceDirectory = Join-Path $extractRoot "ffmpeg-tools"
    if ((Test-ToolsDirectory -DirectoryPath $extractRoot -RequiredFiles $requiredToolFiles)) {
        $sourceDirectory = $extractRoot
    }
    elseif (-not (Test-Path -LiteralPath $sourceDirectory)) {
        $nestedToolsDirectory = Get-ChildItem -LiteralPath $extractRoot -Directory -Recurse |
            Where-Object { $_.Name -ieq "ffmpeg-tools" } |
            Select-Object -First 1

        if ($null -eq $nestedToolsDirectory) {
            throw "The export-tools archive did not contain an 'ffmpeg-tools' directory or the expected files at archive root."
        }

        $sourceDirectory = $nestedToolsDirectory.FullName
    }

    Remove-Item -LiteralPath $toolsDirectory -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $toolsDirectory | Out-Null
    Copy-Item -Path (Join-Path $sourceDirectory "*") -Destination $toolsDirectory -Recurse -Force

    if (-not (Test-ToolsIntegrity -DirectoryPath $toolsDirectory -ExpectedHashes $expectedFileHashes)) {
        throw "The restored FFmpeg export tools failed integrity validation."
    }

    Write-Host "Export tools ready at '$toolsDirectory'."
}
finally {
    Remove-Item -LiteralPath $extractRoot -Recurse -Force -ErrorAction SilentlyContinue
    if ($archivePath -like (Join-Path $env:TEMP "*")) {
        Remove-Item -LiteralPath $archivePath -Force -ErrorAction SilentlyContinue
    }
}
