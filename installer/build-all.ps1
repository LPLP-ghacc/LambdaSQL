# ============================================================
# LambdaSQL — Master build script
# Publishes all projects for Windows (x64) and Linux (x64),
# then optionally builds the Windows Inno Setup installer.
# ============================================================

param(
    [string]$Version    = "1.0.0",
    [string]$OutputDir  = "$PSScriptRoot\dist",
    [switch]$NoInstaller          # skip Inno Setup step
)

$ErrorActionPreference = "Stop"
$Root = Resolve-Path "$PSScriptRoot\.."

function Step($msg) {
    Write-Host "`n==> $msg" -ForegroundColor Cyan
}

function Publish($project, $rid, $outName) {
    $dest = "$OutputDir\$rid\$outName"
    Step "Publishing $outName ($rid)"
    dotnet publish "$Root\$project\$project.csproj" `
        -c Release `
        -r $rid `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:PublishTrimmed=false `
        -p:Version=$Version `
        -o $dest
    Write-Host "  -> $dest" -ForegroundColor Green
}

# ── Clean ────────────────────────────────────────────────────
Step "Cleaning dist"
if (Test-Path $OutputDir) { Remove-Item $OutputDir -Recurse -Force }
New-Item -ItemType Directory -Path $OutputDir | Out-Null

# ── Publish Windows x64 ──────────────────────────────────────
Publish "LambdaSQL.Server" "win-x64"  "server"
Publish "LambdaSQL.Cli"    "win-x64"  "cli"
Publish "LambdaSQL.Web"    "win-x64"  "web"

# ── Publish Linux x64 ────────────────────────────────────────
Publish "LambdaSQL.Server" "linux-x64" "server"
Publish "LambdaSQL.Cli"    "linux-x64" "cli"
Publish "LambdaSQL.Web"    "linux-x64" "web"

# ── Copy Linux scripts ────────────────────────────────────────
Step "Copying Linux installer scripts"
$linuxDist = "$OutputDir\linux-x64"
Copy-Item "$PSScriptRoot\linux\install.sh"   $linuxDist
Copy-Item "$PSScriptRoot\linux\uninstall.sh" $linuxDist

# ── Windows Inno Setup ───────────────────────────────────────
if (-not $NoInstaller) {
    Step "Building Windows installer"
    & "$PSScriptRoot\windows\build-installer.ps1" -Version $Version -OutputDir $OutputDir
} else {
    Write-Host "  Skipped (--NoInstaller)" -ForegroundColor Yellow
}

Step "Done"
Write-Host "Output: $OutputDir" -ForegroundColor Green
