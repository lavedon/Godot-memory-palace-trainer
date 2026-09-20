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
    .\PalaceRoomViewer.exe -- --room 8 --room-textures 'D:\palace walls'
    .\PalaceRoomViewer.exe -- --room 8 --left 'D:\pictures\left.png' --forward 'D:\pictures\front.jpg'

The Room ID is required. The database defaults to C:\tools\Data\palace.db.
Use absolute --db and CLI image paths when launching from another directory;
Godot resolves relative CLI paths from this executable's folder.
The viewer opens SQLite read-only. Missing inputs show instructions in the window.
Godot, a .NET installation, and the shared C:\tools\e_sqlite3.dll are not required.

WASD: walk. Mouse: look. J: toggle all text. L: toggle the Locus under the crosshair.
K: toggle numbered markers. L works on hidden text and leaves other Loci unchanged.
J hides all text if any is visible; otherwise it shows all text.
Escape: release mouse. Click the Room: resume. Alt+F4: close.
Aim at a Position with text enabled to read; release the mouse to scroll long text.
The FRONT wall corresponds to the bottom edge of the room diagrams.

Room images load automatically from Rooms.LeftImagePath, RightImagePath,
ForwardImagePath, BackImagePath, FloorImagePath, and CeilingImagePath. Store local
file paths, either absolute or relative to the database's folder. NULL or blank
values keep the default surface. Old databases without these columns still work.
The viewer never migrates or edits a database. Use the project's separate
scripts/migrate-room-images.ps1 maintenance command to add the columns once.

Optional overrides: --left <file>, --right <file>, --forward <file>, --back <file>.
Or use --room-textures <folder> with left.png, right.png, forward.png, back.png,
floor.png, ceiling.png. Any subset is allowed. Individual switches override folder
images, which override database values for the supplied surfaces. --forward means
the fixed FRONT wall. Quote paths with spaces. PNG, JPEG, and WebP are supported.
Each image stretches across its wall behind the existing grid. For undistorted
images use 12:7 proportions for forward/back, 18:7 for left/right, 12:18 for floor/ceiling.
Missing folder images keep the database choice or default. Invalid selected images
show a warning and keep the affected surfaces at their defaults.
'@ | Set-Content -LiteralPath 'artifacts/windows/README.md'
    Write-Host 'Export ready: artifacts/windows/PalaceRoomViewer.exe'
} finally { Pop-Location }
