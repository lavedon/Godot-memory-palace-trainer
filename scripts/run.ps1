[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][long]$Room,
    [string]$Database = 'C:\tools\Data\palace.db'
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$godot = Join-Path $projectRoot '.tools/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64.exe'
if (-not (Test-Path -LiteralPath $godot)) { & "$PSScriptRoot/bootstrap.ps1" }
Push-Location $projectRoot
try {
    dotnet build PalaceRoomViewer.csproj --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    & $godot --headless --path $projectRoot --editor --import
    if ($LASTEXITCODE -ne 0) { throw 'Godot import failed.' }
    & $godot --path $projectRoot -- --room $Room --db $Database
} finally { Pop-Location }
