# Generate HarvestedFiles.wxs for WiX v4
# This script creates a WiX fragment that includes all files from the publish directory

param(
    [Parameter(Mandatory=$true)]
    [string]$PublishDir,

    [Parameter(Mandatory=$false)]
    [string]$OutputFile = "HarvestedFiles.wxs"
)

$ErrorActionPreference = "Stop"

# Ensure publish directory exists
if (-not (Test-Path $PublishDir)) {
    Write-Error "Publish directory not found: $PublishDir"
    exit 1
}

# Resolve to absolute path and extract the string
$PublishDirResolved = (Resolve-Path $PublishDir).Path

# Get all files recursively
$files = Get-ChildItem -Path $PublishDirResolved -Recurse -File

# Group files by directory
$directories = @{}
foreach ($file in $files) {
    $relativePath = $file.FullName.Substring($PublishDirResolved.Length).TrimStart('\', '/')
    $directory = Split-Path $relativePath -Parent
    if ([string]::IsNullOrEmpty($directory)) {
        $directory = "."
    }
    if (-not $directories.ContainsKey($directory)) {
        $directories[$directory] = @()
    }
    $directories[$directory] += $file
}

# Start building the WiX fragment
$xml = @"
<?xml version="1.0" encoding="UTF-8"?>
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
    <Fragment>
"@

# Generate directory structure
$directoryIds = @{}
$directoryIds["."] = "INSTALLDIR"

# Build directory hierarchy
$sortedDirs = $directories.Keys | Where-Object { $_ -ne "." } | Sort-Object
foreach ($dir in $sortedDirs) {
    $dirParts = $dir.Split('\')
    $dirId = "DIR_" + ($dir -replace '[\\\/]', '_')
    $dirName = $dirParts[-1]

    # Find parent directory
    $parentDir = Split-Path $dir -Parent
    if ([string]::IsNullOrEmpty($parentDir)) {
        $parentDirId = "INSTALLDIR"
    } else {
        $parentDirId = $directoryIds[$parentDir]
    }

    $directoryIds[$dir] = $dirId

    # Add directory definition
    $xml += @"

        <DirectoryRef Id="$parentDirId">
            <Directory Id="$dirId" Name="$dirName" />
        </DirectoryRef>
"@
}

$xml += @"

        <ComponentGroup Id="HarvestedAppFiles">
"@

# Generate a component for each file
$componentIndex = 0
foreach ($dir in ($directories.Keys | Sort-Object)) {
    $dirId = $directoryIds[$dir]

    foreach ($file in ($directories[$dir] | Sort-Object -Property Name)) {
        $relativePath = $file.FullName.Substring($PublishDirResolved.Length)

        $componentId = "cmp" + $componentIndex.ToString("D4")
        $fileId = "fil" + $componentIndex.ToString("D4")

        # Escape XML special characters
        $relativePath = [System.Security.SecurityElement]::Escape($relativePath)

        $xml += @"

            <Component Id="$componentId" Directory="$dirId" Guid="*">
                <File Id="$fileId" Source="`$(var.PublishDir)$relativePath" KeyPath="yes" />
            </Component>
"@
        $componentIndex++
    }
}

$xml += @"

        </ComponentGroup>
    </Fragment>
</Wix>
"@

# Write to output file
$outputPath = Join-Path (Split-Path $PSScriptRoot -Parent) "Installer" $OutputFile
$xml | Out-File -FilePath $outputPath -Encoding UTF8
Write-Host "Generated $outputPath with $componentIndex files" -ForegroundColor Green
