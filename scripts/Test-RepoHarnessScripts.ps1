param(
    [string[]]$ScriptPaths = @(
        "scripts\Build-TestDrop.ps1",
        "scripts\Ensure-DevRuntime.ps1",
        "scripts\Ensure-DevExportTools.ps1",
        "scripts\Run-RegressionSuite.ps1",
        "scripts\Run-ReviewEngine-ManualTests.ps1"
    )
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$parseErrors = New-Object System.Collections.Generic.List[object]

foreach ($relativePath in $ScriptPaths) {
    $resolvedPath = Join-Path $repoRoot $relativePath
    if (-not (Test-Path -LiteralPath $resolvedPath)) {
        throw "Required harness script not found: $relativePath"
    }

    $tokens = $null
    $errors = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile($resolvedPath, [ref]$tokens, [ref]$errors)

    foreach ($error in @($errors)) {
        [void]$parseErrors.Add([pscustomobject]@{
            File = $resolvedPath
            Message = $error.Message
            Line = $error.Extent.StartLineNumber
            Column = $error.Extent.StartColumnNumber
        })
    }
}

if ($parseErrors.Count -gt 0) {
    $details = $parseErrors | ForEach-Object {
        "{0}:{1}:{2} {3}" -f $_.File, $_.Line, $_.Column, $_.Message
    }

    throw "Harness script syntax validation failed.`n$($details -join [Environment]::NewLine)"
}

Write-Host "Repository harness scripts are present and syntactically valid."
