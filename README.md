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
| J or right-click | Hide all text if any is visible; otherwise show all text. During a rehearsal or loop drill, J = knew it |
| L or left-click | Toggle only the Locus under the crosshair, even when its text is hidden |
| K | Toggle numbered markers (initially visible). During a rehearsal or loop drill, K = missed |
| Escape | Release the mouse; use scrollbars or select text |
| Left-click the Room | Capture the mouse and resume walking; also toggle the Locus under the crosshair |
| M | Open or close the Palace menu |
| R | Start or stop a rehearsal (see below) |
| / | Start a loop drill over chosen Positions (see below); T also works |
| Q | Quit a rehearsal or loop drill |
| V | Sound effects on or off |
| F1 | Change key bindings (see below) |
| Alt+F4 | Close the viewer |

These are the default keys. Every key above except Escape and the mouse can be changed.

## Rehearse a Room

Press **R** to walk the Room in Position order with all text hidden. The camera glides to
each populated Position and highlights its marker. Recall the Locus, press **Space** (or
**H**) to reveal it, then grade yourself: **J** = knew it, **K** = missed (**2** and **1** also
work). When the round ends, the next round repeats only that round's misses, still in
Position order, until a round has no misses. Markers turn teal (known) or coral (missed)
as you go. During a rehearsal J and K grade instead of toggling text and markers, and L
and the mouse text toggles are disabled, so nothing is revealed early. Walking or looking
cancels the glide.

When the Room is clear, the result card shows your medal, time, and misses. **Space**
starts again; **Q** (or **R**) ends the rehearsal; **M** opens the Palace menu.

### Beat your time

Every rehearsal is a timed run. The clock starts when you press **R** and stops when
the Room is clear; it pauses while the Palace menu is open.

- **Ghost splits**: after each first-pass answer, the panel shows how far ahead of
  (`−1.3 s`, teal) or behind (`+0.8 s`, coral) your fastest run on that Room you are.
- **Score**: each first-pass recall earns 100 points, +10 per combo step (up to +100),
  and up to +100 for answering within 10 seconds. Recalls in later rounds earn 25.
  A flawless first pass adds 50 per Locus.
- **Combo**: consecutive first-pass recalls. A miss resets it. Chimes rise in pitch as
  the combo grows.
- **Medals**: **Platinum** is flawless at 4 s or less per Locus; **Gold** is flawless at
  6 s or less; **Silver** is at least 80% on the first pass; **Bronze** is any clear.
- **Personal bests**: fastest clear, fastest flawless clear, high score, and best combo
  per Room. A new best time, high score, Gold-or-better medal, or trophy sets off confetti.
- **Daily streak**: consecutive days with at least one rehearsal. It survives until the
  end of the day after your last rehearsal.
- **Trophies**: 19 achievements, from *First Steps* to *Platinum Mind*, *Unstoppable*
  (×20 combo), *Comeback*, *Marathon*, and *Week Walker*. The **Trophies** button in the
  Palace menu lists them.

The Palace menu shows your streak, rehearsal count, and trophies; each Room's best
time and best medal; each Palace's count of Gold-or-better Rooms; and the selected
Room's personal bests.

### Rehearsal history in palace.db

Each completed rehearsal adds one row to the `RehearsalRuns` table in the database it
was loaded from. The viewer creates this table the first time you finish a rehearsal
(`CREATE TABLE IF NOT EXISTS`). This is the viewer's only write: loading Rooms stays
read-only, and no other table is ever changed. Rows hold the Room, start and end
times, duration, medal, score, best combo, and JSON columns for Positions, misses by
round, first-pass splits, first-pass missed Locus IDs, and every Locus ID in Position
order (`LocusIds`). A `RehearsalRuns` table created by an earlier version gains the
nullable `LocusIds` column on its next save; older rows keep `NULL` there. If saving fails, for example
because the database is locked, the result card says so and the run is not recorded.

```sql
SELECT RoomId, MIN(DurationMs) AS BestMs, COUNT(*) AS Runs FROM RehearsalRuns GROUP BY RoomId;
```

## Review next (FSRS)

The viewer forecasts how well you still remember each Locus using
[FSRS](https://github.com/open-spaced-repetition/fsrs-rs), the scheduler Anki uses, and
ranks Rooms by what you have most likely forgotten. It works only from this viewer's own
rehearsal history and never reads or changes Anki.

- **What counts:** completed rehearsals only (loop drills are never saved), only their
  first pass (later rounds re-ask misses the same session), and only a Room's first
  rehearsal each day. Knew it = *Good*, missed = *Again*.
- **Palace menu:** a **Recall** column for each Room (`ok · 96%`, `3 weak · 81%`, or
  `new`), a **Due** count per Palace, a **RECALL** line in the Room details listing its
  weak spots, and "N Rooms due" in the stats line.
- **Review next** button: due Rooms across all Palaces, most likely-forgotten Loci
  first (the sum of 1 − recall over the Room's Loci). Click a Room to select it.
- **Weak spots:** a Locus is weak when its predicted recall is below 90%, or it was
  added after the Room's last rehearsal. In a Room, press **/** for a loop drill; the
  prompt lists the weak spots and **Tab** fills them in.

FSRS runs with its default FSRS-6 parameters. Rooms never rehearsed are listed as `new`,
not due.

## Loop drill

Press **/** (or **T**) and type the Positions to drill, such as `1-3` or `1-3, 7, 10-12`, then
**Enter** (**Esc** cancels). The camera glides to each populated Position in the range,
in order, with the text hidden: **Space** or **H** reveals, **J** = knew it, **K** = missed. After
the last Position it goes straight back to the first and keeps looping the same set,
misses and all, until you press **Q**. Press **/** (or **T**) mid-drill to switch to a new range. The panel shows the lap number, this lap's
known/missed counts, last lap's score, and your current streak. Loop drills are practice
only: they are untimed and are not saved to `RehearsalRuns`. The last range you typed is
offered the next time you press **/**.

## Key bindings

Press **F1**, or the **Keys** button in the Palace menu, to see and change every key.
Each action has a main and an alternate key. Click a key, then press the new one;
**Delete** or **Backspace** unbinds it and **Escape** cancels. **Reset to defaults**
restores the original keys.

Keys are grouped by where they work: *Anywhere* (walking, menus, starting a rehearsal or
loop drill, sound), *While exploring* (text and marker toggles), and *In a rehearsal or
loop drill* (reveal, knew it, missed, quit). One key can do one job while exploring and
another in a quiz, which is why J toggles text normally but means "knew it" in a rehearsal.
If you pick a key that already does something in an overlapping group, it is taken
from that action and the menu tells you which one.

Changes take effect immediately and are saved to `keybindings.cfg` in the viewer's user
folder (`%APPDATA%\Godot\app_userdata\Palace Room Viewer\`), not in `palace.db`. Delete
that file to go back to the defaults. Escape always releases the mouse and cancels, and
the mouse buttons cannot be rebound.

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
The viewer never migrates Rooms or Loci and also supports databases without these columns.

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

Install .NET SDK **10.0.303** and [Rust](https://rustup.rs) (cargo 1.85 or newer). The
bootstrap script downloads and verifies the pinned Godot **4.7.2 .NET** editor and
matching templates into `.tools/`. Every .NET build compiles the FSRS scheduler in
`Native/fsrs-ffi` (a small Rust wrapper over the `fsrs` crate, pinned in `Cargo.lock`)
and copies `fsrs_ffi.dll` beside the viewer's assemblies; cargo skips the rebuild when
nothing changed. `cargo test --manifest-path Native/fsrs-ffi/Cargo.toml` runs its tests.

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
