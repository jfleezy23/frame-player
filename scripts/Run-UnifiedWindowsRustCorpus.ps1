param(
    [Parameter(Mandatory = $false)]
    [string]$CorpusPath = "C:\Projects\Video Test Files",

    [Parameter(Mandatory = $false)]
    [switch]$Recurse,

    [Parameter(Mandatory = $false)]
    [int]$MaxCorpusFiles = 0,

    [Parameter(Mandatory = $false)]
    [string]$Output = "",

    [Parameter(Mandatory = $false)]
    [string]$Configuration = "Release",

    [Parameter(Mandatory = $false)]
    [string[]]$IncludeExtensions = @(".avi", ".m4v", ".mkv", ".mp4", ".wmv"),

    [Parameter(Mandatory = $false)]
    [int]$PerFileTimeoutSeconds = 120,

    [Parameter(Mandatory = $false)]
    [double]$PlaybackFlowMinimumDurationSeconds = 2d,

    [Parameter(Mandatory = $false)]
    [switch]$SkipPackage
)

$ErrorActionPreference = "Stop"

if ($PSVersionTable.PSVersion.Major -lt 7) {
    $pwsh = Get-Command pwsh -ErrorAction Stop
    $forwardedArguments = @("-NoLogo", "-NoProfile", "-File", $PSCommandPath)

    if ($PSBoundParameters.ContainsKey("CorpusPath")) {
        $forwardedArguments += @("-CorpusPath", $CorpusPath)
    }

    if ($Recurse.IsPresent) {
        $forwardedArguments += "-Recurse"
    }

    if ($PSBoundParameters.ContainsKey("MaxCorpusFiles")) {
        $forwardedArguments += @("-MaxCorpusFiles", $MaxCorpusFiles.ToString([Globalization.CultureInfo]::InvariantCulture))
    }

    if ($PSBoundParameters.ContainsKey("Output")) {
        $forwardedArguments += @("-Output", $Output)
    }

    if ($PSBoundParameters.ContainsKey("Configuration")) {
        $forwardedArguments += @("-Configuration", $Configuration)
    }

    if ($PSBoundParameters.ContainsKey("IncludeExtensions")) {
        $forwardedArguments += "-IncludeExtensions"
        $forwardedArguments += $IncludeExtensions
    }

    if ($PSBoundParameters.ContainsKey("PerFileTimeoutSeconds")) {
        $forwardedArguments += @("-PerFileTimeoutSeconds", $PerFileTimeoutSeconds.ToString([Globalization.CultureInfo]::InvariantCulture))
    }

    if ($PSBoundParameters.ContainsKey("PlaybackFlowMinimumDurationSeconds")) {
        $forwardedArguments += @("-PlaybackFlowMinimumDurationSeconds", $PlaybackFlowMinimumDurationSeconds.ToString([Globalization.CultureInfo]::InvariantCulture))
    }

    if ($SkipPackage.IsPresent) {
        $forwardedArguments += "-SkipPackage"
    }

    & $pwsh.Source @forwardedArguments
    exit $LASTEXITCODE
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
if ([string]::IsNullOrWhiteSpace($Output)) {
    $Output = Join-Path $repoRoot "artifacts\unified-windows-rust-corpus"
}

$outputDirectory = (Resolve-Path -LiteralPath (New-Item -ItemType Directory -Path $Output -Force)).Path
$resultsDirectory = Join-Path $outputDirectory "test-results"
New-Item -ItemType Directory -Force -Path $resultsDirectory | Out-Null

function Invoke-ExternalStep {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [scriptblock]$Command
    )

    Write-Host ("== {0} ==" -f $Name)
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw ("{0} failed with exit code {1}." -f $Name, $LASTEXITCODE)
    }
}

function Get-NormalizedExtensions {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Extensions
    )

    @(
        $Extensions |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            ForEach-Object {
                $extension = $_.Trim()
                if (-not $extension.StartsWith(".", [StringComparison]::Ordinal)) {
                    $extension = "." + $extension
                }

                $extension.ToLowerInvariant()
            } |
            Sort-Object -Unique
    )
}

function Get-SafeSlug {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,
        [Parameter(Mandatory = $true)]
        [int]$Index
    )

    $name = [IO.Path]::GetFileNameWithoutExtension($FilePath)
    $slug = ($name -replace '[^A-Za-z0-9._-]+', '_').Trim("_")
    if ([string]::IsNullOrWhiteSpace($slug)) {
        $slug = "file"
    }

    "{0:D2}-{1}" -f $Index, $slug
}

function Stop-ProcessTree {
    param(
        [Parameter(Mandatory = $true)]
        [int]$ProcessId
    )

    $children = @(
        Get-CimInstance Win32_Process |
            Where-Object { $_.ParentProcessId -eq $ProcessId }
    )

    foreach ($child in $children) {
        Stop-ProcessTree -ProcessId $child.ProcessId
    }

    Stop-Process -Id $ProcessId -Force -ErrorAction SilentlyContinue
}

function Get-MediaDurationSeconds {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath
    )

    $ffprobePath = Join-Path $repoRoot "Runtime\ffmpeg-tools\ffprobe.exe"
    if (-not (Test-Path -LiteralPath $ffprobePath)) {
        return $null
    }

    try {
        $durationText = & $ffprobePath `
            -v error `
            -show_entries format=duration `
            -of default=noprint_wrappers=1:nokey=1 `
            $FilePath 2>$null

        $duration = 0d
        if ([double]::TryParse(
            [string]$durationText,
            [Globalization.NumberStyles]::Float,
            [Globalization.CultureInfo]::InvariantCulture,
            [ref]$duration)) {
            return $duration
        }
    }
    catch {
        return $null
    }

    return $null
}

function Invoke-DotnetWithTimeout {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,
        [Parameter(Mandatory = $true)]
        [string]$WorkingDirectory,
        [Parameter(Mandatory = $true)]
        [string]$StandardOutputPath,
        [Parameter(Mandatory = $true)]
        [string]$StandardErrorPath,
        [Parameter(Mandatory = $true)]
        [int]$TimeoutSeconds
    )

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = "dotnet"
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $false
    $startInfo.RedirectStandardError = $false
    foreach ($argument in $Arguments) {
        [void]$startInfo.ArgumentList.Add($argument)
    }

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo

    try {
        [void]$process.Start()

        $timeoutMilliseconds = [Math]::Max(1, $TimeoutSeconds) * 1000
        $timedOut = -not $process.WaitForExit($timeoutMilliseconds)
        if ($timedOut) {
            Stop-ProcessTree -ProcessId $process.Id
        }

        $process.WaitForExit()
        [IO.File]::WriteAllText(
            $StandardOutputPath,
            "Standard output was inherited by the harness process instead of redirected.",
            [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText(
            $StandardErrorPath,
            "Standard error was inherited by the harness process instead of redirected.",
            [Text.UTF8Encoding]::new($false))
        return [pscustomobject]@{
            ExitCode = if ($timedOut) { 124 } else { $process.ExitCode }
            TimedOut = $timedOut
        }
    }
    finally {
        $process.Dispose()
    }
}

$runtimeDirectory = Join-Path $repoRoot "Runtime\ffmpeg"
$rustRuntimeDirectory = Join-Path $repoRoot "Runtime\rust\win-x64"
$cargoBin = Join-Path $env:USERPROFILE ".cargo\bin"
$env:Path = "$cargoBin;$rustRuntimeDirectory;$runtimeDirectory;$env:Path"
$env:FRAMEPLAYER_FFMPEG_INDEX_BUILDER = "rust"
$env:FRAMEPLAYER_FFMPEG_DECODE_CORE = "rust"
$env:FRAMEPLAYER_FFMPEG_FRAME_CONVERTER = "rust"
$env:FRAMEPLAYER_ENABLE_RUST_INDEX_PARITY_TESTS = "1"
$env:FRAMEPLAYER_ENABLE_RUST_DECODE_CORE_TESTS = "1"
$env:FRAMEPLAYER_FFMPEG_RUNTIME_DIR = $runtimeDirectory

$normalizedExtensions = Get-NormalizedExtensions -Extensions $IncludeExtensions
$seen = @{}
$files = New-Object System.Collections.Generic.List[string]

function Add-CorpusFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath
    )

    $resolvedPath = (Resolve-Path -LiteralPath $FilePath).Path
    if (-not $seen.ContainsKey($resolvedPath)) {
        $seen[$resolvedPath] = $true
        [void]$files.Add($resolvedPath)
    }
}

$samplePath = Join-Path $repoRoot "dist\Frame Player\sample-test.mp4"
if (Test-Path -LiteralPath $samplePath) {
    Add-CorpusFile -FilePath $samplePath
}

if (-not (Test-Path -LiteralPath $CorpusPath)) {
    throw "Corpus path does not exist: $CorpusPath"
}

$corpusFiles = @(
    Get-ChildItem -LiteralPath $CorpusPath -File -Recurse:$Recurse |
        Where-Object { $normalizedExtensions -contains $_.Extension.ToLowerInvariant() } |
        Sort-Object FullName
)

if ($MaxCorpusFiles -gt 0) {
    $corpusFiles = @($corpusFiles | Select-Object -First $MaxCorpusFiles)
}

foreach ($file in $corpusFiles) {
    Add-CorpusFile -FilePath $file.FullName
}

if ($files.Count -eq 0) {
    throw "No supported corpus files were found."
}

Write-Host ("Unified Windows Rust corpus files: {0}" -f $files.Count)
Write-Host ("Corpus output: {0}" -f $outputDirectory)

Invoke-ExternalStep -Name "Restore pinned playback runtime" -Command {
    & (Join-Path $PSScriptRoot "Ensure-DevRuntime.ps1")
}
Invoke-ExternalStep -Name "Restore pinned export tools" -Command {
    & (Join-Path $PSScriptRoot "Ensure-DevExportTools.ps1") -Required
}
Invoke-ExternalStep -Name "Restore pinned export runtime" -Command {
    & (Join-Path $PSScriptRoot "Ensure-DevExportRuntime.ps1") -Required
}
Invoke-ExternalStep -Name "Build Rust FFmpeg probe" -Command {
    & (Join-Path $PSScriptRoot "Build-RustFfmpegProbe.ps1")
}
Invoke-ExternalStep -Name "Build Avalonia test project" -Command {
    dotnet build (Join-Path $repoRoot "tests\FramePlayer.Avalonia.Tests\FramePlayer.Avalonia.Tests.csproj") -c $Configuration
}

if (-not $SkipPackage.IsPresent) {
    Invoke-ExternalStep -Name "Package unified Windows preview" -Command {
        & (Join-Path $PSScriptRoot "Package-UnifiedWindowsPreview.ps1") -Configuration $Configuration
    }
}

$testProject = Join-Path $repoRoot "tests\FramePlayer.Avalonia.Tests\FramePlayer.Avalonia.Tests.csproj"
$results = New-Object System.Collections.Generic.List[object]

for ($index = 0; $index -lt $files.Count; $index++) {
    $filePath = $files[$index]
    $displayIndex = $index + 1
    $slug = Get-SafeSlug -FilePath $filePath -Index $displayIndex
    $trxName = $slug + ".trx"
    $stdoutPath = Join-Path $resultsDirectory ($slug + ".stdout.log")
    $stderrPath = Join-Path $resultsDirectory ($slug + ".stderr.log")
    $durationSeconds = Get-MediaDurationSeconds -FilePath $filePath
    $playbackFlowEnabled = $null -eq $durationSeconds -or $durationSeconds -ge $PlaybackFlowMinimumDurationSeconds

    Write-Host ("-- Forced Rust corpus file {0}/{1}: {2}" -f $displayIndex, $files.Count, $filePath)
    if ($null -ne $durationSeconds) {
        Write-Host ("   Duration: {0:N3}s; playback-flow tests: {1}" -f $durationSeconds, $(if ($playbackFlowEnabled) { "enabled" } else { "skipped" }))
    }

    $env:FRAMEPLAYER_RUST_INDEX_TEST_MEDIA = $filePath
    $env:FRAMEPLAYER_RUST_PLAYBACK_TEST_MEDIA = $filePath
    $env:FRAMEPLAYER_ENABLE_RUST_PLAYBACK_FLOW_TESTS = if ($playbackFlowEnabled) { "1" } else { "0" }

    $dotnetArguments = @(
        "test",
        $testProject,
        "-c",
        $Configuration,
        "--no-build",
        "--results-directory",
        $resultsDirectory,
        "--logger",
        ("trx;LogFileName={0}" -f $trxName),
        "--filter",
        "FullyQualifiedName~RustFfmpegProbeTests"
    )

    $testProcessResult = Invoke-DotnetWithTimeout `
        -Arguments $dotnetArguments `
        -WorkingDirectory $repoRoot `
        -StandardOutputPath $stdoutPath `
        -StandardErrorPath $stderrPath `
        -TimeoutSeconds $PerFileTimeoutSeconds

    $timedOut = $testProcessResult.TimedOut
    $exitCode = $testProcessResult.ExitCode
    [void]$results.Add([pscustomobject]@{
        FilePath = $filePath
        Succeeded = $exitCode -eq 0
        ExitCode = $exitCode
        TimedOut = $timedOut
        DurationSeconds = $durationSeconds
        PlaybackFlowEnabled = $playbackFlowEnabled
        TrxPath = Join-Path $resultsDirectory $trxName
        StandardOutputPath = $stdoutPath
        StandardErrorPath = $stderrPath
    })

    if ($timedOut) {
        Write-Warning ("Timed out after {0} seconds: {1}" -f $PerFileTimeoutSeconds, $filePath)
    }
}

$failedResults = @($results | Where-Object { -not $_.Succeeded })
$summary = [pscustomobject]@{
    GeneratedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    FilesTested = $results.Count
    PassCount = $results.Count - $failedResults.Count
    FailCount = $failedResults.Count
    ForcedModes = [pscustomobject]@{
        IndexBuilder = $env:FRAMEPLAYER_FFMPEG_INDEX_BUILDER
        DecodeCore = $env:FRAMEPLAYER_FFMPEG_DECODE_CORE
        FrameConverter = $env:FRAMEPLAYER_FFMPEG_FRAME_CONVERTER
    }
    PlaybackFlowMinimumDurationSeconds = $PlaybackFlowMinimumDurationSeconds
    Results = $results.ToArray()
}

$jsonPath = Join-Path $outputDirectory "unified-windows-rust-corpus-report.json"
$markdownPath = Join-Path $outputDirectory "unified-windows-rust-corpus-summary.md"

$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $jsonPath -Encoding UTF8

$markdown = New-Object System.Collections.Generic.List[string]
[void]$markdown.Add("# Unified Windows Rust Corpus")
[void]$markdown.Add("")
[void]$markdown.Add(("Generated at UTC: {0}" -f $summary.GeneratedAtUtc))
[void]$markdown.Add("")
[void]$markdown.Add(("- Files tested: {0}" -f $summary.FilesTested))
[void]$markdown.Add(("- Pass: {0}" -f $summary.PassCount))
[void]$markdown.Add(("- Fail: {0}" -f $summary.FailCount))
[void]$markdown.Add(("- Playback-flow minimum duration: {0:N3}s" -f $PlaybackFlowMinimumDurationSeconds))
[void]$markdown.Add("")
[void]$markdown.Add("| Result | Playback flow | Duration | File | TRX |")
[void]$markdown.Add("| --- | --- | ---: | --- | --- |")
foreach ($result in $results) {
    $status = if ($result.Succeeded) { "PASS" } else { "FAIL" }
    $playbackStatus = if ($result.PlaybackFlowEnabled) { "enabled" } else { "skipped" }
    $durationText = if ($null -eq $result.DurationSeconds) { "unknown" } else { "{0:N3}" -f $result.DurationSeconds }
    [void]$markdown.Add(("| {0} | {1} | {2} | {3} | {4} |" -f $status, $playbackStatus, $durationText, $result.FilePath, $result.TrxPath))
}

Set-Content -LiteralPath $markdownPath -Value $markdown -Encoding UTF8

Write-Host "Unified Windows Rust corpus complete."
Write-Host ("Files tested: {0}" -f $summary.FilesTested)
Write-Host ("Pass / Fail: {0} / {1}" -f $summary.PassCount, $summary.FailCount)
Write-Host ("JSON: {0}" -f $jsonPath)
Write-Host ("Markdown: {0}" -f $markdownPath)

$summary

if ($failedResults.Count -gt 0) {
    exit 2
}

exit 0
