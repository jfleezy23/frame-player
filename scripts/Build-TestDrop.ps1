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

function Resolve-MSBuildPath {
    $vswherePath = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswherePath) {
        $resolvedPath = & $vswherePath -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" |
            Select-Object -First 1

        if (-not [string]::IsNullOrWhiteSpace($resolvedPath) -and (Test-Path $resolvedPath)) {
            return $resolvedPath
        }
    }

    $fallbackPath = "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\amd64\MSBuild.exe"
    if (Test-Path $fallbackPath) {
        return $fallbackPath
    }

    throw "MSBuild was not found. Install Visual Studio Build Tools 2022 with .NET desktop build tools."
}

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
$ensureExportToolsScript = Join-Path $PSScriptRoot "Ensure-DevExportTools.ps1"
$msbuildPath = Resolve-MSBuildPath
$runtimeIdentifier = "win-x64"
$manifestPath = Join-Path $repoRoot ("Runtime\manifests\{0}\runtime-manifest.json" -f $runtimeIdentifier)
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$expectedRuntimeFiles = Get-ManifestFileHashes -Manifest $manifest
$exportToolsManifestPath = Join-Path $repoRoot ("Runtime\manifests\{0}\export-tools-manifest.json" -f $runtimeIdentifier)
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

if (Test-Path -LiteralPath $resolvedOutputDirectory) {
    Remove-Item -LiteralPath $resolvedOutputDirectory -Recurse -Force
}

if (Test-Path -LiteralPath $resolvedIntermediateDirectory) {
    Remove-Item -LiteralPath $resolvedIntermediateDirectory -Recurse -Force
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

& $msbuildPath (Join-Path $repoRoot "FramePlayer.csproj") /t:Restore,Build /p:Configuration=$Configuration /p:Platform=$Platform /p:OutputPath=$msbuildOutputPath /p:IntermediateOutputPath=$msbuildIntermediatePath
if ($LASTEXITCODE -ne 0) {
    throw "MSBuild failed while producing the test drop."
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

if ($expectedExportToolsFiles.Count -gt 0 -and (Test-Path -LiteralPath (Join-Path $resolvedOutputDirectory "ffmpeg-tools"))) {
    $exportToolsOutputDirectory = Join-Path $resolvedOutputDirectory "ffmpeg-tools"
    Test-OutputRuntimeIntegrity -DirectoryPath $exportToolsOutputDirectory -ExpectedHashes $expectedExportToolsFiles
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
    ExportToolsFiles = @($expectedExportToolsFiles.Keys | Sort-Object)
    ProductVersion = $artifactVersion
}
