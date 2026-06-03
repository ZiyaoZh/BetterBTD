param(
    [Parameter(Mandatory = $true)]
    [string]$BuilderPath,
    [Parameter(Mandatory = $true)]
    [string]$ConfigPath,
    [Parameter(Mandatory = $true)]
    [string]$InputDir,
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [Parameter(Mandatory = $true)]
    [string]$OutputDir
)

$ErrorActionPreference = "Stop"

$builder = (Resolve-Path $BuilderPath).Path
$config = (Resolve-Path $ConfigPath).Path
$publishDir = (Resolve-Path $InputDir).Path
$customPanelPath = Join-Path $PSScriptRoot "kachina-left.webp"
$customCssPath = Join-Path $PSScriptRoot "kachina-custom.css"
$customIconPath = Join-Path $PSScriptRoot "kachina-icon.ico"

$updaterBrandingArgs = @()
$installerBrandingArgs = @()
if (Test-Path $customIconPath) {
    $resolvedIconPath = (Resolve-Path $customIconPath).Path
    $updaterBrandingArgs += @("--icon", $resolvedIconPath)
    $installerBrandingArgs += @("--icon", $resolvedIconPath)
}

$customMediaPath = $null
if (Test-Path $customPanelPath) {
    $customMediaPath = (Resolve-Path $customPanelPath).Path
} elseif (Test-Path $customCssPath) {
    $customMediaPath = (Resolve-Path $customCssPath).Path
}

if ($null -ne $customMediaPath) {
    $updaterBrandingArgs += @("-t", $customMediaPath)
}

if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir | Out-Null
}

$releaseDir = (Resolve-Path $OutputDir).Path
$tempDir = Join-Path $releaseDir "kachina-temp"
$hashedDir = Join-Path $tempDir "hashed"
$metadataPath = Join-Path $tempDir "BetterBTD.metadata.json"
$updaterPath = Join-Path $releaseDir "BetterBTD.update.exe"
$installerPath = Join-Path $releaseDir "BetterBTD.Install.exe"
$packagedUpdaterPath = Join-Path $publishDir "BetterBTD.update.exe"

if (Test-Path $tempDir) {
    Remove-Item -LiteralPath $tempDir -Recurse -Force
}

New-Item -ItemType Directory -Path $tempDir | Out-Null

if (Test-Path $updaterPath) {
    Remove-Item -LiteralPath $updaterPath -Force
}

if (Test-Path $installerPath) {
    Remove-Item -LiteralPath $installerPath -Force
}

& $builder pack -c $config -o $updaterPath @updaterBrandingArgs
if ($LASTEXITCODE -ne 0) {
    throw "Kachina updater generation failed."
}

Copy-Item -LiteralPath $updaterPath -Destination $packagedUpdaterPath -Force

& $builder gen -j 8 -i $publishDir -m $metadataPath -o $hashedDir -r BetterBTD -t $Version -u $updaterPath
if ($LASTEXITCODE -ne 0) {
    throw "Kachina metadata generation failed."
}

& $builder pack -c $config -m $metadataPath -d $hashedDir -o $installerPath @installerBrandingArgs
if ($LASTEXITCODE -ne 0) {
    throw "Kachina installer generation failed."
}

if (Test-Path $updaterPath) {
    Remove-Item -LiteralPath $updaterPath -Force
}

Remove-Item -LiteralPath $tempDir -Recurse -Force
