param(
    [Parameter(Mandatory = $false)]
    [string]$Configuration = "Debug",

    [Parameter(Mandatory = $false)]
    [string]$Platform = "x64"
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $projectRoot "FramePlayer.csproj"

$variants = @(
    [pscustomobject]@{
        Variant = "CustomFfmpeg"
        BuildName = "Frame Player - Custom FFmpeg"
        OutputPath = "bin\CompareCustom\"
        IntermediatePath = "obj\CompareCustom\"
    }
)

$results = @()

foreach ($variant in $variants)
{
    Write-Host ("Building {0}..." -f $variant.BuildName)

    & dotnet build $projectPath `
        -c $Configuration `
        -p:Platform=$Platform `
        -p:FramePlayerVariant=$($variant.Variant) `
        -p:OutputPath=$($variant.OutputPath) `
        -p:IntermediateOutputPath=$($variant.IntermediatePath)

    if ($LASTEXITCODE -ne 0)
    {
        throw ("Build failed for {0}." -f $variant.BuildName)
    }

    $outputDirectory = Join-Path $projectRoot $variant.OutputPath
    $executablePath = Join-Path $outputDirectory ($variant.BuildName + ".exe")
    if (-not (Test-Path -LiteralPath $executablePath))
    {
        throw ("Expected executable was not found: {0}" -f $executablePath)
    }

    $results += [pscustomobject]@{
        BuildName = $variant.BuildName
        Variant = $variant.Variant
        OutputDirectory = (Resolve-Path -LiteralPath $outputDirectory).Path
        ExecutablePath = (Resolve-Path -LiteralPath $executablePath).Path
    }
}

Write-Host ""
Write-Host "Custom FFmpeg comparison build completed successfully."
foreach ($result in $results)
{
    Write-Host ("{0}: {1}" -f $result.BuildName, $result.ExecutablePath)
}

$results
