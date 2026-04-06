param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [ValidateSet("x64")]
    [string]$Platform = "x64"
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

$repoRoot = Split-Path -Parent $PSScriptRoot
$ensureRuntimeScript = Join-Path $PSScriptRoot "Ensure-DevRuntime.ps1"
$msbuildPath = Resolve-MSBuildPath

& $ensureRuntimeScript
& $msbuildPath (Join-Path $repoRoot "FramePlayer.csproj") /t:Restore,Build /p:Configuration=$Configuration /p:Platform=$Platform
