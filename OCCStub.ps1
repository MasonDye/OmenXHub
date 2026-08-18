# OCCStub.ps1 - OMEN Light Studio "OGH presence" stub manager (standalone)
#
# Background: OMEN Light Studio checks at startup that the package
# "AD2F1837.OMENCommandCenter" (OMEN Command Center / OGH) is installed
#
# Source / issues: https://github.com/One1turn/OmenXHub
# (CheckOCCModule.dll -> PackageManager lookup). After uninstalling OGH,
# Light Studio refuses to work. This script registers an empty stub package
# with the EXACT same package identity (name + publisher) and a very high
# version, so the check passes. Light Studio drives hardware itself
# (Aurora.dll / Corsair stack) - OGH is only a presence check.
#
# Usage:
#   .\OCCStub.ps1            register the stub (default action)
#   .\OCCStub.ps1 -Remove    remove the stub (never touches a real OGH)
#   .\OCCStub.ps1 -Status    show Light Studio / OGH detection state
#
# Requirements:
#   - Windows Developer Mode enabled (allows unsigned loose-folder
#     registration via Add-AppxPackage -Register). No admin needed.
#   - Keep the OCCStub folder this script creates (registered package
#     points at it directly). Deleting it breaks the registration.
#
# Multi-user: registration is per-user. Every Windows user who wants
# Light Studio must run this script once under their own account.

param([switch]$Remove, [switch]$Status)

$ErrorActionPreference = 'Stop'
$StubDir   = Join-Path $PSScriptRoot 'OCCStub'
$ManifestPath = Join-Path $StubDir 'AppxManifest.xml'
$OccName   = 'AD2F1837.OMENCommandCenter'
$LsName    = 'AD2F1837.OMENLightStudio'

$Manifest = @'
<?xml version="1.0" encoding="utf-8"?>
<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
         xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
         xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
         IgnorableNamespaces="uap rescap">
  <Identity Name="AD2F1837.OMENCommandCenter"
            Publisher="CN=ED346674-0FA1-4272-85CE-3187C9C86E26"
            Version="9999.1.1.0" ProcessorArchitecture="x64" />
  <Properties>
    <DisplayName>OMEN Command Center</DisplayName>
    <PublisherDisplayName>HP Inc.</PublisherDisplayName>
    <Logo>assets\logo.png</Logo>
  </Properties>
  <Resources><Resource Language="en-US" /></Resources>
  <Dependencies>
    <TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.17763.0" MaxVersionTested="10.0.26100.0" />
  </Dependencies>
  <Capabilities>
    <rescap:Capability Name="runFullTrust" />
  </Capabilities>
  <Applications>
    <Application Id="OCCStub" Executable="stub.exe" EntryPoint="Windows.FullTrustApplication">
      <uap:VisualElements DisplayName="OMEN Command Center" Description="Presence stub"
        BackgroundColor="#1f1f1f" Square150x150Logo="assets\logo.png" Square44x44Logo="assets\logo.png" />
    </Application>
  </Applications>
</Package>
'@

# Standard 1x1 transparent PNG - only satisfies manifest logo validation.
$LogoB64 = 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAACklEQVR4nGMAAQAABQABDQottAAAAABJRU5ErkJggg=='

function Test-DevMode {
  try {
    return (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock' -ErrorAction Stop
      ).AllowDevelopmentWithoutDevLicense -eq 1
  } catch { return $false }
}

function Ensure-StubFiles {
  New-Item -ItemType Directory -Force -Path (Join-Path $StubDir 'assets') | Out-Null
  Set-Content -Path $ManifestPath -Value $Manifest -Encoding UTF8
  $stubExe = Join-Path $StubDir 'stub.exe'
  if (-not (Test-Path $stubExe)) {
    Copy-Item "$env:SystemRoot\System32\cmd.exe" $stubExe   # placeholder, never launched
  }
  [IO.File]::WriteAllBytes((Join-Path $StubDir 'assets\logo.png'), [Convert]::FromBase64String($LogoB64))
}

function Show-Status {
  $ls  = Get-AppxPackage -Name $LsName  -ErrorAction SilentlyContinue
  $occ = Get-AppxPackage -Name $OccName -ErrorAction SilentlyContinue
  Write-Host ("Light Studio : " + $(if ($ls) { "installed (" + $ls.Version + ")" } else { "NOT installed" }))
  if ($occ) {
    $kind = if ($occ.Version -like '9999.*') { "stub registered (" + $occ.Version + ")" } else { "REAL OGH present (" + $occ.Version + ")" }
    Write-Host ("OGH          : $kind")
  } else {
    Write-Host "OGH          : not registered"
  }
}

if ($Status) { Show-Status; return }

if ($Remove) {
  # Guard: only ever remove OUR stub (version 9999.*), never a real OGH.
  $stub = Get-AppxPackage -Name $OccName -ErrorAction SilentlyContinue | Where-Object { $_.Version -like '9999.*' }
  if ($stub) {
    $stub | Remove-AppxPackage
    Write-Host "Stub removed."
  } else {
    Write-Host "No stub found (real OGH, if any, was left untouched)."
  }
  Show-Status
  return
}

# Default: register
if (-not (Test-DevMode)) {
  Write-Host "ERROR: Windows Developer Mode is OFF." -ForegroundColor Red
  Write-Host "Enable it: Settings > Privacy & security > For developers > Developer Mode, then re-run."
  exit 1
}
Ensure-StubFiles
Add-AppxPackage -Register $ManifestPath
Write-Host "Stub registered."
Write-Host ("Keep this folder (registered package points here): " + $StubDir)
Show-Status
