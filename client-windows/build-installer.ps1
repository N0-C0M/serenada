param(
    [ValidateSet('x64')]
    [string]$Architecture = 'x64',

    [string]$Version = '1.0.0'
)

$ErrorActionPreference = 'Stop'

& (Join-Path $PSScriptRoot 'publish.ps1') -Architecture $Architecture
if ($LASTEXITCODE -ne 0) {
    throw "Publishing Serenada failed with exit code $LASTEXITCODE."
}

$programFilesX86 = [Environment]::GetFolderPath(
    [Environment+SpecialFolder]::ProgramFilesX86)
$candidates = @(
    (Join-Path $programFilesX86 'Inno Setup 6\ISCC.exe'),
    (Join-Path ${env:ProgramFiles} 'Inno Setup 6\ISCC.exe'),
    'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
    'C:\Program Files\Inno Setup 6\ISCC.exe'
) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }

$iscc = $candidates | Select-Object -First 1
if (-not $iscc) {
    throw @"
Inno Setup 6 was not found.
Install it, then rerun:
  .\client-windows\build-installer.ps1

GitHub Actions installs Inno Setup automatically and produces the single EXE.
"@
}

$script = Join-Path $PSScriptRoot 'installer\Serenada.iss'
& $iscc "/DMyAppVersion=$Version" $script
if ($LASTEXITCODE -ne 0) {
    throw "Building Serenada installer failed with exit code $LASTEXITCODE."
}

$installer = Join-Path $PSScriptRoot 'artifacts\installer\Serenada-Setup-x64.exe'
if (-not (Test-Path -LiteralPath $installer)) {
    throw "Installer build completed without the expected output: $installer"
}

Write-Host "Single-file installer ready: $installer"
