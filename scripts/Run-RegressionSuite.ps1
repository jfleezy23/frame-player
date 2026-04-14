param(
    [Parameter(Mandatory = $false, ValueFromRemainingArguments = $true)]
    [string[]]$Path,

    [Parameter(Mandatory = $false)]
    [switch]$Recurse,

    [Parameter(Mandatory = $false)]
    [string]$CorpusPath = "C:\Projects\Video Test Files",

    [Parameter(Mandatory = $false)]
    [int]$MaxCorpusFiles = 6,

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
    $Output = Join-Path $projectRoot "artifacts\regression-suite"
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

function Resolve-RegressionInputFiles
{
    param(
        [string[]]$InputPaths,
        [string]$ProjectRoot,
        [string]$DefaultCorpusPath,
        [string[]]$Extensions,
        [switch]$IncludeSubdirectories,
        [int]$CorpusFileLimit
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

    $defaultSamplePath = Join-Path $ProjectRoot "dist\Frame Player\sample-test.mp4"
    if (Test-Path -LiteralPath $defaultSamplePath)
    {
        Add-ResolvedFile $defaultSamplePath
    }

    if (Test-Path -LiteralPath $DefaultCorpusPath)
    {
        $corpusFiles = Get-ChildItem -LiteralPath $DefaultCorpusPath -File |
            Where-Object { $Extensions -contains $_.Extension.ToLowerInvariant() } |
            Sort-Object FullName

        if ($CorpusFileLimit -gt 0)
        {
            $corpusFiles = $corpusFiles | Select-Object -First $CorpusFileLimit
        }

        foreach ($corpusFile in $corpusFiles)
        {
            Add-ResolvedFile $corpusFile.FullName
        }
    }

    return @($resolvedFiles | Sort-Object)
}

function Format-NullableNumber
{
    param(
        [AllowNull()]
        [object]$Value,
        [string]$Format = "0.###",
        [string]$Fallback = ""
    )

    if ($null -eq $Value)
    {
        return $Fallback
    }

    return ("{0:$Format}" -f $Value)
}

function Convert-ToCheckRows
{
    param(
        $Report
    )

    $rows = New-Object System.Collections.Generic.List[object]

    foreach ($check in $Report.Packaging.Checks)
    {
        [void]$rows.Add([pscustomobject]@{
            FilePath = $check.FilePath
            FileName = "(packaging)"
            Scope = $check.Scope
            Category = $check.Category
            Name = $check.Name
            Classification = $check.Classification
            Message = $check.Message
            ExpectedFrameIndex = $check.ExpectedFrameIndex
            ActualFrameIndex = $check.ActualFrameIndex
            ExpectedDisplayedFrame = $check.ExpectedDisplayedFrame
            ActualDisplayedFrame = $check.ActualDisplayedFrame
            RequestedTime = $check.RequestedTime
            ActualTime = $check.ActualTime
            SliderValueSeconds = $check.SliderValueSeconds
            SliderMaximumSeconds = $check.SliderMaximumSeconds
            ElapsedMilliseconds = $check.ElapsedMilliseconds
            IndexReady = $check.IndexReady
            UsedGlobalIndex = $check.UsedGlobalIndex
            CacheHit = $check.CacheHit
            RequiredReconstruction = $check.RequiredReconstruction
            VideoCodec = ""
            FramesPerSecond = ""
            HasAudioStream = ""
            DecodeBackend = ""
            ActualBackendUsed = ""
            GpuActive = ""
            BudgetBand = ""
            HostResourceClass = ""
            SessionBudgetMiB = ""
            PaneBudgetMiB = ""
            ConfiguredCacheBack = ""
            ConfiguredCacheAhead = ""
            ObservedCacheBack = ""
            ObservedCacheAhead = ""
            ForwardCacheHits = ""
            ForwardReconstructions = ""
            ForwardCacheHitRate = ""
            BackwardCacheHits = ""
            BackwardReconstructions = ""
            HwTransferMilliseconds = ""
            BgraConversionMilliseconds = ""
        })
    }

    foreach ($fileResult in $Report.FileResults)
    {
        $checks = @($fileResult.EngineChecks) + @($fileResult.UiChecks)
        foreach ($check in $checks)
        {
            [void]$rows.Add([pscustomobject]@{
                FilePath = $check.FilePath
                FileName = $fileResult.FileName
                Scope = $check.Scope
                Category = $check.Category
                Name = $check.Name
                Classification = $check.Classification
                Message = $check.Message
                ExpectedFrameIndex = $check.ExpectedFrameIndex
                ActualFrameIndex = $check.ActualFrameIndex
                ExpectedDisplayedFrame = $check.ExpectedDisplayedFrame
                ActualDisplayedFrame = $check.ActualDisplayedFrame
                RequestedTime = $check.RequestedTime
                ActualTime = $check.ActualTime
                SliderValueSeconds = $check.SliderValueSeconds
                SliderMaximumSeconds = $check.SliderMaximumSeconds
                ElapsedMilliseconds = $check.ElapsedMilliseconds
                IndexReady = $check.IndexReady
                UsedGlobalIndex = $check.UsedGlobalIndex
                CacheHit = $check.CacheHit
                RequiredReconstruction = $check.RequiredReconstruction
                VideoCodec = $fileResult.MediaProfile.VideoCodecName
                FramesPerSecond = $fileResult.MediaProfile.FramesPerSecond
                HasAudioStream = $fileResult.MediaProfile.HasAudioStream
                DecodeBackend = $fileResult.DecodeProfile.ActiveDecodeBackend
                ActualBackendUsed = $fileResult.DecodeProfile.ActualBackendUsed
                GpuActive = $fileResult.DecodeProfile.IsGpuActive
                BudgetBand = $fileResult.DecodeProfile.BudgetBand
                HostResourceClass = $fileResult.DecodeProfile.HostResourceClass
                SessionBudgetMiB = if ($fileResult.DecodeProfile)
                {
                    [math]::Round(($fileResult.DecodeProfile.SessionDecodedFrameCacheBudgetBytes / 1MB), 3)
                }
                else
                {
                    ""
                }
                PaneBudgetMiB = if ($fileResult.DecodeProfile)
                {
                    [math]::Round(($fileResult.DecodeProfile.DecodedFrameCacheBudgetBytes / 1MB), 3)
                }
                else
                {
                    ""
                }
                ConfiguredCacheBack = $fileResult.DecodeProfile.ConfiguredPreviousCachedFrames
                ConfiguredCacheAhead = $fileResult.DecodeProfile.ConfiguredForwardCachedFrames
                ObservedCacheBack = $fileResult.DecodeProfile.ObservedPreviousCachedFrames
                ObservedCacheAhead = $fileResult.DecodeProfile.ObservedForwardCachedFrames
                ForwardCacheHits = $fileResult.DecodeProfile.ForwardStepCacheHits
                ForwardReconstructions = $fileResult.DecodeProfile.ForwardStepReconstructionCount
                ForwardCacheHitRate = if ($fileResult.DecodeProfile)
                {
                    [math]::Round($fileResult.DecodeProfile.ForwardStepCacheHitRate, 3)
                }
                else
                {
                    ""
                }
                BackwardCacheHits = $fileResult.DecodeProfile.BackwardStepCacheHits
                BackwardReconstructions = $fileResult.DecodeProfile.BackwardStepReconstructionCount
                HwTransferMilliseconds = if ($fileResult.DecodeProfile)
                {
                    [math]::Round($fileResult.DecodeProfile.HardwareFrameTransferMilliseconds, 3)
                }
                else
                {
                    ""
                }
                BgraConversionMilliseconds = if ($fileResult.DecodeProfile)
                {
                    [math]::Round($fileResult.DecodeProfile.BgraConversionMilliseconds, 3)
                }
                else
                {
                    ""
                }
            })
        }
    }

    return $rows.ToArray()
}

function New-MarkdownSummary
{
    param(
        $Report
    )

    $lines = New-Object System.Collections.Generic.List[string]
    [void]$lines.Add("# Frame Player Regression Suite Summary")
    [void]$lines.Add("")
    [void]$lines.Add("- Generated (UTC): $($Report.GeneratedAtUtc)")
    [void]$lines.Add("- Files tested: $($Report.Summary.FilesTested)")
    [void]$lines.Add("- Checks run: $($Report.Summary.ChecksRun)")
    [void]$lines.Add("- Pass / Warning / Fail: $($Report.Summary.PassCount) / $($Report.Summary.WarningCount) / $($Report.Summary.FailCount)")
    [void]$lines.Add("")
    [void]$lines.Add("## Packaging")
    [void]$lines.Add("")
    [void]$lines.Add("- Output directory: $($Report.Packaging.OutputDirectory)")
    [void]$lines.Add("- Artifact: $($Report.Packaging.ArtifactPath)")
    [void]$lines.Add("- Classification: $($Report.Packaging.Classification)")
    [void]$lines.Add("- Expected runtime DLLs: $((@($Report.Packaging.ExpectedRuntimeFiles) | ForEach-Object { [string]$_ }) -join ', ')")
    if ($Report.Packaging.MissingRuntimeFiles.Count -gt 0)
    {
        [void]$lines.Add("- Missing runtime DLLs: $((@($Report.Packaging.MissingRuntimeFiles) | ForEach-Object { [string]$_ }) -join ', ')")
    }
    if ($Report.Packaging.StaleRuntimeFiles.Count -gt 0)
    {
        [void]$lines.Add("- Stale runtime DLLs: $((@($Report.Packaging.StaleRuntimeFiles) | ForEach-Object { [string]$_ }) -join ', ')")
    }

    [void]$lines.Add("")
    [void]$lines.Add("## Per-File Totals")
    [void]$lines.Add("")
    [void]$lines.Add("| File | Codec | FPS | Audio | Pass | Warning | Fail |")
    [void]$lines.Add("| --- | --- | ---: | --- | ---: | ---: | ---: |")

    foreach ($fileResult in $Report.FileResults)
    {
        $checks = @($fileResult.EngineChecks) + @($fileResult.UiChecks)
        $passCount = @($checks | Where-Object { $_.Classification -eq "pass" }).Count
        $warningCount = @($checks | Where-Object { $_.Classification -eq "warning" }).Count
        $failCount = @($checks | Where-Object { $_.Classification -eq "fail" }).Count
        [void]$lines.Add("| $($fileResult.FileName) | $($fileResult.MediaProfile.VideoCodecName) | $(Format-NullableNumber -Value $fileResult.MediaProfile.FramesPerSecond -Format '0.###' -Fallback '0') | $($fileResult.MediaProfile.HasAudioStream) | $passCount | $warningCount | $failCount |")
    }

    [void]$lines.Add("")
    [void]$lines.Add("## Decode Proof")
    [void]$lines.Add("")
    [void]$lines.Add("| File | Backend | Band | Host | GPU | Queue | Session / Pane Budget | Configured Cache | Forward Hits / Recon / Rate | Backward Hits / Recon | Copy / Convert ms |")
    [void]$lines.Add("| --- | --- | --- | --- | --- | ---: | --- | --- | --- | --- | --- |")
    foreach ($fileResult in $Report.FileResults)
    {
        $decodeProfile = $fileResult.DecodeProfile
        $backendText = if ($decodeProfile -and -not [string]::IsNullOrWhiteSpace($decodeProfile.ActiveDecodeBackend))
        {
            $decodeProfile.ActiveDecodeBackend
        }
        else
        {
            "(unknown)"
        }
        $gpuText = if ($decodeProfile)
        {
            if ($decodeProfile.IsGpuActive) { "active" } else { "inactive" }
        }
        else
        {
            "(unknown)"
        }
        $queueDepth = if ($decodeProfile) { $decodeProfile.OperationalQueueDepth } else { 0 }
        $budgetBand = if ($decodeProfile -and -not [string]::IsNullOrWhiteSpace($decodeProfile.BudgetBand))
        {
            $decodeProfile.BudgetBand
        }
        else
        {
            "(unknown)"
        }
        $hostClass = if ($decodeProfile -and -not [string]::IsNullOrWhiteSpace($decodeProfile.HostResourceClass))
        {
            $decodeProfile.HostResourceClass
        }
        else
        {
            "(unknown)"
        }
        $budgetText = if ($decodeProfile)
        {
            ("{0:0.0} / {1:0.0} MiB" -f ($decodeProfile.SessionDecodedFrameCacheBudgetBytes / 1MB), ($decodeProfile.DecodedFrameCacheBudgetBytes / 1MB))
        }
        else
        {
            "0.0 / 0.0 MiB"
        }
        $configuredCache = if ($decodeProfile)
        {
            "$($decodeProfile.ConfiguredPreviousCachedFrames) back / $($decodeProfile.ConfiguredForwardCachedFrames) ahead"
        }
        else
        {
            "0 back / 0 ahead"
        }
        $observedCache = if ($decodeProfile)
        {
            "$($decodeProfile.ObservedPreviousCachedFrames) back / $($decodeProfile.ObservedForwardCachedFrames) ahead"
        }
        else
        {
            "0 back / 0 ahead"
        }
        $forwardStats = if ($decodeProfile)
        {
            "$($decodeProfile.ForwardStepCacheHits) / $($decodeProfile.ForwardStepReconstructionCount) / $(Format-NullableNumber -Value $decodeProfile.ForwardStepCacheHitRate -Format '0.###' -Fallback '0')"
        }
        else
        {
            "0 / 0 / 0"
        }
        $backwardStats = if ($decodeProfile)
        {
            "$($decodeProfile.BackwardStepCacheHits) / $($decodeProfile.BackwardStepReconstructionCount)"
        }
        else
        {
            "0 / 0"
        }
        $copyStats = if ($decodeProfile)
        {
            ("{0:0.###} / {1:0.###}" -f $decodeProfile.HardwareFrameTransferMilliseconds, $decodeProfile.BgraConversionMilliseconds)
        }
        else
        {
            "0.000 / 0.000"
        }

        [void]$lines.Add("| $($fileResult.FileName) | $backendText | $budgetBand | $hostClass | $gpuText | $queueDepth | $budgetText | $configuredCache | $forwardStats | $backwardStats | $copyStats |")
    }

    $issues = New-Object System.Collections.Generic.List[object]
    foreach ($check in $Report.Packaging.Checks)
    {
        if ($check.Classification -ne "pass")
        {
            [void]$issues.Add([pscustomobject]@{
                Label = "(packaging)"
                Check = $check
            })
        }
    }

    foreach ($fileResult in $Report.FileResults)
    {
        foreach ($check in (@($fileResult.EngineChecks) + @($fileResult.UiChecks)))
        {
            if ($check.Classification -ne "pass")
            {
                [void]$issues.Add([pscustomobject]@{
                    Label = $fileResult.FileName
                    Check = $check
                })
            }
        }
    }

    if ($issues.Count -gt 0)
    {
        [void]$lines.Add("")
        [void]$lines.Add("## Warnings And Failures")
        [void]$lines.Add("")
        foreach ($issue in $issues)
        {
            [void]$lines.Add("### $($issue.Label) :: $($issue.Check.Name) [$($issue.Check.Classification)]")
            [void]$lines.Add("")
            [void]$lines.Add("- Scope: $($issue.Check.Scope)")
            [void]$lines.Add("- Category: $($issue.Check.Category)")
            [void]$lines.Add("- Message: $($issue.Check.Message)")
            if ($issue.Check.ExpectedFrameIndex -ne $null -or $issue.Check.ActualFrameIndex -ne $null)
            {
                [void]$lines.Add("- Frame expected / actual: $($issue.Check.ExpectedFrameIndex) / $($issue.Check.ActualFrameIndex)")
            }
            if ($issue.Check.ExpectedDisplayedFrame -ne $null -or $issue.Check.ActualDisplayedFrame -ne $null)
            {
                [void]$lines.Add("- Displayed frame expected / actual: $($issue.Check.ExpectedDisplayedFrame) / $($issue.Check.ActualDisplayedFrame)")
            }
            if (-not [string]::IsNullOrWhiteSpace($issue.Check.RequestedTime) -or -not [string]::IsNullOrWhiteSpace($issue.Check.ActualTime))
            {
                [void]$lines.Add("- Requested / actual time: $($issue.Check.RequestedTime) / $($issue.Check.ActualTime)")
            }
            [void]$lines.Add("")
        }
    }

    return (@($lines) -join [Environment]::NewLine)
}

function New-EmptyMediaProfile
{
    return [pscustomobject]@{
        VideoCodecName = ""
        PixelWidth = 0
        PixelHeight = 0
        Duration = ""
        FramesPerSecond = 0.0
        HasAudioStream = $false
        IsAudioPlaybackAvailable = $false
        AudioCodecName = ""
        AudioSampleRate = 0
        AudioChannelCount = 0
    }
}

function New-EmptyMetrics
{
    return [pscustomobject]@{
        OpenMilliseconds = 0.0
        PreIndexSeekMilliseconds = 0.0
        IndexReadyMilliseconds = 0.0
        IndexedSeekMilliseconds = 0.0
        PlaybackMilliseconds = 0.0
        ReopenMilliseconds = 0.0
        UiOpenMilliseconds = 0.0
        UiPreIndexClickMilliseconds = 0.0
        UiClickSeekMilliseconds = 0.0
        UiDragSeekMilliseconds = 0.0
        UiEndSeekMilliseconds = 0.0
        UiIndexReadyMilliseconds = 0.0
        MaxObservedPreviousCachedFrames = 0
        MaxObservedForwardCachedFrames = 0
        MaxObservedApproximateCacheBytes = 0
        BackwardStepCacheHits = 0
        BackwardStepReconstructionCount = 0
        ForwardStepCacheHits = 0
        ForwardStepReconstructionCount = 0
        LastObservedCacheRefillMilliseconds = 0.0
        LastObservedCacheRefillReason = ""
        LastObservedCacheRefillMode = ""
    }
}

function New-CheckResult
{
    param(
        [string]$FilePath,
        [string]$Scope,
        [string]$Category,
        [string]$Name,
        [string]$Classification,
        [string]$Message
    )

    return [pscustomobject]@{
        FilePath = $FilePath
        Scope = $Scope
        Category = $Category
        Name = $Name
        Classification = $Classification
        Message = $Message
        ExpectedFrameIndex = $null
        ActualFrameIndex = $null
        ExpectedDisplayedFrame = $null
        ActualDisplayedFrame = $null
        RequestedTime = ""
        ActualTime = ""
        SliderValueSeconds = $null
        SliderMaximumSeconds = $null
        ElapsedMilliseconds = $null
        IndexReady = $null
        UsedGlobalIndex = $null
        CacheHit = $null
        RequiredReconstruction = $null
    }
}

function New-FileCrashResult
{
    param(
        [string]$FilePath,
        [int]$ExitCode,
        [string]$ErrorText,
        [string]$TracePath
    )

    $traceSummary = ""
    if (Test-Path -LiteralPath $TracePath)
    {
        $traceSummary = (Get-Content -LiteralPath $TracePath | Select-Object -Last 5) -join " | "
    }

    $message = if (-not [string]::IsNullOrWhiteSpace($ErrorText))
    {
        "Regression child process failed with exit code $ExitCode. $ErrorText"
    }
    elseif (-not [string]::IsNullOrWhiteSpace($traceSummary))
    {
        "Regression child process failed with exit code $ExitCode. Last trace: $traceSummary"
    }
    else
    {
        "Regression child process failed with exit code $ExitCode before it could write a report."
    }

    return [pscustomobject]@{
        FilePath = $FilePath
        FileName = [System.IO.Path]::GetFileName($FilePath)
        MediaProfile = New-EmptyMediaProfile
        EngineChecks = @(
            (New-CheckResult -FilePath $FilePath -Scope "process" -Category "lifecycle" -Name "regression-child-process" -Classification "fail" -Message $message)
        )
        UiChecks = @()
        EngineMetrics = New-EmptyMetrics
        UiMetrics = New-EmptyMetrics
        Notes = @(
            "ProcessExitCode=$ExitCode"
            $(if (-not [string]::IsNullOrWhiteSpace($traceSummary)) { "TraceTail=$traceSummary" })
        ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    }
}

function New-PackagingFallback
{
    param(
        [string]$OutputDirectory,
        [string]$ArtifactPath
    )

    return [pscustomobject]@{
        OutputDirectory = $OutputDirectory
        ArtifactPath = $ArtifactPath
        ExpectedRuntimeFiles = @()
        PresentRuntimeFiles = @()
        MissingRuntimeFiles = @()
        StaleRuntimeFiles = @()
        Checks = @(
            (New-CheckResult -FilePath "" -Scope "packaging" -Category "lifecycle" -Name "packaging-validation-unavailable" -Classification "fail" -Message "No regression child process produced a report, so packaging validation results were unavailable.")
        )
        Classification = "fail"
    }
}

function New-RegressionSummary
{
    param(
        $Packaging,
        $FileResults
    )

    $checks = New-Object System.Collections.Generic.List[object]
    foreach ($check in @($Packaging.Checks))
    {
        [void]$checks.Add($check)
    }

    foreach ($fileResult in @($FileResults))
    {
        foreach ($check in (@($fileResult.EngineChecks) + @($fileResult.UiChecks)))
        {
            [void]$checks.Add($check)
        }
    }

    return [pscustomobject]@{
        FilesTested = @($FileResults).Count
        ChecksRun = $checks.Count
        PassCount = @($checks | Where-Object { $_.Classification -eq "pass" }).Count
        WarningCount = @($checks | Where-Object { $_.Classification -eq "warning" }).Count
        FailCount = @($checks | Where-Object { $_.Classification -eq "fail" }).Count
    }
}

$resolvedFiles = @(Resolve-RegressionInputFiles `
    -InputPaths $Path `
    -ProjectRoot $projectRoot `
    -DefaultCorpusPath $CorpusPath `
    -Extensions $normalizedExtensions `
    -IncludeSubdirectories:$Recurse `
    -CorpusFileLimit $MaxCorpusFiles)

if ($resolvedFiles.Count -eq 0)
{
    throw "No input videos were found for the regression suite."
}

$outputDirectory = (Resolve-Path -LiteralPath (New-Item -ItemType Directory -Path $Output -Force)).Path
$regressionArtifactDirectory = Join-Path $projectRoot "artifacts\regression-builds"
New-Item -ItemType Directory -Force -Path $regressionArtifactDirectory | Out-Null
$regressionArtifactPath = Join-Path $regressionArtifactDirectory ("FramePlayer-RegressionBuild-{0}.zip" -f ([Guid]::NewGuid().ToString("N")))

$buildScript = Join-Path $PSScriptRoot "Build-TestDrop.ps1"
$buildResult = & $buildScript -Configuration $Configuration -ArtifactPath $regressionArtifactPath -RequireExportTools

$assemblyPath = $buildResult.ExecutablePath
$manifestPath = Join-Path $projectRoot "Runtime\runtime-manifest.json"
$csvPath = Join-Path $outputDirectory "regression-suite-checks.csv"
$markdownPath = Join-Path $outputDirectory "regression-suite-summary.md"
$fileReports = New-Object System.Collections.Generic.List[object]
$packagingReport = $null
$processExitCodes = New-Object System.Collections.Generic.List[int]

for ($index = 0; $index -lt $resolvedFiles.Count; $index++)
{
    $filePath = $resolvedFiles[$index]
    $slugBase = [System.IO.Path]::GetFileNameWithoutExtension($filePath)
    $slugBase = ($slugBase -replace '[^A-Za-z0-9._-]+', '_').Trim('_')
    if ([string]::IsNullOrWhiteSpace($slugBase))
    {
        $slugBase = "file"
    }

    $fileSlug = "{0:D2}-{1}" -f ($index + 1), $slugBase
    $fileOutputDirectory = Join-Path $outputDirectory $fileSlug
    New-Item -ItemType Directory -Force -Path $fileOutputDirectory | Out-Null

    $jsonPath = Join-Path $fileOutputDirectory "regression-suite-report.json"
    $requestPath = Join-Path $fileOutputDirectory "regression-suite-request.json"
    $errorPath = Join-Path $fileOutputDirectory "regression-suite-error.txt"
    $tracePath = Join-Path $fileOutputDirectory "regression-suite-trace.log"

    foreach ($artifactPath in @($jsonPath, $requestPath, $errorPath, $tracePath))
    {
        if (Test-Path -LiteralPath $artifactPath)
        {
            Remove-Item -LiteralPath $artifactPath -Force
        }
    }

    $request = [pscustomobject]@{
        filePaths = [string[]]@($filePath)
        packagedOutputDirectory = [string]$buildResult.OutputDirectory
        packagedArtifactPath = [string]$buildResult.ArtifactPath
        runtimeManifestPath = [string]$manifestPath
        reportJsonPath = [string]$jsonPath
        errorPath = [string]$errorPath
        tracePath = [string]$tracePath
    }
    [System.IO.File]::WriteAllText(
        $requestPath,
        ($request | ConvertTo-Json -Depth 6),
        (New-Object System.Text.UTF8Encoding($false)))

    $process = Start-Process -FilePath $assemblyPath `
        -ArgumentList ("--run-regression-suite-request ""{0}""" -f $requestPath) `
        -Wait `
        -PassThru `
        -WindowStyle Hidden
    [void]$processExitCodes.Add($process.ExitCode)

    if (Test-Path -LiteralPath $jsonPath)
    {
        $singleReport = Get-Content -LiteralPath $jsonPath -Raw | ConvertFrom-Json
        if ($null -eq $packagingReport -and $null -ne $singleReport.Packaging)
        {
            $packagingReport = $singleReport.Packaging
        }

        foreach ($fileResult in @($singleReport.FileResults))
        {
            [void]$fileReports.Add($fileResult)
        }

        continue
    }

    $errorText = if (Test-Path -LiteralPath $errorPath)
    {
        Get-Content -LiteralPath $errorPath -Raw
    }
    else
    {
        ""
    }

    [void]$fileReports.Add((New-FileCrashResult -FilePath $filePath -ExitCode $process.ExitCode -ErrorText $errorText -TracePath $tracePath))
}

if ($null -eq $packagingReport)
{
    $packagingReport = New-PackagingFallback -OutputDirectory $buildResult.OutputDirectory -ArtifactPath $buildResult.ArtifactPath
}

Write-Host "Assembling combined regression report..."
$fileResultsArray = $fileReports.ToArray()
$processExitCodesArray = $processExitCodes.ToArray()

$report = [pscustomobject]@{
    GeneratedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    PackagedOutputDirectory = $buildResult.OutputDirectory
    PackagedArtifactPath = $buildResult.ArtifactPath
    Packaging = $packagingReport
    FileResults = $fileResultsArray
    Summary = New-RegressionSummary -Packaging $packagingReport -FileResults $fileResultsArray
}

$jsonPath = Join-Path $outputDirectory "regression-suite-report.json"
Write-Host "Writing combined JSON report..."
[System.IO.File]::WriteAllText(
    $jsonPath,
    ($report | ConvertTo-Json -Depth 12),
    (New-Object System.Text.UTF8Encoding($false)))

Write-Host "Writing combined CSV report..."
$csvRows = Convert-ToCheckRows -Report $report
$csvRows | Export-Csv -LiteralPath $csvPath -NoTypeInformation -Encoding UTF8
Write-Host "Writing combined Markdown summary..."
$markdown = New-MarkdownSummary -Report $report
Set-Content -LiteralPath $markdownPath -Value $markdown -Encoding UTF8

Write-Host "Regression suite complete."
Write-Host ("Files tested: {0}" -f $report.Summary.FilesTested)
Write-Host ("Checks run: {0}" -f $report.Summary.ChecksRun)
Write-Host ("Pass / Warning / Fail: {0} / {1} / {2}" -f $report.Summary.PassCount, $report.Summary.WarningCount, $report.Summary.FailCount)
Write-Host ("Build output: {0}" -f $buildResult.ExecutablePath)
Write-Host ("Artifact: {0}" -f $buildResult.ArtifactPath)
$processExitCodeText = ($processExitCodesArray | ForEach-Object { $_.ToString() }) -join ", "
Write-Host ("Process exit codes: {0}" -f $processExitCodeText)
Write-Host ("JSON: {0}" -f $jsonPath)
Write-Host ("CSV: {0}" -f $csvPath)
Write-Host ("Markdown: {0}" -f $markdownPath)

[pscustomobject]@{
    OutputDirectory = $outputDirectory
    JsonPath = $jsonPath
    CsvPath = $csvPath
    MarkdownPath = $markdownPath
    BuildOutputPath = $buildResult.ExecutablePath
    ArtifactPath = $buildResult.ArtifactPath
    ProcessExitCodes = $processExitCodesArray
    FilesTested = $report.Summary.FilesTested
    ChecksRun = $report.Summary.ChecksRun
    PassCount = $report.Summary.PassCount
    WarningCount = $report.Summary.WarningCount
    FailCount = $report.Summary.FailCount
}

if ($report.Summary.FailCount -gt 0)
{
    exit 2
}

if (@($processExitCodesArray | Where-Object { $_ -eq 1 }).Count -gt 0)
{
    exit 1
}

exit 0
