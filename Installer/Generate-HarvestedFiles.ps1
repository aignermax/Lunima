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

# Start building the WiX fragment
$xml = @"
<?xml version="1.0" encoding="UTF-8"?>
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
    <Fragment>
        <ComponentGroup Id="HarvestedAppFiles">
"@

# Generate a component for each file
$componentIndex = 0
foreach ($file in $files) {
    # Get relative path from publish directory (already starts with \)
    $relativePath = $file.FullName.Substring($PublishDirResolved.Length)

    $componentId = "cmp" + $componentIndex.ToString("D4")
    $fileId = "fil" + $componentIndex.ToString("D4")

    # Escape XML special characters
    $relativePath = [System.Security.SecurityElement]::Escape($relativePath)

    $xml += @"

            <Component Id="$componentId" Directory="INSTALLDIR" Guid="*">
                <File Id="$fileId" Source="`$(var.PublishDir)$relativePath" KeyPath="yes" />
            </Component>
"@
    $componentIndex++
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
