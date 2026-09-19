[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$exportDirectory = Join-Path $projectRoot 'artifacts/windows'
if (-not (Test-Path -LiteralPath (Join-Path $exportDirectory 'PalaceRoomViewer.exe'))) {
    & "$PSScriptRoot/export.ps1"
}
$archive = Join-Path $projectRoot 'artifacts/PalaceRoomViewer-Windows-x64.zip'
Compress-Archive -LiteralPath $exportDirectory -DestinationPath $archive -Force
Get-FileHash -LiteralPath $archive -Algorithm SHA256 | Format-List
Write-Host "Packaged: $archive"
