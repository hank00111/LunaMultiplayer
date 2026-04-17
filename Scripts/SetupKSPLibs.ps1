#Requires -Version 5.1
# ============================================================================
# scripts/SetupKSPLibs.ps1 -- Populate External/KSPLibraries/ from KSP install
#
# LmpClient.csproj references KSP + Unity DLLs via relative HintPaths rooted at
# External/KSPLibraries/. Upstream does NOT ship these DLLs (only a password-
# protected KSPLibraries.7z for CI). Before `scripts/build-lmp-projects.bat`
# can build LmpClient, these DLLs must be copied from a KSP 1.12.5 install.
#
# Usage (PowerShell 5.1 or PowerShell 7+):
#   .\scripts\SetupKSPLibs.ps1 -KspPath "C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program"
#
# If -KspPath is omitted, the script reads KSPPATH from scripts\SetDirectories.bat.
# ============================================================================

[CmdletBinding()]
param(
    [string]$KspPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$RepoRoot  = Resolve-Path (Join-Path $PSScriptRoot '..')
$LibDir    = Join-Path $RepoRoot 'External\KSPLibraries'
$SetDirs   = Join-Path $PSScriptRoot 'SetDirectories.bat'

if (-not $KspPath -and (Test-Path $SetDirs)) {
    $kspLine = Select-String -Path $SetDirs -Pattern '^\s*SET\s+KSPPATH\s*=\s*(.+)$' | Select-Object -First 1
    if ($kspLine) {
        $KspPath = $kspLine.Matches[0].Groups[1].Value.Trim('"').Trim()
        Write-Host "Using KSPPATH from SetDirectories.bat: $KspPath"
    }
}

if (-not $KspPath) {
    throw "KSP path not provided. Pass -KspPath or set KSPPATH in scripts\SetDirectories.bat."
}

$KspManaged = Join-Path $KspPath 'KSP_x64_Data\Managed'
if (-not (Test-Path $KspManaged)) {
    throw "KSP Managed folder not found: $KspManaged (is -KspPath pointing to KSP 1.12.5 install root?)"
}

if (-not (Test-Path $LibDir)) {
    New-Item -ItemType Directory -Path $LibDir | Out-Null
}

# DLLs referenced by LmpClient.csproj <HintPath>..\External\KSPLibraries\...</HintPath>
$KspDlls = @(
    'Assembly-CSharp.dll',
    'Assembly-CSharp-firstpass.dll',
    'System.dll',
    'System.Xml.dll',
    'UnityEngine.dll',
    'UnityEngine.CoreModule.dll',
    'UnityEngine.AnimationModule.dll',
    'UnityEngine.AssetBundleModule.dll',
    'UnityEngine.IMGUIModule.dll',
    'UnityEngine.ImageConversionModule.dll',
    'UnityEngine.InputLegacyModule.dll',
    'UnityEngine.PhysicsModule.dll',
    'UnityEngine.TextRenderingModule.dll',
    'UnityEngine.UI.dll',
    'UnityEngine.UIModule.dll'
)

$copied  = 0
$missing = @()
foreach ($dll in $KspDlls) {
    $src = Join-Path $KspManaged $dll
    $dst = Join-Path $LibDir $dll
    if (Test-Path $src) {
        Copy-Item -Path $src -Destination $dst -Force
        $copied++
    } else {
        $missing += $dll
    }
}

Write-Host "Copied $copied/$($KspDlls.Count) KSP DLLs to $LibDir"
if ($missing.Count -gt 0) {
    Write-Warning "Missing DLLs in KSP install (HintPath may fail): $($missing -join ', ')"
    Write-Warning "Verify KSP version is 1.12.5 and the Managed folder is complete."
    exit 1
}

Write-Host "Done. You can now run: scripts\build-lmp-projects.bat"
