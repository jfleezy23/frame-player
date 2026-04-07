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
    [string[]]$IncludeExtensions = @(".mp4", ".mov", ".mkv", ".avi", ".wmv", ".m4v", ".ts")
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

function Get-ScenarioReportForBackend
{
    param(
        $FileResult,
        [string]$BackendName
    )

    return $FileResult.ComparisonReport.CustomFfmpeg
}

function Convert-ToBackendOutput
{
    param(
        $FileResult,
        $BackendResult
    )

    $scenario = Get-ScenarioReportForBackend -FileResult $FileResult -BackendName $BackendResult.BackendName
    if ($null -eq $scenario)
    {
        return [pscustomobject]@{
            BackendName = $BackendResult.BackendName
            Classification = $BackendResult.Classification
            Warnings = @($BackendResult.Warnings)
            Failures = @($BackendResult.Failures)
        }
    }

    return [pscustomobject]@{
        BackendName = $BackendResult.BackendName
        Classification = $BackendResult.Classification
        Warnings = @($BackendResult.Warnings)
        Failures = @($BackendResult.Failures)
        Open = [pscustomobject]@{
            Succeeded = $scenario.OpenResult.Succeeded
            ElapsedMilliseconds = [math]::Round($scenario.OpenResult.ElapsedMilliseconds, 3)
            Note = $scenario.OpenResult.Note
            ErrorMessage = $scenario.OpenResult.ErrorMessage
            Codec = $scenario.OpenResult.MediaInfo.VideoCodecName
            Width = $scenario.OpenResult.MediaInfo.PixelWidth
            Height = $scenario.OpenResult.MediaInfo.PixelHeight
            Duration = [string]$scenario.OpenResult.MediaInfo.Duration
            NominalFps = $scenario.OpenResult.MediaInfo.FramesPerSecond
            HasAudioStream = $scenario.OpenResult.HasAudioStream
            AudioPlaybackAvailable = $scenario.OpenResult.AudioPlaybackAvailable
            AudioPlaybackActive = $scenario.OpenResult.AudioPlaybackActive
            LastPlaybackUsedAudioClock = $scenario.OpenResult.LastPlaybackUsedAudioClock
            LastAudioSubmittedBytes = $scenario.OpenResult.LastAudioSubmittedBytes
            AudioCodecName = $scenario.OpenResult.AudioCodecName
            AudioErrorMessage = $scenario.OpenResult.AudioErrorMessage
            IsGlobalIndexAvailable = $scenario.OpenResult.IsGlobalFrameIndexAvailable
            IndexedFrameCount = $scenario.OpenResult.IndexedFrameCount
            UsedGlobalIndex = $scenario.OpenResult.UsedGlobalIndex
            AnchorStrategy = $scenario.OpenResult.AnchorStrategy
            AnchorFrameIndex = $scenario.OpenResult.AnchorFrameIndex
            PositionFrameIndex = $scenario.OpenResult.Position.FrameIndex
            PositionAbsolute = $scenario.OpenResult.Position.IsFrameIndexAbsolute
        }
        Playback = [pscustomobject]@{
            Succeeded = $scenario.PlaybackResult.Succeeded
            ElapsedMilliseconds = [math]::Round($scenario.PlaybackResult.ElapsedMilliseconds, 3)
            Note = $scenario.PlaybackResult.Note
            ErrorMessage = $scenario.PlaybackResult.ErrorMessage
            FrameIndex = $scenario.PlaybackResult.Position.FrameIndex
            IsFrameIndexAbsolute = $scenario.PlaybackResult.Position.IsFrameIndexAbsolute
            HasAudioStream = $scenario.PlaybackResult.HasAudioStream
            AudioPlaybackAvailable = $scenario.PlaybackResult.AudioPlaybackAvailable
            AudioPlaybackActive = $scenario.PlaybackResult.AudioPlaybackActive
            LastPlaybackUsedAudioClock = $scenario.PlaybackResult.LastPlaybackUsedAudioClock
            LastAudioSubmittedBytes = $scenario.PlaybackResult.LastAudioSubmittedBytes
            AudioCodecName = $scenario.PlaybackResult.AudioCodecName
            AudioErrorMessage = $scenario.PlaybackResult.AudioErrorMessage
        }
        SeekToTime = [pscustomobject]@{
            Succeeded = $scenario.SeekToTimeResult.Succeeded
            ElapsedMilliseconds = [math]::Round($scenario.SeekToTimeResult.ElapsedMilliseconds, 3)
            Note = $scenario.SeekToTimeResult.Note
            ErrorMessage = $scenario.SeekToTimeResult.ErrorMessage
            PresentationTime = [string]$scenario.SeekToTimeResult.Position.PresentationTime
            FrameIndex = $scenario.SeekToTimeResult.Position.FrameIndex
            IsFrameIndexAbsolute = $scenario.SeekToTimeResult.Position.IsFrameIndexAbsolute
            UsedGlobalIndex = $scenario.SeekToTimeResult.UsedGlobalIndex
            AnchorStrategy = $scenario.SeekToTimeResult.AnchorStrategy
            AnchorFrameIndex = $scenario.SeekToTimeResult.AnchorFrameIndex
            LastPlaybackUsedAudioClock = $scenario.SeekToTimeResult.LastPlaybackUsedAudioClock
            LastAudioSubmittedBytes = $scenario.SeekToTimeResult.LastAudioSubmittedBytes
        }
        SeekToFrame = [pscustomobject]@{
            Succeeded = $scenario.SeekToFrameResult.Succeeded
            ElapsedMilliseconds = [math]::Round($scenario.SeekToFrameResult.ElapsedMilliseconds, 3)
            Note = $scenario.SeekToFrameResult.Note
            ErrorMessage = $scenario.SeekToFrameResult.ErrorMessage
            PresentationTime = [string]$scenario.SeekToFrameResult.Position.PresentationTime
            FrameIndex = $scenario.SeekToFrameResult.Position.FrameIndex
            IsFrameIndexAbsolute = $scenario.SeekToFrameResult.Position.IsFrameIndexAbsolute
            UsedGlobalIndex = $scenario.SeekToFrameResult.UsedGlobalIndex
            AnchorStrategy = $scenario.SeekToFrameResult.AnchorStrategy
            AnchorFrameIndex = $scenario.SeekToFrameResult.AnchorFrameIndex
            LastPlaybackUsedAudioClock = $scenario.SeekToFrameResult.LastPlaybackUsedAudioClock
            LastAudioSubmittedBytes = $scenario.SeekToFrameResult.LastAudioSubmittedBytes
        }
        StepBackward = [pscustomobject]@{
            Succeeded = $scenario.BackwardStepResult.StepResult.Success
            ElapsedMilliseconds = [math]::Round($scenario.BackwardStepResult.ElapsedMilliseconds, 3)
            Message = $scenario.BackwardStepResult.StepResult.Message
            FrameIndex = $scenario.BackwardStepResult.StepResult.Position.FrameIndex
            IsFrameIndexAbsolute = $scenario.BackwardStepResult.StepResult.Position.IsFrameIndexAbsolute
            WasCacheHit = $scenario.BackwardStepResult.StepResult.WasCacheHit
            RequiredReconstruction = $scenario.BackwardStepResult.StepResult.RequiredReconstruction
            UsedGlobalIndex = $scenario.BackwardStepResult.UsedGlobalIndex
            AnchorStrategy = $scenario.BackwardStepResult.AnchorStrategy
            AnchorFrameIndex = $scenario.BackwardStepResult.AnchorFrameIndex
            LastPlaybackUsedAudioClock = $scenario.BackwardStepResult.LastPlaybackUsedAudioClock
            LastAudioSubmittedBytes = $scenario.BackwardStepResult.LastAudioSubmittedBytes
        }
        StepForward = [pscustomobject]@{
            Succeeded = $scenario.ForwardStepResult.StepResult.Success
            ElapsedMilliseconds = [math]::Round($scenario.ForwardStepResult.ElapsedMilliseconds, 3)
            Message = $scenario.ForwardStepResult.StepResult.Message
            FrameIndex = $scenario.ForwardStepResult.StepResult.Position.FrameIndex
            IsFrameIndexAbsolute = $scenario.ForwardStepResult.StepResult.Position.IsFrameIndexAbsolute
            WasCacheHit = $scenario.ForwardStepResult.StepResult.WasCacheHit
            RequiredReconstruction = $scenario.ForwardStepResult.StepResult.RequiredReconstruction
            UsedGlobalIndex = $scenario.ForwardStepResult.UsedGlobalIndex
            AnchorStrategy = $scenario.ForwardStepResult.AnchorStrategy
            AnchorFrameIndex = $scenario.ForwardStepResult.AnchorFrameIndex
            LastPlaybackUsedAudioClock = $scenario.ForwardStepResult.LastPlaybackUsedAudioClock
            LastAudioSubmittedBytes = $scenario.ForwardStepResult.LastAudioSubmittedBytes
        }
        ScenarioError = $scenario.ScenarioError
    }
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
        HasAudioStream = $BackendOutput.Open.HasAudioStream
        AudioPlaybackAvailable = $BackendOutput.Open.AudioPlaybackAvailable
        AudioCodecName = $BackendOutput.Open.AudioCodecName
        AudioErrorMessage = $BackendOutput.Open.AudioErrorMessage
        LastPlaybackUsedAudioClock = $BackendOutput.Open.LastPlaybackUsedAudioClock
        LastAudioSubmittedBytes = $BackendOutput.Open.LastAudioSubmittedBytes
        PlaybackUsedAudioClock = $BackendOutput.Playback.LastPlaybackUsedAudioClock
        PlaybackAudioSubmittedBytes = $BackendOutput.Playback.LastAudioSubmittedBytes
        SeekToTimeSucceeded = $BackendOutput.SeekToTime.Succeeded
        SeekToTimeElapsedMilliseconds = $BackendOutput.SeekToTime.ElapsedMilliseconds
        SeekToTimeFrameIndex = $BackendOutput.SeekToTime.FrameIndex
        SeekToTimeAbsolute = $BackendOutput.SeekToTime.IsFrameIndexAbsolute
        SeekToTimeUsedGlobalIndex = $BackendOutput.SeekToTime.UsedGlobalIndex
        SeekToTimeAnchorStrategy = $BackendOutput.SeekToTime.AnchorStrategy
        SeekToTimeAnchorFrameIndex = $BackendOutput.SeekToTime.AnchorFrameIndex
        SeekToFrameSucceeded = $BackendOutput.SeekToFrame.Succeeded
        SeekToFrameElapsedMilliseconds = $BackendOutput.SeekToFrame.ElapsedMilliseconds
        SeekToFrameFrameIndex = $BackendOutput.SeekToFrame.FrameIndex
        SeekToFrameAbsolute = $BackendOutput.SeekToFrame.IsFrameIndexAbsolute
        SeekToFrameUsedGlobalIndex = $BackendOutput.SeekToFrame.UsedGlobalIndex
        SeekToFrameAnchorStrategy = $BackendOutput.SeekToFrame.AnchorStrategy
        SeekToFrameAnchorFrameIndex = $BackendOutput.SeekToFrame.AnchorFrameIndex
        BackwardStepSucceeded = $BackendOutput.StepBackward.Succeeded
        BackwardStepElapsedMilliseconds = $BackendOutput.StepBackward.ElapsedMilliseconds
        BackwardFrameIndex = $BackendOutput.StepBackward.FrameIndex
        BackwardAbsolute = $BackendOutput.StepBackward.IsFrameIndexAbsolute
        BackwardCacheHit = $BackendOutput.StepBackward.WasCacheHit
        BackwardRequiredReconstruction = $BackendOutput.StepBackward.RequiredReconstruction
        BackwardUsedGlobalIndex = $BackendOutput.StepBackward.UsedGlobalIndex
        BackwardAnchorStrategy = $BackendOutput.StepBackward.AnchorStrategy
        BackwardAnchorFrameIndex = $BackendOutput.StepBackward.AnchorFrameIndex
        ForwardStepSucceeded = $BackendOutput.StepForward.Succeeded
        ForwardStepElapsedMilliseconds = $BackendOutput.StepForward.ElapsedMilliseconds
        ForwardFrameIndex = $BackendOutput.StepForward.FrameIndex
        ForwardAbsolute = $BackendOutput.StepForward.IsFrameIndexAbsolute
        ForwardCacheHit = $BackendOutput.StepForward.WasCacheHit
        ForwardRequiredReconstruction = $BackendOutput.StepForward.RequiredReconstruction
        ForwardUsedGlobalIndex = $BackendOutput.StepForward.UsedGlobalIndex
        ForwardAnchorStrategy = $BackendOutput.StepForward.AnchorStrategy
        ForwardAnchorFrameIndex = $BackendOutput.StepForward.AnchorFrameIndex
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
    [void]$lines.Add("- Generated (UTC): $($SessionReport.GeneratedAtUtc.ToString('o'))")
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
    [void]$lines.Add("| File | Custom FFmpeg | Highlights |")
    [void]$lines.Add("| --- | --- | --- |")
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

        [void]$lines.Add("| $($fileOutput.FileName) | $($custom.Classification) | $highlights |")
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

$assemblyPath = Join-Path $projectRoot ("bin\\{0}\\FramePlayer.exe" -f $Configuration)
if (-not (Test-Path -LiteralPath $assemblyPath))
{
    throw "Build output not found at $assemblyPath. Build the project first."
}

$assemblyDirectory = Split-Path -Parent $assemblyPath
[void][System.Reflection.Assembly]::LoadFrom((Join-Path $assemblyDirectory "FFmpeg.AutoGen.dll"))
[void][System.Reflection.Assembly]::LoadFrom($assemblyPath)
[FFmpeg.AutoGen.ffmpeg]::RootPath = $assemblyDirectory

$sessionReport = [FramePlayer.Diagnostics.ReviewEngineManualTestRunner]::RunAsync(
    [string[]]$resolvedFiles,
    [Threading.CancellationToken]::None).GetAwaiter().GetResult()

$fileOutputs = @(
    foreach ($fileResult in $sessionReport.FileResults)
    {
        $backendOutputs = @(
            foreach ($backendResult in $fileResult.BackendResults)
            {
                Convert-ToBackendOutput -FileResult $fileResult -BackendResult $backendResult
            }
        )

        [pscustomobject]@{
            FilePath = $fileResult.FilePath
            FileName = [IO.Path]::GetFileName($fileResult.FilePath)
            TestPlan = [pscustomobject]@{
                SeekTime = [string]$fileResult.TestPlan.SeekTime
                SeekFrameIndex = $fileResult.TestPlan.SeekFrameIndex
                SeekTimeStrategy = $fileResult.TestPlan.SeekTimeStrategy
                SeekFrameStrategy = $fileResult.TestPlan.SeekFrameStrategy
                DurationKnown = $fileResult.TestPlan.DurationKnown
                NominalFpsKnown = $fileResult.TestPlan.NominalFpsKnown
                IndexAvailable = $fileResult.TestPlan.IndexAvailable
                IndexedFrameCount = $fileResult.TestPlan.IndexedFrameCount
                ReducedTestPathUsed = $fileResult.TestPlan.ReducedTestPathUsed
                SequenceSummary = $fileResult.TestPlan.SequenceSummary
                Warnings = @($fileResult.TestPlan.Warnings)
                PreflightError = $fileResult.TestPlan.PreflightError
            }
            ComparisonHighlights = @($fileResult.ComparisonHighlights)
            Backends = $backendOutputs
        }
    }
)

$csvRows = @(
    foreach ($fileResult in $sessionReport.FileResults)
    {
        $backendOutputs = @(
            foreach ($backendResult in $fileResult.BackendResults)
            {
                Convert-ToBackendOutput -FileResult $fileResult -BackendResult $backendResult
            }
        )

        foreach ($backendOutput in $backendOutputs)
        {
            Convert-ToCsvRow -FileResult $fileResult -BackendOutput $backendOutput
        }
    }
)

$summaryObject = [pscustomobject]@{
    FilesTested = $sessionReport.Summary.FilesTested
    BackendRunsAttempted = $sessionReport.Summary.BackendRunsAttempted
    Backends = @(
        foreach ($backendSummary in $sessionReport.Summary.Backends)
        {
            [pscustomobject]@{
                BackendName = $backendSummary.BackendName
                Attempted = $backendSummary.Attempted
                PassCount = $backendSummary.PassCount
                WarningCount = $backendSummary.WarningCount
                FailCount = $backendSummary.FailCount
            }
        }
    )
}

$jsonObject = [pscustomobject]@{
    GeneratedAtUtc = $sessionReport.GeneratedAtUtc.ToString("o")
    InputFiles = @($sessionReport.InputFiles)
    Summary = $summaryObject
    Files = $fileOutputs
}

$outputDirectory = (Resolve-Path -LiteralPath (New-Item -ItemType Directory -Path $Output -Force)).Path
$jsonPath = Join-Path $outputDirectory "review-engine-manual-tests.json"
$csvPath = Join-Path $outputDirectory "review-engine-manual-tests.csv"
$markdownPath = Join-Path $outputDirectory "review-engine-manual-tests-summary.md"

$jsonObject | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $jsonPath -Encoding UTF8
$csvRows | Export-Csv -LiteralPath $csvPath -NoTypeInformation -Encoding UTF8
$markdown = New-MarkdownSummary -SessionReport $sessionReport -FileOutputs $fileOutputs
Set-Content -LiteralPath $markdownPath -Value $markdown -Encoding UTF8

Write-Host "Manual review-engine test run complete."
Write-Host ("Files tested: {0}" -f $sessionReport.Summary.FilesTested)
Write-Host ("Backend runs attempted: {0}" -f $sessionReport.Summary.BackendRunsAttempted)
foreach ($backendSummary in $sessionReport.Summary.Backends)
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
    FilesTested = $sessionReport.Summary.FilesTested
    BackendRunsAttempted = $sessionReport.Summary.BackendRunsAttempted
}
