param(
    [Parameter(Mandatory = $false, ValueFromRemainingArguments = $true)]
    [string[]]$Path,

    [Parameter(Mandatory = $false)]
    [switch]$Recurse = $true,

    [Parameter(Mandatory = $false)]
    [string]$CorpusPath = "C:\Projects\Video Test Files",

    [Parameter(Mandatory = $false)]
    [string]$Output,

    [Parameter(Mandatory = $false)]
    [string]$Configuration = "Release",

    [Parameter(Mandatory = $false)]
    [string[]]$IncludeExtensions = @(".mp4", ".mov", ".mkv", ".avi", ".wmv", ".m4v")
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Output))
{
    $Output = Join-Path $projectRoot "artifacts\dual-pane-budget-harness"
}

$normalizedExtensions = @(
    $IncludeExtensions |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        ForEach-Object {
            $extension = $_.Trim()
            if (-not $extension.StartsWith("."))
            {
                $extension = "." + $extension
            }

            $extension.ToLowerInvariant()
        } |
        Sort-Object -Unique
)

function Resolve-HarnessInputFiles
{
    param(
        [string[]]$InputPaths,
        [string]$DefaultCorpusPath,
        [string[]]$Extensions,
        [switch]$IncludeSubdirectories
    )

    $resolvedFiles = New-Object System.Collections.Generic.List[string]
    $seen = @{}

    function Add-ResolvedFile([string]$CandidatePath)
    {
        $fullPath = [System.IO.Path]::GetFullPath($CandidatePath)
        if (-not $seen.ContainsKey($fullPath))
        {
            $seen[$fullPath] = $true
            [void]$resolvedFiles.Add($fullPath)
        }
    }

    function Add-FilesFromPath([string]$RawPath)
    {
        foreach ($resolvedPath in (Resolve-Path -Path $RawPath))
        {
            $item = Get-Item -LiteralPath $resolvedPath.Path
            if ($item.PSIsContainer)
            {
                $childItems = Get-ChildItem -LiteralPath $item.FullName -File -Recurse:$IncludeSubdirectories
                foreach ($childItem in $childItems)
                {
                    if ($Extensions -contains $childItem.Extension.ToLowerInvariant())
                    {
                        Add-ResolvedFile $childItem.FullName
                    }
                }
            }
            else
            {
                Add-ResolvedFile $item.FullName
            }
        }
    }

    if ($InputPaths -and $InputPaths.Count -gt 0)
    {
        foreach ($inputPath in $InputPaths)
        {
            Add-FilesFromPath $inputPath
        }

        return @($resolvedFiles | Sort-Object)
    }

    if (-not (Test-Path -LiteralPath $DefaultCorpusPath))
    {
        throw "The default corpus path '$DefaultCorpusPath' does not exist."
    }

    Add-FilesFromPath $DefaultCorpusPath
    return @($resolvedFiles | Sort-Object)
}

function Add-PairIfPresent
{
    param(
        [System.Collections.Generic.List[object]]$Pairs,
        [hashtable]$FileMap,
        [string]$Label,
        [string]$PrimaryKey,
        [string]$CompareKey,
        [bool]$RequirePaneAlignment
    )

    if ($FileMap.ContainsKey($PrimaryKey) -and $FileMap.ContainsKey($CompareKey))
    {
        [void]$Pairs.Add([pscustomobject]@{
            label = $Label
            primaryPath = $FileMap[$PrimaryKey]
            comparePath = $FileMap[$CompareKey]
            requirePaneAlignment = $RequirePaneAlignment
        })
    }
}

function Build-Pairs
{
    param(
        [string[]]$Files
    )

    $pairs = New-Object System.Collections.Generic.List[object]
    $fileMap = @{}
    foreach ($file in $Files)
    {
        $name = [System.IO.Path]::GetFileName($file)
        $fileMap[$name] = $file
        [void]$pairs.Add([pscustomobject]@{
            label = "self::" + $name
            primaryPath = $file
            comparePath = $file
            requirePaneAlignment = $true
        })
    }

    Add-PairIfPresent -Pairs $pairs -FileMap $fileMap -Label "mixed::hevc-2398-20s.mp4-vs-hevc-2398-20s.wmv" -PrimaryKey "hevc-2398-20s.mp4" -CompareKey "hevc-2398-20s.wmv" -RequirePaneAlignment $false
    Add-PairIfPresent -Pairs $pairs -FileMap $fileMap -Label "mixed::hevc-2398-20s.mkv-vs-hevc-2398-20s.wmv" -PrimaryKey "hevc-2398-20s.mkv" -CompareKey "hevc-2398-20s.wmv" -RequirePaneAlignment $false

    $mixedPairs = @($pairs | Where-Object { -not $_.requirePaneAlignment })
    if ($mixedPairs.Count -eq 0)
    {
        throw "The dual-pane budget harness requires at least one real-media mixed-backend pair. Expected derived HEVC MP4/MKV plus WMV corpus assets were not found."
    }

    return @($pairs.ToArray())
}

function New-MarkdownSummary
{
    param(
        $Report
    )

    $lines = New-Object System.Collections.Generic.List[string]
    [void]$lines.Add("# Dual-Pane Budget Harness Summary")
    [void]$lines.Add("")
    [void]$lines.Add("- Generated (UTC): $($Report.generatedAtUtc)")
    [void]$lines.Add("- Pairs tested: $($Report.summary.pairCount)")
    [void]$lines.Add("- Mixed-backend pairs tested: $((@($Report.hostScenarios[0].pairReports | Where-Object { -not $_.requirePaneAlignment })).Count)")
    [void]$lines.Add("- Failed pairs: $($Report.summary.failedPairCount)")
    [void]$lines.Add("- Checks run: $($Report.summary.checksRun)")
    [void]$lines.Add("- Pass / Fail: $($Report.summary.passCount) / $($Report.summary.failCount)")
    [void]$lines.Add("")
    [void]$lines.Add("| Host | Pair | Align | Primary Backend | Compare Backend | Primary Band | Compare Band | Session MiB | Primary Pane MiB | Compare Pane MiB | Step Window | Fail |")
    [void]$lines.Add("| --- | --- | --- | --- | --- | --- | --- | ---: | ---: | ---: | ---: | ---: |")

    foreach ($hostScenario in $Report.hostScenarios)
    {
        foreach ($pairReport in $hostScenario.pairReports)
        {
            [void]$lines.Add("| $($hostScenario.name) | $($pairReport.label) | $($pairReport.requirePaneAlignment) | $($pairReport.primaryDecode.actualBackendUsed) | $($pairReport.compareDecode.actualBackendUsed) | $($pairReport.primaryDecode.budgetBand) | $($pairReport.compareDecode.budgetBand) | $([math]::Round(($pairReport.primaryDecode.sessionBudgetBytes / 1MB), 1)) | $([math]::Round(($pairReport.primaryDecode.paneBudgetBytes / 1MB), 1)) | $([math]::Round(($pairReport.compareDecode.paneBudgetBytes / 1MB), 1)) | $($pairReport.stepWindow) | $($pairReport.summary.failCount) |")
        }
    }

    $failedChecks = New-Object System.Collections.Generic.List[object]
    foreach ($hostScenario in $Report.hostScenarios)
    {
        foreach ($pairReport in $hostScenario.pairReports)
        {
            foreach ($check in $pairReport.checks)
            {
                if (-not $check.passed)
                {
                    [void]$failedChecks.Add([pscustomobject]@{
                        Host = $hostScenario.name
                        Pair = $pairReport.label
                        Check = $check.name
                        Message = $check.message
                    })
                }
            }
        }
    }

    if ($failedChecks.Count -gt 0)
    {
        [void]$lines.Add("")
        [void]$lines.Add("## Failures")
        [void]$lines.Add("")
        foreach ($failedCheck in $failedChecks)
        {
            [void]$lines.Add("### $($failedCheck.Host) :: $($failedCheck.Pair) :: $($failedCheck.Check)")
            [void]$lines.Add("")
            [void]$lines.Add($failedCheck.Message)
            [void]$lines.Add("")
        }
    }

    return (@($lines) -join [Environment]::NewLine)
}

$resolvedFiles = Resolve-HarnessInputFiles -InputPaths $Path -DefaultCorpusPath $CorpusPath -Extensions $normalizedExtensions -IncludeSubdirectories:$Recurse
if (-not $resolvedFiles -or $resolvedFiles.Count -eq 0)
{
    throw "No supported media files were found for the dual-pane budget harness."
}

$pairs = Build-Pairs -Files $resolvedFiles
$hostScenarios = @(
    [pscustomobject]@{ name = "Business16"; totalPhysicalMemoryBytes = 16GB; availablePhysicalMemoryBytes = 12GB },
    [pscustomobject]@{ name = "Workstation32To64"; totalPhysicalMemoryBytes = 32GB; availablePhysicalMemoryBytes = 24GB },
    [pscustomobject]@{ name = "Workstation128Plus"; totalPhysicalMemoryBytes = 128GB; availablePhysicalMemoryBytes = 96GB }
)

New-Item -ItemType Directory -Path $Output -Force | Out-Null

$requestPath = Join-Path $Output "dual-pane-budget-harness-request.json"
$reportPath = Join-Path $Output "dual-pane-budget-harness-report.json"
$errorPath = Join-Path $Output "dual-pane-budget-harness-error.txt"
$csvPath = Join-Path $Output "dual-pane-budget-harness-results.csv"
$markdownPath = Join-Path $Output "dual-pane-budget-harness-summary.md"

$request = [pscustomobject]@{
    pairs = $pairs
    hostScenarios = $hostScenarios
    reportJsonPath = $reportPath
    errorPath = $errorPath
}
$request | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $requestPath -Encoding UTF8

$exePath = Join-Path $projectRoot "bin\$Configuration\FramePlayer.exe"
if (-not (Test-Path -LiteralPath $exePath))
{
    throw "The expected build output '$exePath' was not found. Build FramePlayer first."
}

$argumentLine = '--run-dual-pane-budget-harness-request "{0}"' -f $requestPath
$process = Start-Process -FilePath $exePath -ArgumentList $argumentLine -PassThru -Wait
if (-not (Test-Path -LiteralPath $reportPath))
{
    if (Test-Path -LiteralPath $errorPath)
    {
        throw (Get-Content -LiteralPath $errorPath -Raw)
    }

    throw "The dual-pane budget harness did not produce a report."
}

$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json

$rows = foreach ($hostScenario in $report.hostScenarios)
{
    foreach ($pairReport in $hostScenario.pairReports)
    {
        [pscustomobject]@{
            HostScenario = $hostScenario.name
            PairLabel = $pairReport.label
            PrimaryFile = $pairReport.primaryPath
            CompareFile = $pairReport.comparePath
            RequirePaneAlignment = $pairReport.requirePaneAlignment
            PrimaryBackend = $pairReport.primaryDecode.actualBackendUsed
            CompareBackend = $pairReport.compareDecode.actualBackendUsed
            PrimaryBand = $pairReport.primaryDecode.budgetBand
            CompareBand = $pairReport.compareDecode.budgetBand
            SessionBudgetMiB = [math]::Round(($pairReport.primaryDecode.sessionBudgetBytes / 1MB), 3)
            PrimaryPaneBudgetMiB = [math]::Round(($pairReport.primaryDecode.paneBudgetBytes / 1MB), 3)
            ComparePaneBudgetMiB = [math]::Round(($pairReport.compareDecode.paneBudgetBytes / 1MB), 3)
            PrimaryPrevious = $pairReport.primaryDecode.maxPreviousCachedFrames
            PrimaryForward = $pairReport.primaryDecode.maxForwardCachedFrames
            ComparePrevious = $pairReport.compareDecode.maxPreviousCachedFrames
            CompareForward = $pairReport.compareDecode.maxForwardCachedFrames
            StepWindow = $pairReport.stepWindow
            PrimaryStepCacheHits = $pairReport.primaryStepMetrics.cacheHitCount
            PrimaryStepReconstructions = $pairReport.primaryStepMetrics.reconstructionCount
            CompareStepCacheHits = $pairReport.compareStepMetrics.cacheHitCount
            CompareStepReconstructions = $pairReport.compareStepMetrics.reconstructionCount
            FailCount = $pairReport.summary.failCount
            ChecksRun = $pairReport.summary.checksRun
        }
    }
}
$rows | Export-Csv -LiteralPath $csvPath -NoTypeInformation -Encoding UTF8
New-MarkdownSummary -Report $report | Set-Content -LiteralPath $markdownPath -Encoding UTF8

Write-Host "Dual-pane budget harness complete."
Write-Host ("Corpus files: {0}" -f $resolvedFiles.Count)
Write-Host ("Pairs tested: {0}" -f $report.summary.pairCount)
Write-Host ("Failed pairs: {0}" -f $report.summary.failedPairCount)
Write-Host ("Checks run: {0}" -f $report.summary.checksRun)
Write-Host ("Pass / Fail: {0} / {1}" -f $report.summary.passCount, $report.summary.failCount)
Write-Host ("JSON: {0}" -f $reportPath)
Write-Host ("CSV: {0}" -f $csvPath)
Write-Host ("Markdown: {0}" -f $markdownPath)

if ($process.ExitCode -ne 0 -or $report.summary.failCount -gt 0)
{
    throw "Dual-pane budget harness reported failures."
}

[pscustomobject]@{
    OutputDirectory = $Output
    JsonPath = $reportPath
    CsvPath = $csvPath
    MarkdownPath = $markdownPath
    PairCount = $report.summary.pairCount
    FailedPairCount = $report.summary.failedPairCount
    ChecksRun = $report.summary.checksRun
    PassCount = $report.summary.passCount
    FailCount = $report.summary.failCount
}
