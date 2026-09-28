# Palace Room Viewer

A walkable Godot C# memory-palace Room, loaded directly from SQLite. Each Locus
stays at its numbered Position. The viewer never edits the database.

## Run the Windows application

Keep the entire `artifacts/windows` folder together. From PowerShell:

```powershell
& .\artifacts\windows\PalaceRoomViewer.exe -- --room 8
& .\artifacts\windows\PalaceRoomViewer.exe -- --room 7 --db 'C:\tools\Data\palace.db'
```

`--room` is optional: without it the viewer opens the **Palace menu**. `--db` defaults to `C:\tools\Data\palace.db` and accepts an
absolute path or a path relative to Godot's working directory (the executable
folder for the Windows export, project root in development). Prefer absolute
`--db` paths when launching from another directory. The `--` separator passes
arguments through Godot to the application. Double-clicking without arguments
opens the Palace menu on the default database. Room 8 is a complete example in the reviewed database; Room 7
demonstrates warnings for Positions 27–29.

| Control | Action |
| --- | --- |
| W A S D | Walk relative to your facing direction |
| Mouse | Look, including up at the ceiling and down at the floor |
| J or right-click | Hide all text if any is visible; otherwise show all text |
| L or left-click | Toggle only the Locus under the crosshair, even when its text is hidden |
| K | Toggle numbered markers (initially visible) |
| Escape | Release the mouse; use scrollbars or select text |
| Left-click the Room | Capture the mouse and resume walking; also toggle the Locus under the crosshair |
| M | Open or close the Palace menu |
| Alt+F4 | Close the viewer |

## Palace menu

Press **M** (or start without `--room`) to choose what to load. The left list shows
every Palace with how many of its Rooms have background images (e.g. `1/12`). The
right list shows the selected Palace's Rooms with their Loci count and image status:

- **No images** (grey): none of the six image columns is set.
- **n/6 images** (teal): n surface images are set and exist on disk.
- **· m missing** (amber): m paths are set but the file cannot be found.

Selecting a Room lists each surface (LEFT, RIGHT, FRONT, BACK, FLOOR, CEILING) as
found, MISSING, or none, with the resolved file path. Double-click, press Enter, or
choose **Load Room** to display it; the menu reloads the database each time it opens,
so newly added Rooms and images appear without restarting. `--db` carries over to
Rooms chosen in the menu; image override switches apply only to the starting Room.
Databases without a `Palaces` table list all Rooms under one group.

The wall marked **FRONT** (teal by default) is the bottom of the original diagrams. The room plan
keeps this orientation while you turn. Aim near a populated Position with text
enabled to show its full text in the reading panel; release the mouse to scroll
long text. Warnings stay visible and scroll when needed.

Text starts hidden. Aim at a Locus's text area or numbered marker and press **L** or **left-click**
to reveal or hide only that Locus. Its visibility persists when you look away.
The crosshair and the top-right hint identify the target. Empty Positions or looking
between Positions do nothing. **J** or **right-click** sets all text together; **K** only affects markers.

## Room images from the database

The viewer automatically loads images from six optional `Rooms` columns:

| Column | Surface | Image aspect ratio |
| --- | --- | --- |
| `LeftImagePath` | LEFT wall | 18:7 |
| `RightImagePath` | RIGHT wall | 18:7 |
| `ForwardImagePath` | FRONT wall | 12:7 |
| `BackImagePath` | BACK wall | 12:7 |
| `FloorImagePath` | Floor | 12:18 |
| `CeilingImagePath` | Ceiling | 12:18 |

These nullable TEXT values hold local image file paths. Absolute paths work;
relative paths resolve from the **database's directory**, regardless of where you
launch the viewer. NULL or blank values keep the default surface. Images stretch
across each surface with the existing grid, floor joints, and ceiling lights retained.
Missing or damaged images show warnings while the Room remains usable.

Apply the schema migration once using the separate maintenance command:

```powershell
.\scripts\migrate-room-images.ps1 -Database 'C:\tools\Data\palace.db' -Check
.\scripts\migrate-room-images.ps1 -Database 'C:\tools\Data\palace.db'
```

The first command previews missing columns. The second creates a SQLite backup
beside the database, then adds missing columns in one transaction. Repeating it is
safe. Existing Room and Locus data and the older `RoomImage` field are preserved.
The viewer itself stays read-only and also supports databases without these columns.

For example, place six images in `C:\tools\Data\images\room8\`, then set the paths
using your database editor:

```sql
UPDATE Rooms SET
    LeftImagePath = 'images/room8/left.png',
    RightImagePath = 'images/room8/right.png',
    ForwardImagePath = 'images/room8/forward.png',
    BackImagePath = 'images/room8/back.png',
    FloorImagePath = 'images/room8/floor.png',
    CeilingImagePath = 'images/room8/ceiling.png'
WHERE Id = 8;
```

Start the Room with `PalaceRoomViewer.exe -- --room 8`; no image switches are needed.
Images are loaded afresh when the Room starts. The migration leaves the new columns
NULL; it does not assign images to existing rooms.

## Optional image overrides

Use a folder containing any of `left.png`, `right.png`, `forward.png`, `back.png`,
`floor.png`, and `ceiling.png`:

```powershell
& .\artifacts\windows\PalaceRoomViewer.exe -- --room 8 --room-textures 'D:\palace walls'
```

Or supply individual images for any combination of walls:

```powershell
& .\artifacts\windows\PalaceRoomViewer.exe -- --room 8 --left 'D:\pictures\left.png' --right 'D:\pictures\right.jpg' --forward 'D:\pictures\front.png' --back 'D:\pictures\back.png'
```

Individual switches override matching folder images, and folder images override
database paths for the surfaces supplied. Unspecified surfaces keep their database
images or defaults. Argument order does not affect precedence. CLI paths can be
absolute or relative to Godot's working directory; quote paths containing spaces. `--forward` always means
the fixed **FRONT** wall, matching the room plan, rather than your current heading.

Images load at startup from external PNG, JPEG, or WebP files. Each image stretches
once across its whole wall, with the existing grid on top. For undistorted images,
use a **12:7** aspect ratio for forward/back and **18:7** for left/right. A forward
image replaces the teal inset; the FRONT cue remains visible.

All images are optional. Missing filenames in an override folder keep the database
choice or default. A missing folder, missing explicit file, or unreadable image
produces a visible warning while the Room remains usable. An invalid selected
override leaves that surface at its default appearance.

## Develop and export

Install .NET SDK **10.0.303**. The bootstrap script downloads and verifies the
pinned Godot **4.7.2 .NET** editor and matching templates into `.tools/`.

```powershell
.\scripts\bootstrap.ps1
.\scripts\run.ps1            # opens the Palace menu
.\scripts\run.ps1 -Room 8
.\scripts\run.ps1 -Room 8 -Database 'D:\my data\palace.db'
.\scripts\run.ps1 -Room 8 -RoomTextures 'D:\palace walls' -Left 'D:\pictures\left.png'
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
Database `RoomImage` display, Pegs, editing, learning-session recording, and Previous/Next Room navigation are deferred.

The completed acceptance run and visual notes are recorded in [docs/VALIDATION.md](docs/VALIDATION.md).
