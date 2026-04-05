param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [ValidateSet("x64")]
    [string]$Platform = "x64"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$ensureRuntimeScript = Join-Path $PSScriptRoot "Ensure-DevRuntime.ps1"
$msbuildPath = "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\amd64\MSBuild.exe"

if (-not (Test-Path $msbuildPath)) {
    throw "MSBuild was not found at '$msbuildPath'. Install Visual Studio Build Tools 2022 with .NET desktop build tools."
}

& $ensureRuntimeScript
& $msbuildPath (Join-Path $repoRoot "Rpcs3VideoPlayer.csproj") /t:Restore,Build /p:Configuration=$Configuration /p:Platform=$Platform
