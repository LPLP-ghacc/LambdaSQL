# ============================================================
# Builds the Windows .exe installer using Inno Setup.
# Requires Inno Setup 6 installed:
#   https://jrsoftware.org/isdl.php
# ============================================================

param(
    [string]$Version   = "1.0.0",
    [string]$OutputDir = "$PSScriptRoot\..\dist"
)

$ErrorActionPreference = "Stop"

# Locate iscc.exe
$iscc = @(
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    Write-Warning "Inno Setup not found. Download from https://jrsoftware.org/isdl.php"
    Write-Warning "Skipping installer build."
    exit 0
}

$iss    = "$PSScriptRoot\setup.iss"
$outDir = "$OutputDir\installer"
New-Item -ItemType Directory -Path $outDir -Force | Out-Null

Write-Host "Building installer with Inno Setup..." -ForegroundColor Cyan
& $iscc $iss `
    /DMyAppVersion=$Version `
    /DDistDir=$OutputDir `
    /DOutputDir=$outDir

if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed with exit code $LASTEXITCODE"
}

Write-Host "Installer: $outDir\LambdaSQL-Setup-$Version.exe" -ForegroundColor Green
