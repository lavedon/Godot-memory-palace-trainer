[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$godot = Join-Path $projectRoot '.tools/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe'
& "$PSScriptRoot/bootstrap.ps1"
Push-Location $projectRoot
try {
    New-Item -ItemType Directory -Path 'artifacts/windows' -Force | Out-Null
    Set-Content -LiteralPath 'artifacts/.gdignore' -Value ''
    dotnet build PalaceRoomViewer.csproj --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    & $godot --headless --path $projectRoot --editor --import
    if ($LASTEXITCODE -ne 0) { throw 'Godot import failed.' }
    $exportLog = Join-Path $projectRoot 'artifacts/export.log'
    & $godot --headless --path $projectRoot --log-file $exportLog --export-release 'Windows Desktop' 'artifacts/windows/PalaceRoomViewer.exe'
    if ($LASTEXITCODE -ne 0) { throw 'Windows export failed.' }
    if (Select-String -LiteralPath $exportLog -Pattern '^ERROR:' -Quiet) { throw "Godot reported an export error. See $exportLog" }
    if (-not (Get-ChildItem -LiteralPath 'artifacts/windows' -Recurse -Filter 'e_sqlite3.dll')) { throw 'Native SQLite dependency is missing from export.' }
    if (-not (Get-ChildItem -LiteralPath 'artifacts/windows' -Recurse -Filter 'coreclr.dll')) { throw 'Self-contained .NET runtime is missing from export.' }
    @'
# Palace Room Viewer — Windows x64

Keep this whole folder together. Open PowerShell in this folder and run:

    .\PalaceRoomViewer.exe -- --room 8
    .\PalaceRoomViewer.exe -- --room 7 --db 'D:\my data\palace.db'

The Room ID is required. The database defaults to C:\tools\Data\palace.db.
The viewer opens SQLite read-only. Missing inputs show instructions in the window.
Godot, a .NET installation, and the shared C:\tools\e_sqlite3.dll are not required.

WASD: walk. Mouse: look. J: toggle text. K: toggle numbered markers.
Escape: release mouse. Click the Room: resume. Alt+F4: close.
Aim at a Position with text enabled to read; release the mouse to scroll long text.
The teal FRONT wall corresponds to the bottom edge of the room diagrams.
'@ | Set-Content -LiteralPath 'artifacts/windows/README.md'
    Write-Host 'Export ready: artifacts/windows/PalaceRoomViewer.exe'
} finally { Pop-Location }
