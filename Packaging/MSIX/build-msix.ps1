param(
    [string]$Configuration = "Release",
    [string]$Platform = "x64",
    [string]$PackageName = "JonathanFloyd.FramePlayer",
    [string]$PackageDisplayName = "Frame Player",
    [string]$Publisher = "CN=Frame Player Dev",
    [string]$PublisherDisplayName = "Frame Player Dev",
    [string]$PackageVersion = "",
    [string]$SigningPfxPath = "",
    [string]$SigningPfxPassword = "",
    [string]$TimestampUrl = "",
    [string]$CertificatePassword = "",
    [switch]$UseDevCertificate,
    [switch]$TrustCertificate,
    [switch]$InstallPackage
)

$ErrorActionPreference = "Stop"

function Get-ToolPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ToolName
    )

    $windowsKitsRoot = "C:\Program Files (x86)\Windows Kits\10\bin"
    if (-not (Test-Path $windowsKitsRoot)) {
        throw "Windows SDK tools were not found under '$windowsKitsRoot'."
    }

    $tool = Get-ChildItem $windowsKitsRoot -Recurse -Filter $ToolName -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending |
        Select-Object -First 1

    if ($null -eq $tool) {
        throw "Could not locate $ToolName in the Windows SDK."
    }

    return $tool.FullName
}

function Assert-HttpsUrl {
    param(
        [string]$Url,
        [Parameter(Mandatory = $true)]
        [string]$Label
    )

    if ([string]::IsNullOrWhiteSpace($Url)) {
        return
    }

    $parsedUri = $null
    if (-not [System.Uri]::TryCreate($Url, [System.UriKind]::Absolute, [ref]$parsedUri) -or
        -not [string]::Equals($parsedUri.Scheme, [System.Uri]::UriSchemeHttps, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "$Label must be an absolute HTTPS URL."
    }
}

function New-PngFromIcon {
    param(
        [Parameter(Mandatory = $true)]
        [string]$IconPath,
        [Parameter(Mandatory = $true)]
        [string]$OutputPath,
        [Parameter(Mandatory = $true)]
        [int]$Size
    )

    Add-Type -AssemblyName PresentationCore
    Add-Type -AssemblyName WindowsBase

    $iconUri = New-Object System.Uri($IconPath)
    $decoder = New-Object System.Windows.Media.Imaging.IconBitmapDecoder(
        $iconUri,
        [System.Windows.Media.Imaging.BitmapCreateOptions]::PreservePixelFormat,
        [System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad)

    $sourceFrame = $decoder.Frames |
        Sort-Object PixelWidth -Descending |
        Select-Object -First 1

    if ($null -eq $sourceFrame) {
        throw "Could not decode icon frames from '$IconPath'."
    }

    $scaleTransform = New-Object System.Windows.Media.ScaleTransform(
        ($Size / [double]$sourceFrame.PixelWidth),
        ($Size / [double]$sourceFrame.PixelHeight))

    $scaledFrame = New-Object System.Windows.Media.Imaging.TransformedBitmap($sourceFrame, $scaleTransform)
    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($scaledFrame))

    $stream = [System.IO.File]::Create($OutputPath)
    try {
        $encoder.Save($stream)
    }
    finally {
        $stream.Dispose()
    }
}

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$projectPath = Join-Path $repoRoot "FramePlayer.csproj"
$ensureRuntimeScript = Join-Path $repoRoot "scripts\Ensure-DevRuntime.ps1"
$ensureExportToolsScript = Join-Path $repoRoot "scripts\Ensure-DevExportTools.ps1"
$releaseDir = Join-Path $repoRoot ("bin\" + $Configuration)
$distDir = Join-Path $repoRoot "dist\MSIX"
$buildRoot = Join-Path $distDir "_build"
$packageRoot = Join-Path $buildRoot "PackageRoot"
$assetsDir = Join-Path $packageRoot "Assets"
$certificateDir = Join-Path $buildRoot "cert"
$iconPath = Join-Path $repoRoot "Assets\FramePlayer.ico"
$makeappxPath = Get-ToolPath -ToolName "makeappx.exe"
$makepriPath = Get-ToolPath -ToolName "makepri.exe"
$signtoolPath = Get-ToolPath -ToolName "signtool.exe"

& $ensureRuntimeScript | Out-Host
if (Test-Path -LiteralPath $ensureExportToolsScript) {
    & $ensureExportToolsScript | Out-Host
}
& dotnet build $projectPath -c $Configuration -p:Platform=$Platform | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed while producing the MSIX package."
}

if (-not (Test-Path $releaseDir)) {
    throw "Release output directory '$releaseDir' was not produced."
}

$exePath = Join-Path $releaseDir "FramePlayer.exe"
if (-not (Test-Path $exePath)) {
    throw "FramePlayer.exe was not found in '$releaseDir'."
}

$resolvedVersion = if ([string]::IsNullOrWhiteSpace($PackageVersion)) {
    $fileVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exePath).FileVersion
    if ([string]::IsNullOrWhiteSpace($fileVersion)) { "1.0.0.0" } else { $fileVersion }
}
else {
    $PackageVersion
}

$safeVersion = [Version]$resolvedVersion
$resolvedVersion = $safeVersion.ToString()

Assert-HttpsUrl -Url $TimestampUrl -Label "TimestampUrl"

New-Item -ItemType Directory -Force -Path $distDir | Out-Null
Remove-Item $buildRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $packageRoot, $assetsDir, $certificateDir | Out-Null

Get-ChildItem $releaseDir -File |
    Where-Object { $_.Extension -ne ".pdb" } |
    ForEach-Object {
        Copy-Item $_.FullName -Destination (Join-Path $packageRoot $_.Name) -Force
    }

$exportToolsReleaseDirectory = Join-Path $releaseDir "ffmpeg-tools"
if (Test-Path -LiteralPath $exportToolsReleaseDirectory) {
    Copy-Item -LiteralPath $exportToolsReleaseDirectory -Destination (Join-Path $packageRoot "ffmpeg-tools") -Recurse -Force
}

New-PngFromIcon -IconPath $iconPath -OutputPath (Join-Path $assetsDir "FramePlayer150x150.png") -Size 150
New-PngFromIcon -IconPath $iconPath -OutputPath (Join-Path $assetsDir "FramePlayer44x44.png") -Size 44
Copy-Item (Join-Path $assetsDir "FramePlayer44x44.png") (Join-Path $assetsDir "FramePlayer44x44.targetsize-44_altform-unplated.png") -Force

$manifestPath = Join-Path $packageRoot "AppxManifest.xml"
$manifest = @"
<?xml version="1.0" encoding="utf-8"?>
<Package
  xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
  xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
  xmlns:uap10="http://schemas.microsoft.com/appx/manifest/uap/windows10/10"
  xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
  IgnorableNamespaces="uap uap10 rescap">
  <Identity Name="$PackageName"
            Version="$resolvedVersion"
            Publisher="$Publisher"
            ProcessorArchitecture="$Platform" />
  <Properties>
    <DisplayName>$PackageDisplayName</DisplayName>
    <PublisherDisplayName>$PublisherDisplayName</PublisherDisplayName>
    <Description>Compact frame-accurate video player with bundled FFmpeg runtime.</Description>
    <Logo>Assets\FramePlayer150x150.png</Logo>
  </Properties>
  <Resources>
    <Resource Language="en-us" />
  </Resources>
  <Dependencies>
    <TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.19041.0" MaxVersionTested="10.0.26100.0" />
  </Dependencies>
  <Capabilities>
    <rescap:Capability Name="runFullTrust" />
  </Capabilities>
  <Applications>
    <Application Id="FramePlayer"
                 Executable="FramePlayer.exe"
                 uap10:RuntimeBehavior="packagedClassicApp"
                 uap10:TrustLevel="mediumIL">
      <uap:VisualElements BackgroundColor="transparent"
                          DisplayName="$PackageDisplayName"
                          Description="Compact frame-accurate video player"
                          Square150x150Logo="Assets\FramePlayer150x150.png"
                          Square44x44Logo="Assets\FramePlayer44x44.png" />
    </Application>
  </Applications>
</Package>
"@
Set-Content -Path $manifestPath -Value $manifest -Encoding UTF8

Push-Location $packageRoot
try {
    & $makepriPath createconfig /cf priconfig.xml /dq en-US | Out-Host
    & $makepriPath new /pr $packageRoot /cf (Join-Path $packageRoot "priconfig.xml") | Out-Host
    Remove-Item (Join-Path $packageRoot "priconfig.xml") -Force -ErrorAction SilentlyContinue
}
finally {
    Pop-Location
}

$msixFileName = "FramePlayer_{0}_{1}.msix" -f $resolvedVersion, $Platform
$msixPath = Join-Path $distDir $msixFileName
Remove-Item $msixPath -Force -ErrorAction SilentlyContinue

& $makeappxPath pack /o /h SHA256 /d $packageRoot /p $msixPath | Out-Host

$pfxPath = Join-Path $certificateDir "FramePlayer-TestCert.pfx"
$cerPath = Join-Path $distDir "FramePlayer-TestCert.cer"
$usingCustomSigningCertificate = -not [string]::IsNullOrWhiteSpace($SigningPfxPath)

if (-not $usingCustomSigningCertificate -and -not $UseDevCertificate) {
    throw "MSIX signing requires either -SigningPfxPath for a trusted certificate or -UseDevCertificate for local testing."
}

if ($usingCustomSigningCertificate) {
    if (-not (Test-Path -LiteralPath $SigningPfxPath)) {
        throw "The signing PFX was not found at '$SigningPfxPath'."
    }

    $plainPassword = $SigningPfxPassword
    $securePassword = if ([string]::IsNullOrWhiteSpace($SigningPfxPassword)) {
        $null
    }
    else {
        ConvertTo-SecureString -String $SigningPfxPassword -AsPlainText -Force
    }

    $pfxPath = $SigningPfxPath

    $pfxData = if ($securePassword -eq $null) {
        Get-PfxData -FilePath $SigningPfxPath
    }
    else {
        Get-PfxData -FilePath $SigningPfxPath -Password $securePassword
    }

    Export-Certificate -Cert $pfxData.EndEntityCertificates[0] -FilePath $cerPath | Out-Null

    if ([string]::IsNullOrWhiteSpace($TimestampUrl)) {
        Write-Warning "No timestamp URL was provided. The signed MSIX will expire with the signing certificate."
    }
}
else {
    $plainPassword = if ([string]::IsNullOrWhiteSpace($CertificatePassword)) {
        [Guid]::NewGuid().ToString("N")
    }
    else {
        $CertificatePassword
    }

    $securePassword = ConvertTo-SecureString -String $plainPassword -AsPlainText -Force

    $certificate = New-SelfSignedCertificate `
        -Type Custom `
        -Subject $Publisher `
        -FriendlyName "Frame Player MSIX Test Certificate" `
        -KeyAlgorithm RSA `
        -KeyLength 2048 `
        -HashAlgorithm SHA256 `
        -KeyExportPolicy Exportable `
        -KeyUsage DigitalSignature `
        -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3") `
        -CertStoreLocation "Cert:\CurrentUser\My"

    Export-PfxCertificate -Cert $certificate -FilePath $pfxPath -Password $securePassword | Out-Null
    Export-Certificate -Cert $certificate -FilePath $cerPath | Out-Null
}

if ($TrustCertificate) {
    Import-Certificate -FilePath $cerPath -CertStoreLocation "Cert:\CurrentUser\TrustedPeople" | Out-Null
}

$signArguments = @("sign", "/fd", "SHA256", "/a", "/f", $pfxPath)
if (-not [string]::IsNullOrWhiteSpace($plainPassword)) {
    $signArguments += @("/p", $plainPassword)
}
$useTimestamp = $usingCustomSigningCertificate -and -not [string]::IsNullOrWhiteSpace($TimestampUrl)
if ($useTimestamp) {
    $signArguments += @("/tr", $TimestampUrl, "/td", "SHA256")
}
$signArguments += $msixPath
& $signtoolPath $signArguments | Out-Host

$installScriptPath = Join-Path $distDir "Install-FramePlayer-MSIX.ps1"
$certificateInstallCommand = if ($usingCustomSigningCertificate) {
    "# Trusted signing certificates should already be deployed through your enterprise certificate policy."
}
else {
    'Import-Certificate -FilePath $certificatePath -CertStoreLocation "Cert:\CurrentUser\TrustedPeople" | Out-Null'
}
$installScript = @"
param(
    [switch]`$ForceUpdate
)

`$ErrorActionPreference = "Stop"
`$packagePath = Join-Path `$PSScriptRoot "$msixFileName"
`$certificatePath = Join-Path `$PSScriptRoot "FramePlayer-TestCert.cer"

$certificateInstallCommand

if (`$ForceUpdate) {
    Add-AppxPackage -ForceUpdateFromAnyVersion -Path `$packagePath
}
else {
    Add-AppxPackage -Path `$packagePath
}
"@
Set-Content -Path $installScriptPath -Value $installScript -Encoding UTF8

if ($InstallPackage) {
    if (-not $usingCustomSigningCertificate) {
        Import-Certificate -FilePath $cerPath -CertStoreLocation "Cert:\CurrentUser\TrustedPeople" | Out-Null
    }
    Add-AppxPackage -ForceUpdateFromAnyVersion -Path $msixPath
}

Remove-Item $buildRoot -Recurse -Force -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "MSIX package created:" -ForegroundColor Green
Write-Host "  $msixPath"
Write-Host "Certificate exported:"
Write-Host "  $cerPath"
Write-Host "Install helper:"
Write-Host "  $installScriptPath"

if ($usingCustomSigningCertificate) {
    Write-Host "Signing certificate:"
    Write-Host "  $SigningPfxPath"
    if ($useTimestamp) {
        Write-Host "Timestamp URL:"
        Write-Host "  $TimestampUrl"
    }
}
elseif ([string]::IsNullOrWhiteSpace($CertificatePassword)) {
    Write-Warning "A temporary self-signed development certificate was used. Do not use this package for production deployment."
}
