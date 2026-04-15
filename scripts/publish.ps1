<#
.SYNOPSIS
    Publishes Elysium Wallpaper as a self-contained Windows app and packages it as a zip.

.DESCRIPTION
    Produces a self-contained directory under bin\Release\<tfm>\<rid>\publish\ containing
    the managed exe + all .NET runtime + WindowsAppSDK native DLLs. End users only need to
    unzip and double-click ElysiumWallpaper.exe — no .NET install required.

    WinUI 3 unpackaged apps cannot be a literal single-file exe; native DLLs must remain
    loose. The output zip bundles everything into one downloadable archive.

.PARAMETER Rid
    Runtime identifier. Defaults to win-x64. Use win-arm64 for ARM machines.

.PARAMETER Configuration
    Release (default) or Debug.

.EXAMPLE
    .\scripts\publish.ps1
    Produces dist\ElysiumWallpaper-win-x64.zip

.EXAMPLE
    .\scripts\publish.ps1 -Rid win-arm64
#>
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64', 'win-x86')]
    [string]$Rid = 'win-x64',
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot 'ElysiumWallpaper.WinUI\ElysiumWallpaper.csproj'
$distDir = Join-Path $repoRoot 'dist'

# Map RID to the platform name used in the publish profile.
$platform = switch ($Rid) {
    'win-x64' { 'x64' }
    'win-arm64' { 'ARM64' }
    'win-x86' { 'x86' }
}

Write-Host "Publishing $Rid ($Configuration)..." -ForegroundColor Cyan

# Stop any running instance so the file copy doesn't fail.
Get-Process ElysiumWallpaper -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host "Stopping running ElysiumWallpaper (PID $($_.Id))" -ForegroundColor Yellow
    Stop-Process -Id $_.Id -Force
    Start-Sleep -Seconds 1
}

# PublishProfile name matches the RID (win-x64.pubxml lives at Properties\PublishProfiles\).
dotnet publish $projectPath `
    -c $Configuration `
    -p:Platform=$platform `
    -p:RuntimeIdentifier=$Rid `
    -p:PublishProfile=$Rid `
    --nologo

if ($LASTEXITCODE -ne 0) {
    Write-Error "Publish failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

# Locate the publish output. csproj computes PublishDir as bin\<config>\<tfm>\<rid>\publish\.
$tfm = 'net10.0-windows10.0.19041.0'
$publishDir = Join-Path $repoRoot "ElysiumWallpaper.WinUI\bin\$Configuration\$tfm\$Rid\publish"

if (-not (Test-Path $publishDir)) {
    # Some configurations produce bin\<platform>\<config>\... instead.
    $publishDir = Join-Path $repoRoot "ElysiumWallpaper.WinUI\bin\$platform\$Configuration\$tfm\$Rid\publish"
}

if (-not (Test-Path $publishDir)) {
    Write-Error "Publish directory not found. Tried both bin\$Configuration and bin\$platform\$Configuration."
    exit 1
}

Write-Host "Publish output: $publishDir" -ForegroundColor Green

# Ensure dist exists, then create the zip.
New-Item -ItemType Directory -Path $distDir -Force | Out-Null
$zipPath = Join-Path $distDir "ElysiumWallpaper-$Rid.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

Write-Host "Compressing to $zipPath..." -ForegroundColor Cyan
Compress-Archive -Path "$publishDir\*" -DestinationPath $zipPath -CompressionLevel Optimal

$zipSize = (Get-Item $zipPath).Length / 1MB
Write-Host ("Done. {0} ({1:N1} MB)" -f $zipPath, $zipSize) -ForegroundColor Green
