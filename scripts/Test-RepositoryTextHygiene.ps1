$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$trackedFiles = @(
    & git -C $repoRoot ls-files --cached --others --exclude-standard |
        Sort-Object -Unique
)
if ($LASTEXITCODE -ne 0) {
    throw "Unable to enumerate tracked repository files."
}

$issues = New-Object System.Collections.Generic.List[string]
$caseInsensitivePaths = @{}

foreach ($relativePath in $trackedFiles) {
    $caseKey = $relativePath.ToUpperInvariant()
    if ($caseInsensitivePaths.ContainsKey($caseKey)) {
        $issues.Add(
            "Case-insensitive path collision: '$($caseInsensitivePaths[$caseKey])' and '$relativePath'.")
    }
    else {
        $caseInsensitivePaths[$caseKey] = $relativePath
    }
}

$directoryEntries = @{}
foreach ($relativePath in $trackedFiles) {
    $currentDirectory = $repoRoot
    foreach ($segment in $relativePath.Split('/')) {
        if (-not $directoryEntries.ContainsKey($currentDirectory)) {
            $directoryEntries[$currentDirectory] = @(
                Get-ChildItem -LiteralPath $currentDirectory -Force | Select-Object -ExpandProperty Name)
        }

        $actualName = @($directoryEntries[$currentDirectory] | Where-Object {
            [string]::Equals($_, $segment, [StringComparison]::OrdinalIgnoreCase)
        })

        if ($actualName.Count -ne 1) {
            $issues.Add("Tracked path '$relativePath' could not be resolved at '$segment'.")
            break
        }

        if (-not [string]::Equals($actualName[0], $segment, [StringComparison]::Ordinal)) {
            $issues.Add(
                "Tracked path casing mismatch in '$relativePath': git records '$segment', disk has '$($actualName[0])'.")
            break
        }

        $currentDirectory = Join-Path $currentDirectory $actualName[0]
    }
}

$textExtensions = @(
    ".axaml",
    ".config",
    ".cs",
    ".csproj",
    ".json",
    ".md",
    ".props",
    ".ps1",
    ".sh",
    ".targets",
    ".toml",
    ".xml",
    ".yaml",
    ".yml"
)
$textFileNames = @(
    ".editorconfig",
    ".gitattributes",
    ".gitignore",
    ".psscriptanalyzer.psd1",
    "LICENSE",
    "NuGet.config"
)
$strictUtf8 = New-Object System.Text.UTF8Encoding($false, $true)

foreach ($relativePath in $trackedFiles) {
    $extension = [IO.Path]::GetExtension($relativePath).ToLowerInvariant()
    $fileName = [IO.Path]::GetFileName($relativePath)
    if ($textExtensions -notcontains $extension -and $textFileNames -notcontains $fileName) {
        continue
    }

    $absolutePath = Join-Path $repoRoot $relativePath
    $bytes = [IO.File]::ReadAllBytes($absolutePath)
    try {
        $text = $strictUtf8.GetString($bytes)
    }
    catch {
        $issues.Add("Text file '$relativePath' is not valid UTF-8.")
        continue
    }

    if ($bytes.Length -ge 3 -and
        $bytes[0] -eq 0xEF -and
        $bytes[1] -eq 0xBB -and
        $bytes[2] -eq 0xBF) {
        $issues.Add("Text file '$relativePath' has a UTF-8 BOM.")
    }

    if ($text.Contains("`r")) {
        $issues.Add("Text file '$relativePath' contains CR or CRLF line endings; LF is required.")
    }

    if ($text.Length -gt 0 -and -not $text.EndsWith("`n", [StringComparison]::Ordinal)) {
        $issues.Add("Text file '$relativePath' does not end with a newline.")
    }

    if ($text -match '(?m)[ \t]+$') {
        $issues.Add("Text file '$relativePath' contains trailing whitespace.")
    }
}

if ($issues.Count -gt 0) {
    throw "Repository text hygiene failed:`n- $($issues -join "`n- ")"
}

Write-Host "Repository text, path casing, and case-collision checks passed."
