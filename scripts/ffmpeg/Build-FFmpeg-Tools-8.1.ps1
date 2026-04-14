param(
    [string]$Msys2Root = "C:\msys64",
    [string]$WorkRoot = "",
    [string]$CandidateToolsPath = "",
    [string]$ArchivePath = "",
    [int]$Jobs = 0,
    [switch]$Clean
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$resolvedWorkRoot = if ([string]::IsNullOrWhiteSpace($WorkRoot)) {
    Join-Path $env:TEMP "frameplayer-ffmpeg-tools-8.1-source-build"
}
else {
    $WorkRoot
}
$resolvedCandidateToolsPath = if ([string]::IsNullOrWhiteSpace($CandidateToolsPath)) {
    Join-Path $repoRoot "Runtime\ffmpeg-tools-8.1-candidate"
}
else {
    $CandidateToolsPath
}
$resolvedArchivePath = if ([string]::IsNullOrWhiteSpace($ArchivePath)) {
    Join-Path $repoRoot "artifacts\FramePlayer-ffmpeg-tools-x64.zip"
}
else {
    $ArchivePath
}

$bashPath = Join-Path $Msys2Root "usr\bin\bash.exe"
if (-not (Test-Path -LiteralPath $bashPath)) {
    throw "MSYS2 bash was not found at '$bashPath'. Install MSYS2 and the MinGW-w64 x64 toolchain first."
}
if ($resolvedWorkRoot -match "\s") {
    throw "FFmpeg out-of-tree builds fail when the source path contains whitespace. Use -WorkRoot with a path that has no spaces. Current path: '$resolvedWorkRoot'."
}

New-Item -ItemType Directory -Force -Path $resolvedWorkRoot | Out-Null
New-Item -ItemType Directory -Force -Path $resolvedCandidateToolsPath | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $resolvedArchivePath) | Out-Null

$repoRootUnix = (& $bashPath -lc "cygpath -u '$($repoRoot.Path -replace '\\', '\\')'").Trim()
$workRootUnix = (& $bashPath -lc "cygpath -u '$($resolvedWorkRoot -replace '\\', '\\')'").Trim()
$candidateToolsUnix = (& $bashPath -lc "cygpath -u '$($resolvedCandidateToolsPath -replace '\\', '\\')'").Trim()
$jobsValue = if ($Jobs -gt 0) { $Jobs } else { [Math]::Max(1, [Environment]::ProcessorCount) }
$cleanValue = if ($Clean) { "1" } else { "0" }

$script = @"
set -euo pipefail

export PATH=/mingw64/bin:/usr/bin:`$PATH
export MSYSTEM=MINGW64
export CHERE_INVOKING=1

work_root='$workRootUnix'
source_dir="`$work_root/ffmpeg-n8.1"
build_dir="`$work_root/build-mingw64-export-tools"
install_dir="`$work_root/install-mingw64-export-tools"
candidate_dir='$candidateToolsUnix'
jobs='$jobsValue'
clean='$cleanValue'

mkdir -p "`$work_root"

for tool in git gcc g++ make pkgconf nasm; do
    command -v "`$tool" >/dev/null || { echo "Missing required tool: `$tool" >&2; exit 2; }
done

pkgconf --exists x264 || {
    echo "Missing x264 development files in the MSYS2 MinGW-w64 x64 environment. Install mingw-w64-x86_64-x264 before building the export tools." >&2
    exit 2
}

if [ "`$clean" = "1" ]; then
    rm -rf "`$source_dir" "`$build_dir" "`$install_dir"
fi

if [ ! -d "`$source_dir/.git" ]; then
    git clone --branch n8.1 --depth 1 https://git.ffmpeg.org/ffmpeg.git "`$source_dir"
else
    git -C "`$source_dir" fetch --depth 1 origin tag n8.1
    git -C "`$source_dir" checkout -f n8.1
fi

actual_commit="`$(git -C "`$source_dir" rev-parse HEAD)"
expected_commit="9047fa1b084f76b1b4d065af2d743df1b40dfb56"
if [ "`$actual_commit" != "`$expected_commit" ]; then
    echo "Unexpected FFmpeg n8.1 commit: `$actual_commit" >&2
    exit 3
fi

rm -rf "`$build_dir" "`$install_dir"
mkdir -p "`$build_dir" "`$install_dir"

cd "`$build_dir"
configure_flags=(
    --prefix="`$install_dir"
    --target-os=mingw32
    --arch=x86_64
    --enable-shared
    --disable-static
    --enable-ffmpeg
    --enable-ffprobe
    --disable-ffplay
    --disable-doc
    --disable-debug
    --disable-avdevice
    --disable-network
    --enable-gpl
    --enable-libx264
    --extra-version=frameplayer-export-tools
)

"`$source_dir/configure" "`${configure_flags[@]}"

grep -q "^#define CONFIG_FFMPEG 1$" config.h || {
    echo "FFmpeg configure did not enable the ffmpeg CLI program." >&2
    exit 4
}
grep -q "^#define CONFIG_FFPROBE 1$" config.h || {
    echo "FFmpeg configure did not enable the ffprobe CLI program." >&2
    exit 4
}
grep -q "^#define CONFIG_LIBX264_ENCODER 1$" config_components.h || {
    echo "FFmpeg configure did not enable the libx264 encoder." >&2
    exit 4
}

make -j"`$jobs"
make install

dependency_report_ffmpeg="`$(mktemp)"
dependency_report_ffprobe="`$(mktemp)"
ldd "`$install_dir/bin/ffmpeg.exe" > "`$dependency_report_ffmpeg"
ldd "`$install_dir/bin/ffprobe.exe" > "`$dependency_report_ffprobe"

if grep -q "not found" "`$dependency_report_ffmpeg" "`$dependency_report_ffprobe"; then
    echo "One or more FFmpeg export-tool dependencies were not resolved by ldd." >&2
    cat "`$dependency_report_ffmpeg" >&2
    cat "`$dependency_report_ffprobe" >&2
    exit 5
fi

mapfile -t staged_dependency_paths < <(
    awk '/=>/ { print `$3 }' "`$dependency_report_ffmpeg" "`$dependency_report_ffprobe" |
    grep '^/mingw64/bin/' |
    sort -u ||
    true
)

rm -rf "`$candidate_dir"
mkdir -p "`$candidate_dir"
cp "`$install_dir/bin/"*.exe "`$candidate_dir/"
cp "`$install_dir/bin/"*.dll "`$candidate_dir/"
for dependency_path in "`${staged_dependency_paths[@]}"; do
    if [ -n "`$dependency_path" ] && [ -f "`$dependency_path" ]; then
        cp "`$dependency_path" "`$candidate_dir/"
    fi
done

{
    echo "FFmpeg tag: n8.1"
    echo "FFmpeg commit: `$actual_commit"
    echo "Toolchain: MSYS2 MinGW-w64 x64"
    echo "GCC: `$(gcc --version | head -1)"
    echo "Make: `$(make --version | head -1)"
    echo "pkgconf: `$(pkgconf --version)"
    echo "x264 version: `$(pkgconf --modversion x264)"
    echo "NASM: `$(nasm -v)"
    echo "Bundled dependency DLLs:"
    if [ "`${#staged_dependency_paths[@]}" -eq 0 ]; then
        echo "  (none beyond install bin output)"
    else
        for dependency_path in "`${staged_dependency_paths[@]}"; do
            printf '  %s\n' "`$(basename "`$dependency_path")"
        done
    fi
    echo "Configure flags:"
    printf '  %q' "`$source_dir/configure" "`${configure_flags[@]}"
    printf '\n'
} > "`$candidate_dir/build-provenance.txt"

sha256sum "`$candidate_dir/"* > "`$candidate_dir/SHA256SUMS.txt"
rm -f "`$dependency_report_ffmpeg" "`$dependency_report_ffprobe"

echo "Candidate FFmpeg export tools staged at: `$candidate_dir"
ls -la "`$candidate_dir"
"@

$temporaryScriptPath = Join-Path $env:TEMP ("frameplayer-build-ffmpeg-tools-8.1-" + [Guid]::NewGuid().ToString("N") + ".sh")
Set-Content -LiteralPath $temporaryScriptPath -Value $script -Encoding ASCII
$temporaryScriptUnix = (& $bashPath -lc "cygpath -u '$($temporaryScriptPath -replace '\\', '\\')'").Trim()

try {
    & $bashPath -lc "bash '$temporaryScriptUnix'"
    if ($LASTEXITCODE -ne 0) {
        throw "FFmpeg export-tools source build failed with exit code $LASTEXITCODE."
    }
}
finally {
    Remove-Item -LiteralPath $temporaryScriptPath -Force -ErrorAction SilentlyContinue
}

Write-Host "Candidate export tools staged at '$resolvedCandidateToolsPath'."
if (Test-Path -LiteralPath $resolvedArchivePath) {
    Remove-Item -LiteralPath $resolvedArchivePath -Force
}
Compress-Archive -Path (Join-Path $resolvedCandidateToolsPath "*") -DestinationPath $resolvedArchivePath -CompressionLevel Optimal
$archiveHash = (Get-FileHash -LiteralPath $resolvedArchivePath -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host "Candidate export-tools archive staged at '$resolvedArchivePath'."
Write-Host "Archive SHA256: $archiveHash"
