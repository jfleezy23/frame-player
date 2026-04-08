param(
    [Parameter(Mandatory = $false)]
    [string]$DesktopPath = [Environment]::GetFolderPath("DesktopDirectory"),

    [Parameter(Mandatory = $false)]
    [string]$ExecutablePath
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ExecutablePath))
{
    $ExecutablePath = Join-Path $projectRoot "bin\TestDrop\FramePlayer.exe"
}

if (-not (Test-Path -LiteralPath $DesktopPath))
{
    throw "Desktop path was not found: $DesktopPath"
}

$shell = New-Object -ComObject WScript.Shell
$createdShortcuts = @()
$legacyFfmeShortcutPath = Join-Path $DesktopPath "Frame Player - FFME Baseline.lnk"
if (Test-Path -LiteralPath $legacyFfmeShortcutPath)
{
    Remove-Item -LiteralPath $legacyFfmeShortcutPath -Force
    Write-Host "Removed legacy FFME baseline shortcut from the desktop."
}

if (-not (Test-Path -LiteralPath $ExecutablePath))
{
    throw ("Build output not found for Frame Player - Custom FFmpeg: {0}. Run .\\scripts\\Build-TestDrop.ps1 first." -f $ExecutablePath)
}

$shortcutName = "Frame Player - Custom FFmpeg"
$shortcutPath = Join-Path $DesktopPath ($shortcutName + ".lnk")
if (Test-Path -LiteralPath $shortcutPath)
{
    Remove-Item -LiteralPath $shortcutPath -Force
}

$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = (Resolve-Path -LiteralPath $ExecutablePath).Path
$shortcut.WorkingDirectory = Split-Path -Parent $shortcut.TargetPath
$shortcut.Description = $shortcutName
$shortcut.IconLocation = $shortcut.TargetPath + ",0"
$shortcut.Save()

$createdShortcuts += [pscustomobject]@{
    ShortcutName = $shortcutName
    ShortcutPath = (Resolve-Path -LiteralPath $shortcutPath).Path
    TargetPath = $shortcut.TargetPath
}

Write-Host "Custom FFmpeg shortcut created."
foreach ($shortcutInfo in $createdShortcuts)
{
    Write-Host ("{0}: {1}" -f $shortcutInfo.ShortcutName, $shortcutInfo.ShortcutPath)
}

$createdShortcuts
