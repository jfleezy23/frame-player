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
    [string[]]$IncludeExtensions = @(".mp4", ".mkv", ".avi", ".wmv", ".m4v")
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
                $extension = $item.Extension.ToLowerInvariant()
                if ($Extensions -notcontains $extension)
                {
                    Write-Warning ("Skipping unsupported manual-test input: {0}" -f $item.FullName)
                    continue
                }

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

function Get-PropertyValue
{
    param(
        $Object,
        [string]$PropertyName,
        $Default = $null
    )

    if ($null -eq $Object)
    {
        return $Default
    }

    $property = $Object.PSObject.Properties[$PropertyName]
    if ($null -eq $property)
    {
        return $Default
    }

    return $property.Value
}

function Get-FirstNonEmptyString
{
    param(
        [object[]]$Values
    )

    foreach ($value in @($Values))
    {
        if (-not [string]::IsNullOrWhiteSpace([string]$value))
        {
            return [string]$value
        }
    }

    return ""
}

function Test-StepBoundaryWarning
{
    param(
        $BackendOutput,
        [string]$OperationName
    )

    foreach ($warning in @(Get-PropertyValue -Object $BackendOutput -PropertyName "Warnings" -Default @()))
    {
        if ([string]$warning -like "$OperationName hit a clip boundary*")
        {
            return $true
        }
    }

    return $false
}

function Get-OperationClassification
{
    param(
        [string]$OperationName,
        $Operation,
        $BackendOutput
    )

    if ($null -eq $Operation)
    {
        return "fail"
    }

    $succeeded = [bool](Get-PropertyValue -Object $Operation -PropertyName "Succeeded" -Default $false)
    if (-not $succeeded)
    {
        if (($OperationName -eq "step-backward" -or $OperationName -eq "step-forward") -and
            (Test-StepBoundaryWarning -BackendOutput $BackendOutput -OperationName $OperationName))
        {
            return "warning"
        }

        return "fail"
    }

    if (($OperationName -eq "seek-to-time" -or $OperationName -eq "seek-to-frame") -and
        -not [bool](Get-PropertyValue -Object $Operation -PropertyName "IsFrameIndexAbsolute" -Default $false))
    {
        return "warning"
    }

    return "pass"
}

function Get-OperationMessage
{
    param(
        [string]$OperationName,
        $Operation,
        $BackendOutput
    )

    if ($null -eq $Operation)
    {
        return "The operation result was missing."
    }

    $classification = Get-OperationClassification -OperationName $OperationName -Operation $Operation -BackendOutput $BackendOutput
    if ($classification -eq "warning")
    {
        if ($OperationName -eq "seek-to-time")
        {
            return "Seek-to-time completed but did not retain absolute frame identity."
        }

        if ($OperationName -eq "seek-to-frame")
        {
            return "Seek-to-frame completed but did not retain absolute frame identity."
        }

        if (($OperationName -eq "step-backward" -or $OperationName -eq "step-forward") -and
            (Test-StepBoundaryWarning -BackendOutput $BackendOutput -OperationName $OperationName))
        {
            return Get-FirstNonEmptyString -Values @(
                foreach ($warning in @(Get-PropertyValue -Object $BackendOutput -PropertyName "Warnings" -Default @()))
                {
                    if ([string]$warning -like "$OperationName hit a clip boundary*")
                    {
                        [string]$warning
                    }
                })
        }
    }

    $errorMessage = Get-PropertyValue -Object $Operation -PropertyName "ErrorMessage" -Default ""
    $operationMessage = Get-PropertyValue -Object $Operation -PropertyName "Message" -Default ""
    $operationNote = Get-PropertyValue -Object $Operation -PropertyName "Note" -Default ""

    return Get-FirstNonEmptyString -Values @(
        $errorMessage,
        $operationMessage,
        $operationNote)
}

function Test-IssueCoveredByOperationRow
{
    param(
        [string]$IssueMessage
    )

    if ([string]::IsNullOrWhiteSpace($IssueMessage))
    {
        return $false
    }

    return $IssueMessage -match '^Seek-to-time did not report absolute frame identity\.$' -or
           $IssueMessage -match '^Seek-to-frame did not retain absolute frame identity\.$' -or
           $IssueMessage -match '^step-backward hit a clip boundary' -or
           $IssueMessage -match '^step-forward hit a clip boundary' -or
           $IssueMessage -match '^open failed:' -or
           $IssueMessage -match '^playback failed:' -or
           $IssueMessage -match '^seek-to-time failed:' -or
           $IssueMessage -match '^seek-to-frame failed:' -or
           $IssueMessage -match '^step-backward failed:' -or
           $IssueMessage -match '^step-forward failed:'
}

function Convert-ToOperationRow
{
    param(
        $FileResult,
        $BackendOutput,
        [string]$OperationName,
        $Operation
    )

    $decode = Get-PropertyValue -Object $Operation -PropertyName "Decode"
    $positionFrameIndex = Get-PropertyValue -Object $Operation -PropertyName "PositionFrameIndex"
    $positionAbsolute = Get-PropertyValue -Object $Operation -PropertyName "PositionAbsolute"
    $frameIndex = Get-PropertyValue -Object $Operation -PropertyName "FrameIndex" -Default $positionFrameIndex
    $isFrameIndexAbsolute = Get-PropertyValue -Object $Operation -PropertyName "IsFrameIndexAbsolute" -Default $positionAbsolute

    return [pscustomobject]@{
        FilePath = $FileResult.FilePath
        FileName = $FileResult.FileName
        BackendName = $BackendOutput.BackendName
        EntryType = "operation"
        Name = $OperationName
        Classification = Get-OperationClassification -OperationName $OperationName -Operation $Operation -BackendOutput $BackendOutput
        Message = Get-OperationMessage -OperationName $OperationName -Operation $Operation -BackendOutput $BackendOutput
        PlanSeekTime = [string]$FileResult.TestPlan.SeekTime
        PlanSeekFrameIndex = $FileResult.TestPlan.SeekFrameIndex
        PlanSeekTimeStrategy = $FileResult.TestPlan.SeekTimeStrategy
        PlanSeekFrameStrategy = $FileResult.TestPlan.SeekFrameStrategy
        PlanReducedPath = $FileResult.TestPlan.ReducedTestPathUsed
        PlanIndexedFrameCount = $FileResult.TestPlan.IndexedFrameCount
        Succeeded = Get-PropertyValue -Object $Operation -PropertyName "Succeeded" -Default $false
        ElapsedMilliseconds = Get-PropertyValue -Object $Operation -PropertyName "ElapsedMilliseconds"
        FrameIndex = $frameIndex
        IsFrameIndexAbsolute = $isFrameIndexAbsolute
        PresentationTime = Get-PropertyValue -Object $Operation -PropertyName "PresentationTime" -Default ""
        UsedGlobalIndex = Get-PropertyValue -Object $Operation -PropertyName "UsedGlobalIndex"
        AnchorStrategy = Get-PropertyValue -Object $Operation -PropertyName "AnchorStrategy" -Default ""
        AnchorFrameIndex = Get-PropertyValue -Object $Operation -PropertyName "AnchorFrameIndex"
        Codec = Get-PropertyValue -Object $Operation -PropertyName "Codec" -Default ""
        Width = Get-PropertyValue -Object $Operation -PropertyName "Width"
        Height = Get-PropertyValue -Object $Operation -PropertyName "Height"
        Duration = Get-PropertyValue -Object $Operation -PropertyName "Duration" -Default ""
        NominalFps = Get-PropertyValue -Object $Operation -PropertyName "NominalFps"
        DecodeBackend = Get-PropertyValue -Object $decode -PropertyName "DecodeBackend" -Default ""
        ActualBackendUsed = Get-PropertyValue -Object $decode -PropertyName "ActualBackendUsed" -Default ""
        GpuActive = Get-PropertyValue -Object $decode -PropertyName "GpuActive"
        GpuStatus = Get-PropertyValue -Object $decode -PropertyName "GpuStatus" -Default ""
        GpuFallbackReason = Get-PropertyValue -Object $decode -PropertyName "GpuFallbackReason" -Default ""
        QueueDepth = Get-PropertyValue -Object $decode -PropertyName "QueueDepth"
        CacheBudgetMiB = Get-PropertyValue -Object $decode -PropertyName "CacheBudgetMiB"
        SessionCacheBudgetMiB = Get-PropertyValue -Object $decode -PropertyName "SessionCacheBudgetMiB"
        BudgetBand = Get-PropertyValue -Object $decode -PropertyName "BudgetBand" -Default ""
        HostResourceClass = Get-PropertyValue -Object $decode -PropertyName "HostResourceClass" -Default ""
        CacheBack = Get-PropertyValue -Object $decode -PropertyName "CacheBack"
        CacheAhead = Get-PropertyValue -Object $decode -PropertyName "CacheAhead"
        MaxCacheBack = Get-PropertyValue -Object $decode -PropertyName "MaxCacheBack"
        MaxCacheAhead = Get-PropertyValue -Object $decode -PropertyName "MaxCacheAhead"
        ApproximateCacheMiB = Get-PropertyValue -Object $decode -PropertyName "ApproximateCacheMiB"
        HwTransferMilliseconds = Get-PropertyValue -Object $decode -PropertyName "HwTransferMilliseconds"
        BgraConversionMilliseconds = Get-PropertyValue -Object $decode -PropertyName "BgraConversionMilliseconds"
        HasAudioStream = Get-PropertyValue -Object $Operation -PropertyName "HasAudioStream"
        AudioPlaybackAvailable = Get-PropertyValue -Object $Operation -PropertyName "AudioPlaybackAvailable"
        AudioPlaybackActive = Get-PropertyValue -Object $Operation -PropertyName "AudioPlaybackActive"
        AudioCodecName = Get-PropertyValue -Object $Operation -PropertyName "AudioCodecName" -Default ""
        AudioErrorMessage = Get-PropertyValue -Object $Operation -PropertyName "AudioErrorMessage" -Default ""
        LastPlaybackUsedAudioClock = Get-PropertyValue -Object $Operation -PropertyName "LastPlaybackUsedAudioClock"
        LastAudioSubmittedBytes = Get-PropertyValue -Object $Operation -PropertyName "LastAudioSubmittedBytes"
        WasCacheHit = Get-PropertyValue -Object $Operation -PropertyName "WasCacheHit"
        RequiredReconstruction = Get-PropertyValue -Object $Operation -PropertyName "RequiredReconstruction"
    }
}

function New-ManualIssueRow
{
    param(
        $FileResult,
        $BackendOutput,
        [string]$EntryType,
        [string]$Name,
        [string]$Classification,
        [string]$Message
    )

    return [pscustomobject]@{
        FilePath = $FileResult.FilePath
        FileName = $FileResult.FileName
        BackendName = $BackendOutput.BackendName
        EntryType = $EntryType
        Name = $Name
        Classification = $Classification
        Message = $Message
        PlanSeekTime = [string]$FileResult.TestPlan.SeekTime
        PlanSeekFrameIndex = $FileResult.TestPlan.SeekFrameIndex
        PlanSeekTimeStrategy = $FileResult.TestPlan.SeekTimeStrategy
        PlanSeekFrameStrategy = $FileResult.TestPlan.SeekFrameStrategy
        PlanReducedPath = $FileResult.TestPlan.ReducedTestPathUsed
        PlanIndexedFrameCount = $FileResult.TestPlan.IndexedFrameCount
        Succeeded = $null
        ElapsedMilliseconds = $null
        FrameIndex = $null
        IsFrameIndexAbsolute = $null
        PresentationTime = ""
        UsedGlobalIndex = $null
        AnchorStrategy = ""
        AnchorFrameIndex = $null
        Codec = ""
        Width = $null
        Height = $null
        Duration = ""
        NominalFps = $null
        DecodeBackend = ""
        ActualBackendUsed = ""
        GpuActive = $null
        GpuStatus = ""
        GpuFallbackReason = ""
        QueueDepth = $null
        CacheBudgetMiB = $null
        SessionCacheBudgetMiB = $null
        BudgetBand = ""
        HostResourceClass = ""
        CacheBack = $null
        CacheAhead = $null
        MaxCacheBack = $null
        MaxCacheAhead = $null
        ApproximateCacheMiB = $null
        HwTransferMilliseconds = $null
        BgraConversionMilliseconds = $null
        HasAudioStream = $null
        AudioPlaybackAvailable = $null
        AudioPlaybackActive = $null
        AudioCodecName = ""
        AudioErrorMessage = ""
        LastPlaybackUsedAudioClock = $null
        LastAudioSubmittedBytes = $null
        WasCacheHit = $null
        RequiredReconstruction = $null
    }
}

function Convert-ToResultRows
{
    param(
        $FileResult
    )

    $rows = New-Object System.Collections.Generic.List[object]

    foreach ($planWarning in @($FileResult.TestPlan.Warnings))
    {
        [void]$rows.Add((New-ManualIssueRow `
            -FileResult $FileResult `
            -BackendOutput ([pscustomobject]@{ BackendName = "custom-ffmpeg" }) `
            -EntryType "plan-advisory" `
            -Name "test-plan" `
            -Classification "advisory" `
            -Message ([string]$planWarning)))
    }

    if (-not [string]::IsNullOrWhiteSpace($FileResult.TestPlan.PreflightError))
    {
        [void]$rows.Add((New-ManualIssueRow `
            -FileResult $FileResult `
            -BackendOutput ([pscustomobject]@{ BackendName = "custom-ffmpeg" }) `
            -EntryType "plan-advisory" `
            -Name "preflight-error" `
            -Classification "advisory" `
            -Message ([string]$FileResult.TestPlan.PreflightError)))
    }

    foreach ($backendOutput in @($FileResult.Backends))
    {
        $operationDefinitions = @(
            @{ Name = "open"; Operation = $backendOutput.Open },
            @{ Name = "playback"; Operation = $backendOutput.Playback },
            @{ Name = "seek-to-time"; Operation = $backendOutput.SeekToTime },
            @{ Name = "seek-to-frame"; Operation = $backendOutput.SeekToFrame },
            @{ Name = "step-backward"; Operation = $backendOutput.StepBackward },
            @{ Name = "step-forward"; Operation = $backendOutput.StepForward }
        )

        foreach ($operationDefinition in $operationDefinitions)
        {
            [void]$rows.Add((Convert-ToOperationRow `
                -FileResult $FileResult `
                -BackendOutput $backendOutput `
                -OperationName $operationDefinition.Name `
                -Operation $operationDefinition.Operation))
        }

        foreach ($warning in @($backendOutput.Warnings))
        {
            if (-not (Test-IssueCoveredByOperationRow -IssueMessage ([string]$warning)))
            {
                [void]$rows.Add((New-ManualIssueRow `
                    -FileResult $FileResult `
                    -BackendOutput $backendOutput `
                    -EntryType "backend-warning" `
                    -Name "backend-warning" `
                    -Classification "warning" `
                    -Message ([string]$warning)))
            }
        }

        foreach ($failure in @($backendOutput.Failures))
        {
            if (-not (Test-IssueCoveredByOperationRow -IssueMessage ([string]$failure)))
            {
                [void]$rows.Add((New-ManualIssueRow `
                    -FileResult $FileResult `
                    -BackendOutput $backendOutput `
                    -EntryType "backend-failure" `
                    -Name "backend-failure" `
                    -Classification "fail" `
                    -Message ([string]$failure)))
            }
        }

        if (-not [string]::IsNullOrWhiteSpace($backendOutput.ScenarioError))
        {
            [void]$rows.Add((New-ManualIssueRow `
                -FileResult $FileResult `
                -BackendOutput $backendOutput `
                -EntryType "scenario-error" `
                -Name "scenario-error" `
                -Classification "fail" `
                -Message ([string]$backendOutput.ScenarioError)))
        }
    }

    return $rows.ToArray()
}

function Get-ResultCounts
{
    param(
        $Rows
    )

    $rowArray = @($Rows)
    return [pscustomobject]@{
        TotalCount = $rowArray.Count
        OperationCount = @($rowArray | Where-Object { $_.EntryType -eq "operation" }).Count
        PassCount = @($rowArray | Where-Object { $_.Classification -eq "pass" }).Count
        WarningCount = @($rowArray | Where-Object { $_.Classification -eq "warning" }).Count
        FailCount = @($rowArray | Where-Object { $_.Classification -eq "fail" }).Count
        AdvisoryCount = @($rowArray | Where-Object { $_.Classification -eq "advisory" }).Count
    }
}

function Get-PerFileOperationStatus
{
    param(
        $ResultRows,
        [string]$FilePath,
        [string]$BackendName,
        [string]$OperationName
    )

    $row = @($ResultRows | Where-Object {
        $_.EntryType -eq "operation" -and
        $_.FilePath -eq $FilePath -and
        $_.BackendName -eq $BackendName -and
        $_.Name -eq $OperationName
    } | Select-Object -First 1)

    if ($row.Count -eq 0)
    {
        return "(missing)"
    }

    return $row[0].Classification
}

function New-MarkdownSummary
{
    param(
        $SessionReport,
        $FileOutputs,
        $ResultRows,
        $ResultCounts
    )

    $lines = New-Object System.Collections.Generic.List[string]
    [void]$lines.Add("# Review Engine Manual Test Summary")
    [void]$lines.Add("")
    [void]$lines.Add("- Generated (UTC): $($SessionReport.GeneratedAtUtc)")
    [void]$lines.Add("- Files tested: $($SessionReport.Summary.FilesTested)")
    [void]$lines.Add("- Backend runs attempted: $($SessionReport.Summary.BackendRunsAttempted)")
    [void]$lines.Add("- Result rows emitted: $($ResultCounts.TotalCount)")
    [void]$lines.Add("- Operation rows emitted: $($ResultCounts.OperationCount)")
    [void]$lines.Add("- Pass / Warning / Fail / Advisory: $($ResultCounts.PassCount) / $($ResultCounts.WarningCount) / $($ResultCounts.FailCount) / $($ResultCounts.AdvisoryCount)")
    [void]$lines.Add("")
    [void]$lines.Add("## Backend Totals")
    [void]$lines.Add("")
    [void]$lines.Add("| Backend | Attempted | Scenario Pass | Scenario Warning | Scenario Fail | Warning Rows | Fail Rows | Advisory Rows |")
    [void]$lines.Add("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |")
    foreach ($backendSummary in $SessionReport.Summary.Backends)
    {
        $backendRows = @($ResultRows | Where-Object { $_.BackendName -eq $backendSummary.BackendName })
        [void]$lines.Add("| $($backendSummary.BackendName) | $($backendSummary.Attempted) | $($backendSummary.PassCount) | $($backendSummary.WarningCount) | $($backendSummary.FailCount) | $(@($backendRows | Where-Object { $_.Classification -eq 'warning' }).Count) | $(@($backendRows | Where-Object { $_.Classification -eq 'fail' }).Count) | $(@($backendRows | Where-Object { $_.Classification -eq 'advisory' }).Count) |")
    }
    [void]$lines.Add("")
    [void]$lines.Add("Scenario columns come from the app's backend summary. Row columns show the granular operation and advisory entries emitted by this report.")

    $operationRows = @($ResultRows | Where-Object { $_.EntryType -eq "operation" })
    [void]$lines.Add("")
    [void]$lines.Add("## Operation Totals")
    [void]$lines.Add("")
    [void]$lines.Add("| Operation | Pass | Warning | Fail |")
    [void]$lines.Add("| --- | ---: | ---: | ---: |")
    foreach ($operationGroup in ($operationRows | Group-Object Name | Sort-Object Name))
    {
        $groupRows = @($operationGroup.Group)
        [void]$lines.Add("| $($operationGroup.Name) | $(@($groupRows | Where-Object { $_.Classification -eq 'pass' }).Count) | $(@($groupRows | Where-Object { $_.Classification -eq 'warning' }).Count) | $(@($groupRows | Where-Object { $_.Classification -eq 'fail' }).Count) |")
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
    [void]$lines.Add("## Per-File Operation Matrix")
    [void]$lines.Add("")
    [void]$lines.Add("| File | Backend | Open | Playback | Seek Time | Seek Frame | Backward | Forward | W/F/A | Highlights |")
    [void]$lines.Add("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |")
    foreach ($fileOutput in $FileOutputs)
    {
        $custom = @($fileOutput.Backends | Where-Object { $_.BackendName -eq "custom-ffmpeg" } | Select-Object -First 1)
        if ($custom.Count -eq 0)
        {
            continue
        }

        $backendName = $custom[0].BackendName
        $fileRows = @($ResultRows | Where-Object { $_.FilePath -eq $fileOutput.FilePath -and $_.BackendName -eq $backendName })
        $warningCount = @($fileRows | Where-Object { $_.Classification -eq "warning" }).Count
        $failCount = @($fileRows | Where-Object { $_.Classification -eq "fail" }).Count
        $advisoryCount = @($fileRows | Where-Object { $_.Classification -eq "advisory" }).Count
        $highlights = if ($fileOutput.ComparisonHighlights.Count -gt 0)
        {
            [string]::Join("; ", $fileOutput.ComparisonHighlights)
        }
        else
        {
            "None"
        }

        [void]$lines.Add("| $($fileOutput.FileName) | $backendName | $(Get-PerFileOperationStatus -ResultRows $ResultRows -FilePath $fileOutput.FilePath -BackendName $backendName -OperationName 'open') | $(Get-PerFileOperationStatus -ResultRows $ResultRows -FilePath $fileOutput.FilePath -BackendName $backendName -OperationName 'playback') | $(Get-PerFileOperationStatus -ResultRows $ResultRows -FilePath $fileOutput.FilePath -BackendName $backendName -OperationName 'seek-to-time') | $(Get-PerFileOperationStatus -ResultRows $ResultRows -FilePath $fileOutput.FilePath -BackendName $backendName -OperationName 'seek-to-frame') | $(Get-PerFileOperationStatus -ResultRows $ResultRows -FilePath $fileOutput.FilePath -BackendName $backendName -OperationName 'step-backward') | $(Get-PerFileOperationStatus -ResultRows $ResultRows -FilePath $fileOutput.FilePath -BackendName $backendName -OperationName 'step-forward') | $warningCount/$failCount/$advisoryCount | $highlights |")
    }

    $issues = @($ResultRows | Where-Object { $_.Classification -eq "warning" -or $_.Classification -eq "fail" } | Sort-Object FileName, BackendName, EntryType, Name)
    if ($issues.Count -gt 0)
    {
        [void]$lines.Add("")
        [void]$lines.Add("## Warnings And Failures")
        [void]$lines.Add("")
        foreach ($issue in $issues)
        {
            [void]$lines.Add("### $($issue.FileName) [$($issue.BackendName)] :: $($issue.Name) [$($issue.Classification)]")
            [void]$lines.Add("")
            [void]$lines.Add("- Entry type: $($issue.EntryType)")
            [void]$lines.Add("- Message: $($issue.Message)")
            if ($issue.FrameIndex -ne $null)
            {
                [void]$lines.Add("- Frame index: $($issue.FrameIndex)")
            }
            if ($issue.IsFrameIndexAbsolute -ne $null)
            {
                [void]$lines.Add("- Absolute frame identity: $($issue.IsFrameIndexAbsolute)")
            }
            if (-not [string]::IsNullOrWhiteSpace($issue.PresentationTime))
            {
                [void]$lines.Add("- Presentation time: $($issue.PresentationTime)")
            }
            if ($issue.ElapsedMilliseconds -ne $null)
            {
                [void]$lines.Add("- Elapsed milliseconds: $($issue.ElapsedMilliseconds)")
            }
            if (-not [string]::IsNullOrWhiteSpace($issue.AnchorStrategy))
            {
                [void]$lines.Add("- Anchor: $($issue.AnchorStrategy)")
            }
            [void]$lines.Add("")
        }
    }

    $advisories = @($ResultRows | Where-Object { $_.Classification -eq "advisory" } | Sort-Object FileName, Name, Message)
    if ($advisories.Count -gt 0)
    {
        [void]$lines.Add("")
        [void]$lines.Add("## Planning Advisories")
        [void]$lines.Add("")
        foreach ($advisory in $advisories)
        {
            [void]$lines.Add("### $($advisory.FileName) :: $($advisory.Name)")
            [void]$lines.Add("")
            [void]$lines.Add("- Message: $($advisory.Message)")
            [void]$lines.Add("- Seek time strategy: $($advisory.PlanSeekTimeStrategy)")
            [void]$lines.Add("- Seek frame strategy: $($advisory.PlanSeekFrameStrategy)")
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
        Convert-ToResultRows -FileResult $fileResult
    }
)
$resultCounts = Get-ResultCounts -Rows $csvRows

$csvRows | Export-Csv -LiteralPath $csvPath -NoTypeInformation -Encoding UTF8
$markdown = New-MarkdownSummary -SessionReport $report -FileOutputs $fileOutputs -ResultRows $csvRows -ResultCounts $resultCounts
Set-Content -LiteralPath $markdownPath -Value $markdown -Encoding UTF8

Write-Host "Manual review-engine test run complete."
Write-Host ("Files tested: {0}" -f $report.Summary.FilesTested)
Write-Host ("Backend runs attempted: {0}" -f $report.Summary.BackendRunsAttempted)
Write-Host ("Result rows emitted: {0}" -f $resultCounts.TotalCount)
Write-Host ("Operation rows emitted: {0}" -f $resultCounts.OperationCount)
Write-Host ("Pass / Warning / Fail / Advisory: {0} / {1} / {2} / {3}" -f $resultCounts.PassCount, $resultCounts.WarningCount, $resultCounts.FailCount, $resultCounts.AdvisoryCount)
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
    ResultRowCount = $resultCounts.TotalCount
    OperationCount = $resultCounts.OperationCount
    PassCount = $resultCounts.PassCount
    WarningCount = $resultCounts.WarningCount
    FailCount = $resultCounts.FailCount
    AdvisoryCount = $resultCounts.AdvisoryCount
    ProcessExitCode = $process.ExitCode
}

if ($process.ExitCode -eq 1)
{
    exit 1
}

if ($resultCounts.FailCount -gt 0)
{
    exit 2
}

exit 0
