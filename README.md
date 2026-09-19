# Palace Room Viewer

A walkable Godot C# memory-palace Room, loaded directly from SQLite. Each Locus
stays at its numbered Position. The viewer never edits the database.

## Run the Windows application

Keep the entire `artifacts/windows` folder together. From PowerShell:

```powershell
& .\artifacts\windows\PalaceRoomViewer.exe -- --room 8
& .\artifacts\windows\PalaceRoomViewer.exe -- --room 7 --db 'C:\tools\Data\palace.db'
```

`--room` is required. `--db` defaults to `C:\tools\Data\palace.db` and accepts an
absolute or current-working-directory-relative path. The `--` separator passes
arguments through Godot to the application. Double-clicking without arguments
shows instructions. Room 8 is a complete example in the reviewed database; Room 7
demonstrates warnings for Positions 27–29.

| Control | Action |
| --- | --- |
| W A S D | Walk relative to your facing direction |
| Mouse | Look, including up at the ceiling and down at the floor |
| J | Toggle Locus text (initially hidden) |
| K | Toggle numbered markers (initially visible) |
| Escape | Release the mouse; use scrollbars or select text |
| Click the Room | Capture the mouse and resume walking |
| Alt+F4 | Close the viewer |

The teal wall is **FRONT**, the bottom of the original diagrams. The room plan
keeps this orientation while you turn. Aim near a populated Position with text
enabled to show its full text in the reading panel; release the mouse to scroll
long text. Warnings stay visible and scroll when needed.

## Develop and export

Install .NET SDK **10.0.303**. The bootstrap script downloads and verifies the
pinned Godot **4.7.2 .NET** editor and matching templates into `.tools/`.

```powershell
.\scripts\bootstrap.ps1
.\scripts\run.ps1 -Room 8
.\scripts\run.ps1 -Room 8 -Database 'D:\my data\palace.db'
.\scripts\export.ps1
.\scripts\package.ps1
```

Open `project.godot` with the downloaded .NET editor to work visually. F6/F5 uses
Room 8 via the configured main run arguments. The standard Godot edition cannot
run C#. The export is self-contained; users need neither Godot nor a .NET SDK.
Distribute the whole export directory, including the native SQLite dependency.

## Verification

```powershell
dotnet run --project Tests/PalaceRoomViewer.Tests.csproj
.\scripts\verify.ps1
.\scripts\verify.ps1 -Export
.\scripts\verify.ps1 -Export -Visual
```

Use PowerShell 7 for `verify.ps1`. The test runner creates isolated SQLite fixtures. Runtime verification exercises
the actual Godot scene, toggles, wall collision, eye height, mapping, and loading.
The optional visual run also captures front/corner/floor/ceiling PNGs. Results live
in `artifacts/verification/`. Export verification uses a separate working directory
and a restricted PATH, and checks that native SQLite loads from the export folder.

Missing arguments, unknown Rooms, unusable files, and incompatible schemas show
visible errors. Null, non-integer, out-of-range, and duplicate Positions produce
warnings identifying the skipped Loci. The lowest Locus Id wins a duplicate.
Gaps stay empty; an existing empty Room still has all 26 markers.

Implementation choices and pinned versions are in [docs/IMPLEMENTATION.md](docs/IMPLEMENTATION.md).
The original [plan](room-loci.md), [glossary](CONTEXT.md), and
[database ADR](docs/adr/0001-csharp-godot-reads-palace-db-directly.md) define the scope.
Room images, Pegs, editing, learning-session recording, and Room navigation are deferred.

The completed acceptance run and visual notes are recorded in [docs/VALIDATION.md](docs/VALIDATION.md).
