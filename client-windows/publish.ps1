param(
    [ValidateSet('x64', 'x86')]
    [string]$Architecture = 'x64'
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'SerenadaApp\SerenadaApp.csproj'
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'artifacts'))
$output = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot "SerenadaApp-win-$Architecture"))
$expectedPrefix = $artifactsRoot.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar
) + [System.IO.Path]::DirectorySeparatorChar

if (-not $output.StartsWith($expectedPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to clear publish output outside $artifactsRoot"
}

if (Test-Path -LiteralPath $output) {
    Remove-Item -LiteralPath $output -Recurse -Force
}

dotnet publish $project `
    --configuration Release `
    --runtime "win-$Architecture" `
    --self-contained true `
    -p:Platform=$Architecture `
    --output $output

if ($LASTEXITCODE -ne 0) {
    throw "Publishing SerenadaApp failed with exit code $LASTEXITCODE."
}

$requiredFiles = @(
    (Join-Path $output 'SerenadaApp.exe'),
    (Join-Path $output 'mrwebrtc.dll')
)
foreach ($requiredFile in $requiredFiles) {
    if (-not (Test-Path -LiteralPath $requiredFile)) {
        throw "Published application is incomplete: missing $requiredFile"
    }
}

Write-Host "Ready to run: $(Join-Path $output 'SerenadaApp.exe')"
