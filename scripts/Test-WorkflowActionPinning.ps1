param(
    [string]$WorkflowsDirectory = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$resolvedWorkflowsDirectory = if ([string]::IsNullOrWhiteSpace($WorkflowsDirectory)) {
    Join-Path $repoRoot ".github\workflows"
}
else {
    $WorkflowsDirectory
}

if (-not (Test-Path -LiteralPath $resolvedWorkflowsDirectory)) {
    throw "Workflow directory not found at '$resolvedWorkflowsDirectory'."
}

$workflowFiles = Get-ChildItem -LiteralPath $resolvedWorkflowsDirectory -File -Include *.yml, *.yaml
$floatingReferences = @()

foreach ($workflowFile in $workflowFiles) {
    $lineNumber = 0
    foreach ($line in Get-Content -LiteralPath $workflowFile.FullName) {
        $lineNumber++

        if ($line -notmatch '^\s*uses:\s*(?<value>\S+)') {
            continue
        }

        $reference = $Matches.value.Trim()
        if ($reference.StartsWith("./", [System.StringComparison]::Ordinal) -or
            $reference.StartsWith("docker://", [System.StringComparison]::OrdinalIgnoreCase)) {
            continue
        }

        if ($reference -match '@[0-9a-fA-F]{40}$') {
            continue
        }

        $floatingReferences += [pscustomobject]@{
            File = $workflowFile.FullName
            Line = $lineNumber
            Reference = $reference
        }
    }
}

if ($floatingReferences.Count -gt 0) {
    $details = $floatingReferences | ForEach-Object {
        "{0}:{1} -> {2}" -f $_.File, $_.Line, $_.Reference
    }

    throw "Floating GitHub Action references are not allowed. Pin each 'uses:' reference to a full commit SHA.`n$($details -join [Environment]::NewLine)"
}

Write-Host "Workflow action pinning check passed for '$resolvedWorkflowsDirectory'."
