# build.ps1 - One-click build script for OmenXHub
# Usage: powershell -ExecutionPolicy Bypass -File build.ps1
#        or double-click build.bat

[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Project = "OmenSuperHub.csproj",
    [switch]$NoRun,
    [switch]$Package
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "  OmenXHub Build Script" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Project:       $Project"
Write-Host "Configuration: $Configuration"
Write-Host "Root:          $root"
Write-Host ""

# Locate dotnet
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    Write-Host "[ERROR] dotnet SDK not found on PATH." -ForegroundColor Red
    Write-Host "        Install .NET SDK (net481 targeting pack required) from https://dotnet.microsoft.com"
    exit 1
}
Write-Host "[INFO] Using dotnet: $($dotnet.Source)"
Write-Host ""

# Build
Write-Host "[BUILD] Starting build..." -ForegroundColor Yellow
$buildArgs = @("build", $Project, "-c", $Configuration, "-p:Platform=x64", "--nologo")
Write-Host "[BUILD] dotnet $($buildArgs -join ' ')"
Write-Host ""

& dotnet @buildArgs
$exitCode = $LASTEXITCODE

Write-Host ""
if ($exitCode -ne 0) {
    Write-Host "[FAILED] Build failed with exit code $exitCode." -ForegroundColor Red
    exit $exitCode
}

Write-Host "[OK] Build succeeded." -ForegroundColor Green

# Show output location
$outDir = Join-Path $root "bin\x64\$Configuration\net481\"
$exe = Join-Path $outDir "OmenXHub.exe"
if (Test-Path $exe) {
    Write-Host ""
    Write-Host "[OUTPUT] $exe" -ForegroundColor Cyan
    Write-Host "         Size: $((Get-Item $exe).Length / 1KB) KB"
} else {
    Write-Host "[WARN] Expected output not found: $exe" -ForegroundColor Yellow
    exit 1
}

if ($Package) {
    $required = @(
        "OmenXHub.exe", "OmenXHub.exe.config", "LibreHardwareMonitorLib.dll",
        "HP.Omen.Core.Common.dll", "HP.Omen.Core.Model.Device.dll",
        "HP.Omen.Core.Api.dll", "PerformanceControl.dll",
        "System.Threading.Tasks.Extensions.dll", "SMBiosSDK.dll", "Utf8Json.dll"
    )
    $missing = $required | Where-Object { -not (Test-Path (Join-Path $outDir $_)) }
    if ($missing) {
        Write-Host "[FAILED] Missing package files: $($missing -join ', ')" -ForegroundColor Red
        exit 1
    }

    $iscc = @(
        (Get-Command ISCC.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue),
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    if (-not $iscc) {
        Write-Host "[WARN] ISCC.exe not found. Install Inno Setup 6 to build the installer." -ForegroundColor Yellow
        exit 0
    }

    $iss = Join-Path $root "installer\OmenXHub.iss"
    Write-Host "[PACKAGE] Compiling installer..." -ForegroundColor Yellow
    & $iscc $iss
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    Write-Host "[OK] Installer created in $root\dist" -ForegroundColor Green
    exit 0
}

# Launch the app (app.manifest requires admin, so UAC prompt will appear)
if ($NoRun) {
    Write-Host ""
    Write-Host "Done." -ForegroundColor Green
    exit 0
}

Write-Host ""
Write-Host "[RUN] Launching OmenXHub.exe (UAC prompt expected)..." -ForegroundColor Yellow
try {
    # ponytail: Use Start-Process -Verb RunAs so the admin manifest is honored.
    # Without -Verb RunAs, an elevated parent shell would inherit elevation silently;
    # with it, UAC is always shown, matching double-click behavior.
    Start-Process -FilePath $exe -Verb RunAs
    Write-Host "[OK] Launched." -ForegroundColor Green
} catch {
    Write-Host "[WARN] Launch failed or was declined by user: $($_.Exception.Message)" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Done." -ForegroundColor Green
