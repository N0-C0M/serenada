<#
.SYNOPSIS
    Builds libwebrtc for Windows x64 from Google's WebRTC source.
    Follows the same branch-heads/7827 used by the Android client.

.DESCRIPTION
    This script automates the multi-step process of building the WebRTC
    native library on Windows. It requires:
      - Visual Studio 2022 with "Desktop development with C++" workload
      - depot_tools (https://commondatastorage.googleapis.com/chrome-infra-docs/flat/depot_tools/docs/html/depot_tools_tutorial.html)
      - ~20 GB of free disk space for the checkout
      - A fast internet connection (the checkout is ~15 GB)

.PARAMETER OutputDir
    Directory where the webrtc source will be checked out and built.
    Default: $env:USERPROFILE\webrtc-build

.PARAMETER Branch
    WebRTC branch to build. Default: branch-heads/7827 (matches Android).

.PARAMETER Configuration
    Build configuration: Debug or Release. Default: Release.

.EXAMPLE
    .\build-libwebrtc.ps1
    .\build-libwebrtc.ps1 -OutputDir D:\webrtc -Configuration Debug
#>

param(
    [string]$OutputDir = "$env:USERPROFILE\webrtc-build",
    [string]$Branch = "branch-heads/7827",
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Serenada libwebrtc Windows Build" -ForegroundColor Cyan
Write-Host "  Branch: $Branch" -ForegroundColor Cyan
Write-Host "  Config: $Configuration" -ForegroundColor Cyan
Write-Host "  Output: $OutputDir" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# --- Prerequisites ---

if (-not (Get-Command "python3" -ErrorAction SilentlyContinue)) {
    Write-Host "ERROR: python3 not found. Install Python 3 and add it to PATH." -ForegroundColor Red
    exit 1
}

if (-not (Get-Command "git" -ErrorAction SilentlyContinue)) {
    Write-Host "ERROR: git not found." -ForegroundColor Red
    exit 1
}

# Check for depot_tools
$depotTools = "$OutputDir\depot_tools"
if (-not (Test-Path "$depotTools\gclient.bat")) {
    Write-Host "Cloning depot_tools..." -ForegroundColor Yellow
    git clone https://chromium.googlesource.com/chromium/tools/depot_tools.git $depotTools
}

$env:PATH = "$depotTools;$env:PATH"
$env:DEPOT_TOOLS_WIN_TOOLCHAIN = "0"  # Use local VS, not downloaded toolchain

# --- Checkout WebRTC source ---

$webrtcSrc = "$OutputDir\src"
if (-not (Test-Path "$webrtcSrc\.gclient")) {
    Write-Host "Fetching WebRTC source (this will take a while)..." -ForegroundColor Yellow
    Push-Location $OutputDir
    & "$depotTools\fetch.bat" webrtc
    Pop-Location
}

# --- Sync to the correct branch ---

Write-Host "Syncing to $Branch..." -ForegroundColor Yellow
Push-Location $webrtcSrc
git fetch origin $Branch
git checkout $Branch
& "$depotTools\gclient.bat" sync -D
Pop-Location

# --- Generate build files with gn ---

$outDir = "$webrtcSrc\out\$Configuration"
$isDebug = if ($Configuration -eq "Debug") { "true" } else { "false" }

Write-Host "Generating build files (gn)..." -ForegroundColor Yellow
Push-Location $webrtcSrc

& gn gen $outDir --args=@"
is_debug = $isDebug
target_cpu = "x64"
target_os = "win"
rtc_include_tests = false
rtc_build_examples = false
rtc_build_tools = false
rtc_enable_protobuf = false
use_rtti = true
is_clang = false
treat_warnings_as_errors = false
"@

Pop-Location

# --- Build ---

Write-Host "Building libwebrtc (ninja)..." -ForegroundColor Yellow
Push-Location $webrtcSrc
& ninja -C $outDir webrtc
Pop-Location

# --- Verify output ---

$libPath = "$outDir\obj\webrtc.lib"
$dllPath = "$outDir\webrtc.dll"

if (Test-Path $libPath) {
    Write-Host "SUCCESS: webrtc.lib built at $libPath" -ForegroundColor Green
} else {
    Write-Host "WARNING: webrtc.lib not found at expected path." -ForegroundColor Yellow
    Write-Host "Look for .lib files in $outDir\obj\" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "=== Next Steps ===" -ForegroundColor Cyan
Write-Host "1. Set WEBRTC_ROOT=$webrtcSrc" -ForegroundColor White
Write-Host "2. Open client-windows/SerenadaWindows.sln in Visual Studio 2022" -ForegroundColor White
Write-Host "3. Build SerenadaWebRtcNative (C++/CLI bridge)" -ForegroundColor White
Write-Host "4. Build SerenadaCore (C# SDK)" -ForegroundColor White
