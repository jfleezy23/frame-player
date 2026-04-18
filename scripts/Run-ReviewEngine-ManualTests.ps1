param(
    [Parameter(Mandatory = $false, ValueFromRemainingArguments = $true)]
    [string[]]$Path,

    [Parameter(Mandatory = $false)]
    [switch]$Recurse,

    [Parameter(Mandatory = $false)]
    [string]$Output,

    [Parameter(Mandatory = $false)]
    [string]$Configuration = "Debug",

    [Parameter(Mandatory = $false)]
    [string[]]$IncludeExtensions = @(".mp4", ".mov", ".mkv", ".avi", ".wmv", ".m4v")
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $Path -or $Path.Count -eq 0)
{
    $defaultSamplePath = Join-Path $projectRoot "dist\\Frame Player\\sample-test.mp4"
    if (-not (Test-Path -LiteralPath $defaultSamplePath))
    {
        throw "No -Path was provided and the default sample clip was not found."
    }

    $Path = @($defaultSamplePath)
}

if ([string]::IsNullOrWhiteSpace($Output))
{
    $Output = Join-Path $projectRoot "artifacts\\review-engine-manual-tests"
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

function Resolve-ManualTestInputFiles
{
    param(
        [string[]]$InputPaths,
        [string[]]$Extensions,
        [switch]$IncludeSubdirectories
    )

    $resolvedFiles = New-Object System.Collections.Generic.List[string]
    $seen = @{}

    foreach ($rawPath in $InputPaths)
    {
        foreach ($resolvedPath in (Resolve-Path -Path $rawPath))
        {
            $item = Get-Item -LiteralPath $resolvedPath.Path
            if ($item.PSIsContainer)
            {
                $childItems = Get-ChildItem -LiteralPath $item.FullName -File -Recurse:$IncludeSubdirectories
                foreach ($childItem in $childItems)
                {
                    $extension = $childItem.Extension.ToLowerInvariant()
                    if ($Extensions -notcontains $extension)
                    {
                        continue
                    }

                    if (-not $seen.ContainsKey($childItem.FullName))
                    {
                        $seen[$childItem.FullName] = $true
                        [void]$resolvedFiles.Add($childItem.FullName)
                    }
                }
            }
            else
            {
                if (-not $seen.ContainsKey($item.FullName))
                {
                    $seen[$item.FullName] = $true
                    [void]$resolvedFiles.Add($item.FullName)
                }
            }
        }
    }

    return @($resolvedFiles | Sort-Object)
}

function Convert-ToCsvRow
{
    param(
        $FileResult,
        $BackendOutput
    )

    return [pscustomobject]@{
        FilePath = $FileResult.FilePath
        BackendName = $BackendOutput.BackendName
        Classification = $BackendOutput.Classification
        PlanSeekTime = [string]$FileResult.TestPlan.SeekTime
        PlanSeekFrameIndex = $FileResult.TestPlan.SeekFrameIndex
        PlanSeekTimeStrategy = $FileResult.TestPlan.SeekTimeStrategy
        PlanSeekFrameStrategy = $FileResult.TestPlan.SeekFrameStrategy
        PlanReducedPath = $FileResult.TestPlan.ReducedTestPathUsed
        PlanIndexedFrameCount = $FileResult.TestPlan.IndexedFrameCount
        OpenSucceeded = $BackendOutput.Open.Succeeded
        OpenElapsedMilliseconds = $BackendOutput.Open.ElapsedMilliseconds
        PlaybackSucceeded = $BackendOutput.Playback.Succeeded
        PlaybackElapsedMilliseconds = $BackendOutput.Playback.ElapsedMilliseconds
        PlaybackFrameIndex = $BackendOutput.Playback.FrameIndex
        IndexAvailable = $BackendOutput.Open.IsGlobalIndexAvailable
        IndexedFrameCount = $BackendOutput.Open.IndexedFrameCount
        Codec = $BackendOutput.Open.Codec
        Width = $BackendOutput.Open.Width
        Height = $BackendOutput.Open.Height
        Duration = $BackendOutput.Open.Duration
        NominalFps = $BackendOutput.Open.NominalFps
        OpenDecodeBackend = $BackendOutput.Open.Decode.DecodeBackend
        OpenGpuActive = $BackendOutput.Open.Decode.GpuActive
        OpenGpuStatus = $BackendOutput.Open.Decode.GpuStatus
        OpenGpuFallbackReason = $BackendOutput.Open.Decode.GpuFallbackReason
        OpenQueueDepth = $BackendOutput.Open.Decode.QueueDepth
        OpenCacheBudgetMiB = $BackendOutput.Open.Decode.CacheBudgetMiB
        OpenSessionCacheBudgetMiB = $BackendOutput.Open.Decode.SessionCacheBudgetMiB
        OpenBudgetBand = $BackendOutput.Open.Decode.BudgetBand
        OpenHostResourceClass = $BackendOutput.Open.Decode.HostResourceClass
        OpenCacheBack = $BackendOutput.Open.Decode.CacheBack
        OpenCacheAhead = $BackendOutput.Open.Decode.CacheAhead
        OpenMaxCacheBack = $BackendOutput.Open.Decode.MaxCacheBack
        OpenMaxCacheAhead = $BackendOutput.Open.Decode.MaxCacheAhead
        OpenApproximateCacheMiB = $BackendOutput.Open.Decode.ApproximateCacheMiB
        OpenHwTransferMilliseconds = $BackendOutput.Open.Decode.HwTransferMilliseconds
        OpenBgraConversionMilliseconds = $BackendOutput.Open.Decode.BgraConversionMilliseconds
        HasAudioStream = $BackendOutput.Open.HasAudioStream
        AudioPlaybackAvailable = $BackendOutput.Open.AudioPlaybackAvailable
        AudioCodecName = $BackendOutput.Open.AudioCodecName
        AudioErrorMessage = $BackendOutput.Open.AudioErrorMessage
        LastPlaybackUsedAudioClock = $BackendOutput.Open.LastPlaybackUsedAudioClock
        LastAudioSubmittedBytes = $BackendOutput.Open.LastAudioSubmittedBytes
        PlaybackUsedAudioClock = $BackendOutput.Playback.LastPlaybackUsedAudioClock
        PlaybackAudioSubmittedBytes = $BackendOutput.Playback.LastAudioSubmittedBytes
        PlaybackDecodeBackend = $BackendOutput.Playback.Decode.DecodeBackend
        PlaybackGpuActive = $BackendOutput.Playback.Decode.GpuActive
        SeekToTimeSucceeded = $BackendOutput.SeekToTime.Succeeded
        SeekToTimeElapsedMilliseconds = $BackendOutput.SeekToTime.ElapsedMilliseconds
        SeekToTimeFrameIndex = $BackendOutput.SeekToTime.FrameIndex
        SeekToTimeAbsolute = $BackendOutput.SeekToTime.IsFrameIndexAbsolute
        SeekToTimeUsedGlobalIndex = $BackendOutput.SeekToTime.UsedGlobalIndex
        SeekToTimeAnchorStrategy = $BackendOutput.SeekToTime.AnchorStrategy
        SeekToTimeAnchorFrameIndex = $BackendOutput.SeekToTime.AnchorFrameIndex
        SeekToTimeDecodeBackend = $BackendOutput.SeekToTime.Decode.DecodeBackend
        SeekToTimeCacheBack = $BackendOutput.SeekToTime.Decode.CacheBack
        SeekToTimeCacheAhead = $BackendOutput.SeekToTime.Decode.CacheAhead
        SeekToFrameSucceeded = $BackendOutput.SeekToFrame.Succeeded
        SeekToFrameElapsedMilliseconds = $BackendOutput.SeekToFrame.ElapsedMilliseconds
        SeekToFrameFrameIndex = $BackendOutput.SeekToFrame.FrameIndex
        SeekToFrameAbsolute = $BackendOutput.SeekToFrame.IsFrameIndexAbsolute
        SeekToFrameUsedGlobalIndex = $BackendOutput.SeekToFrame.UsedGlobalIndex
        SeekToFrameAnchorStrategy = $BackendOutput.SeekToFrame.AnchorStrategy
        SeekToFrameAnchorFrameIndex = $BackendOutput.SeekToFrame.AnchorFrameIndex
        SeekToFrameDecodeBackend = $BackendOutput.SeekToFrame.Decode.DecodeBackend
        SeekToFrameCacheBack = $BackendOutput.SeekToFrame.Decode.CacheBack
        SeekToFrameCacheAhead = $BackendOutput.SeekToFrame.Decode.CacheAhead
        BackwardStepSucceeded = $BackendOutput.StepBackward.Succeeded
        BackwardStepElapsedMilliseconds = $BackendOutput.StepBackward.ElapsedMilliseconds
        BackwardFrameIndex = $BackendOutput.StepBackward.FrameIndex
        BackwardAbsolute = $BackendOutput.StepBackward.IsFrameIndexAbsolute
        BackwardCacheHit = $BackendOutput.StepBackward.WasCacheHit
        BackwardRequiredReconstruction = $BackendOutput.StepBackward.RequiredReconstruction
        BackwardUsedGlobalIndex = $BackendOutput.StepBackward.UsedGlobalIndex
        BackwardAnchorStrategy = $BackendOutput.StepBackward.AnchorStrategy
        BackwardAnchorFrameIndex = $BackendOutput.StepBackward.AnchorFrameIndex
        BackwardDecodeBackend = $BackendOutput.StepBackward.Decode.DecodeBackend
        BackwardCacheBack = $BackendOutput.StepBackward.Decode.CacheBack
        BackwardCacheAhead = $BackendOutput.StepBackward.Decode.CacheAhead
        ForwardStepSucceeded = $BackendOutput.StepForward.Succeeded
        ForwardStepElapsedMilliseconds = $BackendOutput.StepForward.ElapsedMilliseconds
        ForwardFrameIndex = $BackendOutput.StepForward.FrameIndex
        ForwardAbsolute = $BackendOutput.StepForward.IsFrameIndexAbsolute
        ForwardCacheHit = $BackendOutput.StepForward.WasCacheHit
        ForwardRequiredReconstruction = $BackendOutput.StepForward.RequiredReconstruction
        ForwardUsedGlobalIndex = $BackendOutput.StepForward.UsedGlobalIndex
        ForwardAnchorStrategy = $BackendOutput.StepForward.AnchorStrategy
        ForwardAnchorFrameIndex = $BackendOutput.StepForward.AnchorFrameIndex
        ForwardDecodeBackend = $BackendOutput.StepForward.Decode.DecodeBackend
        ForwardGpuActive = $BackendOutput.StepForward.Decode.GpuActive
        ForwardCacheBack = $BackendOutput.StepForward.Decode.CacheBack
        ForwardCacheAhead = $BackendOutput.StepForward.Decode.CacheAhead
        ForwardMaxCacheAhead = $BackendOutput.StepForward.Decode.MaxCacheAhead
        Warnings = [string]::Join(" | ", @($BackendOutput.Warnings))
        Failures = [string]::Join(" | ", @($BackendOutput.Failures))
        ScenarioError = $BackendOutput.ScenarioError
    }
}

function New-MarkdownSummary
{
    param(
        $SessionReport,
        $FileOutputs
    )

    $lines = New-Object System.Collections.Generic.List[string]
    [void]$lines.Add("# Review Engine Manual Test Summary")
    [void]$lines.Add("")
    [void]$lines.Add("- Generated (UTC): $($SessionReport.GeneratedAtUtc)")
    [void]$lines.Add("- Files tested: $($SessionReport.Summary.FilesTested)")
    [void]$lines.Add("- Backend runs attempted: $($SessionReport.Summary.BackendRunsAttempted)")
    [void]$lines.Add("")
    [void]$lines.Add("## Backend Totals")
    [void]$lines.Add("")
    [void]$lines.Add("| Backend | Attempted | Pass | Warning | Fail |")
    [void]$lines.Add("| --- | ---: | ---: | ---: | ---: |")
    foreach ($backendSummary in $SessionReport.Summary.Backends)
    {
        [void]$lines.Add("| $($backendSummary.BackendName) | $($backendSummary.Attempted) | $($backendSummary.PassCount) | $($backendSummary.WarningCount) | $($backendSummary.FailCount) |")
    }

    [void]$lines.Add("")
    [void]$lines.Add("## Standard Sequence")
    [void]$lines.Add("")
    [void]$lines.Add("- Open file and capture initial metadata / first-frame state.")
    [void]$lines.Add("- Start playback briefly, pause, and capture audio stream/output/clock diagnostics.")
    [void]$lines.Add("- Seek to 25% of duration when duration is known and long enough; otherwise fall back to the start position.")
    [void]$lines.Add("- Seek to a deterministic target frame from the global index midpoint when available; otherwise use a duration/fps estimate or frame 0 fallback.")
    [void]$lines.Add("- Step backward once, then step forward once.")
    [void]$lines.Add("")
    [void]$lines.Add("## Per-File Results")
    [void]$lines.Add("")
    [void]$lines.Add("| File | Custom FFmpeg | Backend | GPU | Open Cache | Forward Step | Highlights |")
    [void]$lines.Add("| --- | --- | --- | --- | --- | --- | --- |")
    foreach ($fileOutput in $FileOutputs)
    {
        $custom = $fileOutput.Backends | Where-Object { $_.BackendName -eq "custom-ffmpeg" } | Select-Object -First 1
        $highlights = if ($fileOutput.ComparisonHighlights.Count -gt 0)
        {
            [string]::Join("; ", $fileOutput.ComparisonHighlights)
        }
        else
        {
            "None"
        }

        $backendText = if ($custom -and $custom.Open -and $custom.Open.Decode)
        {
            $custom.Open.Decode.DecodeBackend
        }
        else
        {
            "(unknown)"
        }
        $gpuText = if ($custom -and $custom.Open -and $custom.Open.Decode)
        {
            if ($custom.Open.Decode.GpuActive) { "active" } else { "inactive" }
        }
        else
        {
            "(unknown)"
        }
        $openCacheText = if ($custom -and $custom.Open -and $custom.Open.Decode)
        {
            "{0} back / {1} ahead (max {2}/{3})" -f `
                $custom.Open.Decode.CacheBack, `
                $custom.Open.Decode.CacheAhead, `
                $custom.Open.Decode.MaxCacheBack, `
                $custom.Open.Decode.MaxCacheAhead
        }
        else
        {
            "(unavailable)"
        }
        $forwardStepText = if ($custom -and $custom.StepForward)
        {
            "cache-hit={0}; reconstruct={1}; cache={2}/{3}" -f `
                $custom.StepForward.WasCacheHit, `
                $custom.StepForward.RequiredReconstruction, `
                $custom.StepForward.Decode.CacheBack, `
                $custom.StepForward.Decode.CacheAhead
        }
        else
        {
            "(unavailable)"
        }

        [void]$lines.Add("| $($fileOutput.FileName) | $($custom.Classification) | $backendText | $gpuText | $openCacheText | $forwardStepText | $highlights |")
    }

    $issues = @(
        foreach ($fileOutput in $FileOutputs)
        {
            foreach ($backend in $fileOutput.Backends)
            {
                if ($backend.Classification -ne "pass")
                {
                    [pscustomobject]@{
                        FileName = $fileOutput.FileName
                        BackendName = $backend.BackendName
                        Classification = $backend.Classification
                        Warnings = @($backend.Warnings)
                        Failures = @($backend.Failures)
                    }
                }
            }
        }
    )

    if ($issues.Count -gt 0)
    {
        [void]$lines.Add("")
        [void]$lines.Add("## Warnings And Failures")
        [void]$lines.Add("")
        foreach ($issue in $issues)
        {
            [void]$lines.Add("### $($issue.FileName) [$($issue.BackendName)] - $($issue.Classification)")
            [void]$lines.Add("")
            foreach ($warning in $issue.Warnings)
            {
                [void]$lines.Add("- Warning: $warning")
            }

            foreach ($failure in $issue.Failures)
            {
                [void]$lines.Add("- Failure: $failure")
            }

            [void]$lines.Add("")
        }
    }

    return [string]::Join([Environment]::NewLine, $lines)
}

$resolvedFiles = Resolve-ManualTestInputFiles -InputPaths $Path -Extensions $normalizedExtensions -IncludeSubdirectories:$Recurse
if ($resolvedFiles.Count -eq 0)
{
    throw "No input videos matched the supplied paths and extension filter."
}

$appHostPath = Join-Path $projectRoot ("bin\\{0}\\FramePlayer.exe" -f $Configuration)
if (-not (Test-Path -LiteralPath $appHostPath))
{
    throw "Build output not found at $appHostPath. Build the project first."
}

$outputDirectory = (Resolve-Path -LiteralPath (New-Item -ItemType Directory -Path $Output -Force)).Path
$jsonPath = Join-Path $outputDirectory "review-engine-manual-tests.json"
$csvPath = Join-Path $outputDirectory "review-engine-manual-tests.csv"
$markdownPath = Join-Path $outputDirectory "review-engine-manual-tests-summary.md"
$requestPath = Join-Path $outputDirectory "review-engine-manual-tests-request.json"
$errorPath = Join-Path $outputDirectory "review-engine-manual-tests-error.txt"

foreach ($artifactPath in @($jsonPath, $csvPath, $markdownPath, $requestPath, $errorPath))
{
    if (Test-Path -LiteralPath $artifactPath)
    {
        Remove-Item -LiteralPath $artifactPath -Force
    }
}

$request = [pscustomobject]@{
    filePaths = [string[]]$resolvedFiles
    reportJsonPath = [string]$jsonPath
    errorPath = [string]$errorPath
}

[System.IO.File]::WriteAllText(
    $requestPath,
    ($request | ConvertTo-Json -Depth 6),
    (New-Object System.Text.UTF8Encoding($false)))

$process = Start-Process -FilePath $appHostPath `
    -ArgumentList ("--run-review-engine-manual-tests-request ""{0}""" -f $requestPath) `
    -Wait `
    -PassThru `
    -WindowStyle Hidden

if (-not (Test-Path -LiteralPath $jsonPath))
{
    $errorText = if (Test-Path -LiteralPath $errorPath)
    {
        Get-Content -LiteralPath $errorPath -Raw
    }
    else
    {
        ""
    }

    if ([string]::IsNullOrWhiteSpace($errorText))
    {
        throw "Manual review-engine child process failed with exit code $($process.ExitCode) before it could write a report."
    }

    throw "Manual review-engine child process failed with exit code $($process.ExitCode). $errorText"
}

$report = Get-Content -LiteralPath $jsonPath -Raw | ConvertFrom-Json
$fileOutputs = @($report.Files)
$backendSummaries = @($report.Summary.Backends)
$csvRows = @(
    foreach ($fileResult in $fileOutputs)
    {
        foreach ($backendOutput in @($fileResult.Backends))
        {
            Convert-ToCsvRow -FileResult $fileResult -BackendOutput $backendOutput
        }
    }
)

$csvRows | Export-Csv -LiteralPath $csvPath -NoTypeInformation -Encoding UTF8
$markdown = New-MarkdownSummary -SessionReport $report -FileOutputs $fileOutputs
Set-Content -LiteralPath $markdownPath -Value $markdown -Encoding UTF8

$passCount = ($backendSummaries | Measure-Object -Property PassCount -Sum).Sum
$warningCount = ($backendSummaries | Measure-Object -Property WarningCount -Sum).Sum
$failCount = ($backendSummaries | Measure-Object -Property FailCount -Sum).Sum
if ($null -eq $passCount) { $passCount = 0 }
if ($null -eq $warningCount) { $warningCount = 0 }
if ($null -eq $failCount) { $failCount = 0 }

Write-Host "Manual review-engine test run complete."
Write-Host ("Files tested: {0}" -f $report.Summary.FilesTested)
Write-Host ("Backend runs attempted: {0}" -f $report.Summary.BackendRunsAttempted)
Write-Host ("Pass / Warning / Fail: {0} / {1} / {2}" -f $passCount, $warningCount, $failCount)
Write-Host ("Process exit code: {0}" -f $process.ExitCode)
foreach ($backendSummary in $backendSummaries)
{
    Write-Host ("{0}: pass={1}, warning={2}, fail={3}" -f $backendSummary.BackendName, $backendSummary.PassCount, $backendSummary.WarningCount, $backendSummary.FailCount)
}

Write-Host ("JSON: {0}" -f $jsonPath)
Write-Host ("CSV: {0}" -f $csvPath)
Write-Host ("Markdown: {0}" -f $markdownPath)

[pscustomobject]@{
    OutputDirectory = $outputDirectory
    JsonPath = $jsonPath
    CsvPath = $csvPath
    MarkdownPath = $markdownPath
    FilesTested = $report.Summary.FilesTested
    BackendRunsAttempted = $report.Summary.BackendRunsAttempted
    PassCount = $passCount
    WarningCount = $warningCount
    FailCount = $failCount
    ProcessExitCode = $process.ExitCode
}

if ($process.ExitCode -eq 1)
{
    exit 1
}

if ($failCount -gt 0)
{
    exit 2
}

exit 0
