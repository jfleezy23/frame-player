param(
    [ValidateSet("Release")]
    [string]$Configuration = "Release",

    [ValidateSet("x64")]
    [string]$Platform = "x64",

    [string]$OutputDirectory = "",

    [string]$IntermediateDirectory = "",

    [string]$ArtifactPath = "",

    [switch]$RequireExportTools
)

$ErrorActionPreference = "Stop"

function Get-ManifestFileHashes {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Manifest
    )

    $hashes = @{}
    if ($null -eq $Manifest.files) {
        return $hashes
    }

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

    (Get-FileHash -LiteralPath $FilePath -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Assert-PathWithin {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$Root
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $fullRoot = [System.IO.Path]::GetFullPath($Root)
    if (-not $fullRoot.EndsWith([System.IO.Path]::DirectorySeparatorChar.ToString(), [System.StringComparison]::Ordinal)) {
        $fullRoot += [System.IO.Path]::DirectorySeparatorChar
    }

    if (-not $fullPath.StartsWith($fullRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to operate on '$fullPath' because it is outside '$fullRoot'."
    }

    return $fullPath
}

function Get-RepoRelativePath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$Root
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $fullRoot = [System.IO.Path]::GetFullPath($Root)
    if (-not $fullRoot.EndsWith([System.IO.Path]::DirectorySeparatorChar.ToString(), [System.StringComparison]::Ordinal)) {
        $fullRoot += [System.IO.Path]::DirectorySeparatorChar
    }

    if (-not $fullPath.StartsWith($fullRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Path '$fullPath' is not under repository root '$fullRoot'."
    }

    return $fullPath.Substring($fullRoot.Length)
}

function Test-OutputRuntimeIntegrity {
    param(
        [Parameter(Mandatory = $true)]
        [string]$DirectoryPath,
        [Parameter(Mandatory = $true)]
        [hashtable]$ExpectedHashes
    )

    foreach ($entry in $ExpectedHashes.GetEnumerator()) {
        $filePath = Join-Path $DirectoryPath $entry.Key
        if (-not (Test-Path -LiteralPath $filePath)) {
            throw "Packaged output is missing required runtime file '$($entry.Key)'."
        }

        $actualHash = Get-Sha256 -FilePath $filePath
        if ($actualHash -ne $entry.Value.ToLowerInvariant()) {
            throw "Packaged output failed integrity validation for '$($entry.Key)'."
        }
    }
}

function Stop-OutputProcesses {
    param(
        [Parameter(Mandatory = $true)]
        [string]$OutputDirectory
    )

    if (-not (Test-Path -LiteralPath $OutputDirectory)) {
        return
    }

    $normalizedOutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
    if (-not $normalizedOutputDirectory.EndsWith([System.IO.Path]::DirectorySeparatorChar.ToString(), [System.StringComparison]::Ordinal)) {
        $normalizedOutputDirectory += [System.IO.Path]::DirectorySeparatorChar
    }

    $processes = @(
        Get-CimInstance Win32_Process -Filter "Name = 'FramePlayer.exe'" |
            Where-Object {
                if ([string]::IsNullOrWhiteSpace($_.ExecutablePath)) {
                    return $false
                }

                $executablePath = [System.IO.Path]::GetFullPath($_.ExecutablePath)
                return $executablePath.StartsWith($normalizedOutputDirectory, [System.StringComparison]::OrdinalIgnoreCase)
            }
    )

    foreach ($process in $processes) {
        try {
            Stop-Process -Id $process.ProcessId -Force -ErrorAction Stop | Out-Null
            Wait-Process -Id $process.ProcessId -Timeout 5 -ErrorAction SilentlyContinue
        }
        catch {
            throw "Could not stop stale test-drop process $($process.ProcessId) from '$($process.ExecutablePath)': $($_.Exception.Message)"
        }
    }
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$resolvedOutputDirectory = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    Join-Path $repoRoot "bin\TestDrop"
}
else {
    $OutputDirectory
}
$resolvedIntermediateDirectory = if ([string]::IsNullOrWhiteSpace($IntermediateDirectory)) {
    Join-Path $repoRoot "obj\TestDrop"
}
else {
    $IntermediateDirectory
}

$resolvedOutputDirectory = Assert-PathWithin -Path $resolvedOutputDirectory -Root (Join-Path $repoRoot "bin")
$resolvedIntermediateDirectory = Assert-PathWithin -Path $resolvedIntermediateDirectory -Root (Join-Path $repoRoot "obj")

$ensureRuntimeScript = Join-Path $PSScriptRoot "Ensure-DevRuntime.ps1"
$ensureExportRuntimeScript = Join-Path $PSScriptRoot "Ensure-DevExportRuntime.ps1"
$ensureExportToolsScript = Join-Path $PSScriptRoot "Ensure-DevExportTools.ps1"
$projectPath = Join-Path $repoRoot "FramePlayer.csproj"
$manifestPath = Join-Path $repoRoot "Runtime\runtime-manifest.json"
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$expectedRuntimeFiles = Get-ManifestFileHashes -Manifest $manifest
$exportRuntimeManifestPath = Join-Path $repoRoot "Runtime\export-runtime-manifest.json"
$exportRuntimeManifest = if (Test-Path -LiteralPath $exportRuntimeManifestPath) {
    Get-Content -LiteralPath $exportRuntimeManifestPath -Raw | ConvertFrom-Json
}
else {
    $null
}
$expectedExportRuntimeFiles = if ($null -ne $exportRuntimeManifest) {
    Get-ManifestFileHashes -Manifest $exportRuntimeManifest
}
else {
    @{}
}
$exportToolsManifestPath = Join-Path $repoRoot "Runtime\export-tools-manifest.json"
$exportToolsManifest = if (Test-Path -LiteralPath $exportToolsManifestPath) {
    Get-Content -LiteralPath $exportToolsManifestPath -Raw | ConvertFrom-Json
}
else {
    $null
}
$expectedExportToolsFiles = if ($null -ne $exportToolsManifest) {
    Get-ManifestFileHashes -Manifest $exportToolsManifest
}
else {
    @{}
}

& $ensureRuntimeScript
if ($expectedExportToolsFiles.Count -gt 0 -and (Test-Path -LiteralPath $ensureExportToolsScript)) {
    if ($RequireExportTools) {
        & $ensureExportToolsScript -Required
    }
    else {
        & $ensureExportToolsScript
    }
}
if ($expectedExportRuntimeFiles.Count -gt 0 -and (Test-Path -LiteralPath $ensureExportRuntimeScript)) {
    & $ensureExportRuntimeScript
}

try {
    if (Test-Path -LiteralPath $resolvedOutputDirectory) {
        Stop-OutputProcesses -OutputDirectory $resolvedOutputDirectory
        Remove-Item -LiteralPath $resolvedOutputDirectory -Recurse -Force
    }

    if (Test-Path -LiteralPath $resolvedIntermediateDirectory) {
        Remove-Item -LiteralPath $resolvedIntermediateDirectory -Recurse -Force
    }
}
catch {
    if (-not [string]::IsNullOrWhiteSpace($OutputDirectory) -or
        -not [string]::IsNullOrWhiteSpace($IntermediateDirectory)) {
        throw
    }

    $fallbackSuffix = [Guid]::NewGuid().ToString("N").Substring(0, 8)
    $fallbackOutputDirectory = Join-Path (Join-Path $repoRoot "bin") ("TestDrop-" + $fallbackSuffix)
    $fallbackIntermediateDirectory = Join-Path (Join-Path $repoRoot "obj") ("TestDrop-" + $fallbackSuffix)
    $resolvedOutputDirectory = Assert-PathWithin -Path $fallbackOutputDirectory -Root (Join-Path $repoRoot "bin")
    $resolvedIntermediateDirectory = Assert-PathWithin -Path $fallbackIntermediateDirectory -Root (Join-Path $repoRoot "obj")
    Write-Warning ("Could not clean the default test-drop directories. Building into fallback output '{0}' instead." -f $resolvedOutputDirectory)
}

New-Item -ItemType Directory -Force -Path $resolvedOutputDirectory | Out-Null
New-Item -ItemType Directory -Force -Path $resolvedIntermediateDirectory | Out-Null

$msbuildOutputPath = Get-RepoRelativePath -Path $resolvedOutputDirectory -Root $repoRoot
$msbuildIntermediatePath = Get-RepoRelativePath -Path $resolvedIntermediateDirectory -Root $repoRoot
if (-not $msbuildOutputPath.EndsWith("\")) {
    $msbuildOutputPath += "\"
}

if (-not $msbuildIntermediatePath.EndsWith("\")) {
    $msbuildIntermediatePath += "\"
}

& dotnet build $projectPath -c $Configuration -p:Platform=$Platform -p:OutputPath=$msbuildOutputPath -p:IntermediateOutputPath=$msbuildIntermediatePath
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed while producing the test drop."
}

$exePath = Join-Path $resolvedOutputDirectory "FramePlayer.exe"
if (-not (Test-Path -LiteralPath $exePath)) {
    throw "Expected packaged executable was not found at '$exePath'."
}

$staleRuntimeFiles = @(
    "avcodec-61.dll",
    "avdevice-61.dll",
    "avfilter-10.dll",
    "avformat-61.dll",
    "avutil-59.dll",
    "swresample-5.dll",
    "swscale-8.dll"
) | Where-Object { Test-Path -LiteralPath (Join-Path $resolvedOutputDirectory $_) }

if ($staleRuntimeFiles.Count -gt 0) {
    throw "Packaged output still contains stale runtime files: $([string]::Join(', ', $staleRuntimeFiles))."
}

Test-OutputRuntimeIntegrity -DirectoryPath $resolvedOutputDirectory -ExpectedHashes $expectedRuntimeFiles

if ($expectedExportRuntimeFiles.Count -gt 0) {
    $exportRuntimeOutputDirectory = Join-Path $resolvedOutputDirectory "ffmpeg-export"
    if (-not (Test-Path -LiteralPath $exportRuntimeOutputDirectory)) {
        throw "Packaged output is missing the ffmpeg-export runtime directory."
    }

    Test-OutputRuntimeIntegrity -DirectoryPath $exportRuntimeOutputDirectory -ExpectedHashes $expectedExportRuntimeFiles
}

$ffmpegToolsOutputDirectory = Join-Path $resolvedOutputDirectory "ffmpeg-tools"
if (Test-Path -LiteralPath $ffmpegToolsOutputDirectory) {
    throw "Packaged output still contains the ffmpeg-tools directory."
}

$testingNotesPath = Join-Path $repoRoot "TESTING_NOTES.md"
if (Test-Path -LiteralPath $testingNotesPath) {
    Copy-Item -LiteralPath $testingNotesPath -Destination (Join-Path $resolvedOutputDirectory "TESTING_NOTES.md") -Force
}

$artifactVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exePath).ProductVersion
if ([string]::IsNullOrWhiteSpace($artifactVersion)) {
    throw "The packaged executable did not report a product version. Update Properties\\AssemblyInfo.cs before building the release test drop."
}

$resolvedArtifactPath = if ([string]::IsNullOrWhiteSpace($ArtifactPath)) {
    Join-Path $repoRoot ("artifacts\FramePlayer-CustomFFmpeg-{0}.zip" -f $artifactVersion)
}
else {
    $ArtifactPath
}

$resolvedArtifactPath = Assert-PathWithin -Path $resolvedArtifactPath -Root (Join-Path $repoRoot "artifacts")
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $resolvedArtifactPath) | Out-Null
if (Test-Path -LiteralPath $resolvedArtifactPath) {
    Remove-Item -LiteralPath $resolvedArtifactPath -Force
}

Compress-Archive -Path (Join-Path $resolvedOutputDirectory "*") -DestinationPath $resolvedArtifactPath -CompressionLevel Optimal

[pscustomobject]@{
    OutputDirectory = $resolvedOutputDirectory
    ExecutablePath = $exePath
    ArtifactPath = $resolvedArtifactPath
    RuntimeFiles = @($expectedRuntimeFiles.Keys | Sort-Object)
    ExportRuntimeFiles = @($expectedExportRuntimeFiles.Keys | Sort-Object)
    ExportToolsFiles = @($expectedExportToolsFiles.Keys | Sort-Object)
    ProductVersion = $artifactVersion
}
