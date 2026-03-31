<#
.SYNOPSIS
    Builds the Lunima MSI installer.

.DESCRIPTION
    1. Installs / updates the WiX dotnet tool (wix v4).
    2. Generates the Lunima application icon if not present.
    3. Publishes the CAP.Desktop project (framework-dependent, win-x64).
    4. Builds the WiX installer project (Installer/Installer.wixproj).
    5. Copies the MSI to the artifacts/ directory.

.PARAMETER Configuration
    Build configuration: Release (default) or Debug.

.PARAMETER Version
    Override the application version embedded in the MSI.
    When omitted, the version is read from CAP.Desktop/CAP.Desktop.csproj.

.PARAMETER SelfContained
    When specified, creates a self-contained installer (~150 MB) that bundles
    the .NET 8 runtime.  Default is framework-dependent (requires .NET 8 to
    be installed on the target machine).

.EXAMPLE
    .\scripts\build_installer.ps1
    .\scripts\build_installer.ps1 -Configuration Debug
    .\scripts\build_installer.ps1 -SelfContained
    .\scripts\build_installer.ps1 -Version 1.0.0

.NOTES
    Prerequisites:
        - .NET 8 SDK (https://dotnet.microsoft.com/download/dotnet/8.0)
        - Python 3 with Pillow  (pip install Pillow)
          Only needed when LunimaIcon.ico does not yet exist.
#>

[CmdletBinding()]
param(
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release",

    [string]$Version = "",

    [switch]$SelfContained
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ─────────────────────────────────────────────────────────────────────────────
# Resolve paths
# ─────────────────────────────────────────────────────────────────────────────
$RepoRoot      = Split-Path -Parent $PSScriptRoot
$PublishDir    = Join-Path $RepoRoot "publish\win-x64"
$InstallerDir  = Join-Path $RepoRoot "Installer"
$ArtifactsDir  = Join-Path $RepoRoot "artifacts"
$IconPath      = Join-Path $InstallerDir "LunimaIcon.ico"
$IconScript    = Join-Path $PSScriptRoot "generate_icon.py"
$DesktopProj   = Join-Path $RepoRoot "CAP.Desktop\CAP.Desktop.csproj"
$InstallerProj = Join-Path $InstallerDir "Installer.wixproj"

if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$desktopProject = Get-Content $DesktopProj
    $Version = $desktopProject.Project.PropertyGroup.Version

    if ([string]::IsNullOrWhiteSpace($Version)) {
        throw "Could not determine version from $DesktopProj."
    }
}

Write-Host ""
Write-Host "═══════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "   Lunima MSI Installer Build" -ForegroundColor Cyan
Write-Host "   Configuration : $Configuration" -ForegroundColor Cyan
Write-Host "   Version       : $Version" -ForegroundColor Cyan
Write-Host "   Self-Contained: $SelfContained" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

# ─────────────────────────────────────────────────────────────────────────────
# Step 1 – Ensure WiX v4 dotnet tool is installed
# ─────────────────────────────────────────────────────────────────────────────
Write-Host "[1/5] Checking WiX toolset …" -ForegroundColor Yellow

$wixVersion = & dotnet tool run wix -- --version 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Host "      WiX not found – installing …" -ForegroundColor DarkYellow
    & dotnet tool install --global wix --version 4.0.5
    if ($LASTEXITCODE -ne 0) {
        # Try latest if 4.0.5 is unavailable
        & dotnet tool install --global wix
    }
    # Add WiX extensions
    & wix extension add WixToolset.UI.wixext/4.0.5    2>$null
    & wix extension add WixToolset.NetFx.wixext/4.0.5 2>$null
} else {
    Write-Host "      WiX $wixVersion already installed." -ForegroundColor Green
}

# ─────────────────────────────────────────────────────────────────────────────
# Step 2 – Generate application icon
# ─────────────────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "[2/5] Checking Lunima icon …" -ForegroundColor Yellow

if (-not (Test-Path $IconPath)) {
    Write-Host "      Icon not found – generating with Python …" -ForegroundColor DarkYellow
    $python = Get-Command python3 -ErrorAction SilentlyContinue
    if (-not $python) { $python = Get-Command python -ErrorAction SilentlyContinue }
    if (-not $python) {
        throw "Python not found. Please install Python and Pillow, then run: python scripts/generate_icon.py"
    }
    & $python.Source $IconScript --output $IconPath
    if ($LASTEXITCODE -ne 0) { throw "Icon generation failed." }
} else {
    Write-Host "      Icon exists: $IconPath" -ForegroundColor Green
}

# ─────────────────────────────────────────────────────────────────────────────
# Step 3 – Publish the desktop application
# ─────────────────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "[3/5] Publishing CAP.Desktop ($Configuration, win-x64) …" -ForegroundColor Yellow

if (Test-Path $PublishDir) { Remove-Item -Recurse -Force $PublishDir }

$publishArgs = @(
    "publish", $DesktopProj,
    "--configuration", $Configuration,
    "--runtime", "win-x64",
    "--output", $PublishDir,
    "-p:Version=$Version"
)

if ($SelfContained) {
    $publishArgs += "--self-contained", "true"
    $publishArgs += "-p:PublishSingleFile=true"
} else {
    $publishArgs += "--self-contained", "false"
    $publishArgs += "--no-self-contained"
}

& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

Write-Host "      Published to: $PublishDir" -ForegroundColor Green

# ─────────────────────────────────────────────────────────────────────────────
# Step 4 – Build the WiX installer
# ─────────────────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "[4/5] Building MSI installer …" -ForegroundColor Yellow

# Ensure trailing separator so WiX HarvestDirectory works correctly
$publishDirSlash = $PublishDir.TrimEnd('\') + '\'

& dotnet build $InstallerProj `
    --configuration $Configuration `
    "-p:PublishDir=$publishDirSlash" `
    "-p:ProductVersion=$Version"

if ($LASTEXITCODE -ne 0) { throw "WiX build failed." }

# ─────────────────────────────────────────────────────────────────────────────
# Step 5 – Copy MSI to artifacts/
# ─────────────────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "[5/5] Copying artifacts …" -ForegroundColor Yellow

$msiSource = Join-Path $InstallerDir "bin\$Configuration\Lunima-Setup.msi"
if (-not (Test-Path $msiSource)) {
    # WiX may place it in a runtime-qualified subfolder
    $msiSource = Get-ChildItem -Path (Join-Path $InstallerDir "bin") -Filter "Lunima-Setup.msi" -Recurse |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}

if (-not $msiSource -or -not (Test-Path $msiSource)) {
    throw "Could not locate Lunima-Setup.msi after build."
}

if (-not (Test-Path $ArtifactsDir)) { New-Item -ItemType Directory -Path $ArtifactsDir | Out-Null }

$msiDest = Join-Path $ArtifactsDir "Lunima-Setup-$Version.msi"
Copy-Item -Force $msiSource $msiDest

Write-Host ""
Write-Host "═══════════════════════════════════════════════════" -ForegroundColor Green
Write-Host "   BUILD SUCCESSFUL" -ForegroundColor Green
Write-Host "   MSI: $msiDest" -ForegroundColor Green
Write-Host "   Size: $([math]::Round((Get-Item $msiDest).Length / 1MB, 1)) MB" -ForegroundColor Green
Write-Host "═══════════════════════════════════════════════════" -ForegroundColor Green
