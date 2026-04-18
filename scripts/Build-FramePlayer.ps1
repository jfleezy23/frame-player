param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [ValidateSet("x64")]
    [string]$Platform = "x64"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$ensureRuntimeScript = Join-Path $PSScriptRoot "Ensure-DevRuntime.ps1"
$ensureExportRuntimeScript = Join-Path $PSScriptRoot "Ensure-DevExportRuntime.ps1"
$ensureExportToolsScript = Join-Path $PSScriptRoot "Ensure-DevExportTools.ps1"
$projectPath = Join-Path $repoRoot "FramePlayer.csproj"

& $ensureRuntimeScript
if (Test-Path -LiteralPath $ensureExportToolsScript) {
    & $ensureExportToolsScript
}
if (Test-Path -LiteralPath $ensureExportRuntimeScript) {
    & $ensureExportRuntimeScript
}
& dotnet build $projectPath -c $Configuration -p:Platform=$Platform
