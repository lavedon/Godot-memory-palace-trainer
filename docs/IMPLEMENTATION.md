# Implementation decisions

## Reproducible Windows toolchain

- Godot .NET editor: **4.7.2-stable**, Windows x64.
- Export templates: **4.7.2-stable mono**, Windows x64 release/debug.
- .NET SDK: **10.0.303**, pinned in `global.json`.
- Application target: **net8.0**, self-contained Windows x64 export.
- `Godot.NET.Sdk`: **4.7.2**.
- `Microsoft.Data.Sqlite`: **8.0.31**, including its NuGet-managed native runtime.

`scripts/bootstrap.ps1` downloads the editor and matching templates from the official
Godot release and checks their SHA-512 hashes. Local tools live under `.tools/`.
No dependency is taken on `C:\tools\e_sqlite3.dll`.

## Coordinate convention (recorded before layout implementation)

The diagrams are viewed from above, with their bottom edge the **front** and top
edge the **back**. World +X is diagram-right, +Y is up, +Z is diagram-down/front.
The room's clear interior is 12 m wide (X = -6..6), 18 m deep (Z = -9..9), and
7 m high (Y = 0..7). The front wall has a teal inset and an explicit FRONT cue.

The eight wall-slots in order are front-center, front-right, right-center,
back-right, back-center, back-left, left-center, front-left. At each wall-slot,
Slices 1/2/3 have logical heights 0/3.5/7 m. Position = (wall-slot - 1) * 3 + Slice.
Positions 25/26 are exactly (0,0,0)/(0,7,0). These Anchors never follow the camera.

The player starts at (0,0,-4), with an eye height of 1.7 m, facing the front
(+Z, yaw PI). Walking speed is 3.5 m/s; mouse sensitivity is 0.002 rad/pixel;
pitch is clamped to +/-85 degrees. Collision radius is 0.3 m. No jump or flight.

Billboard presentation is separate from logical Anchors. Text moves inward from
walls and away from floor/ceiling, with bounded distance scaling and word wrapping.
Numbers have their own visibility toggle. Full text is also available in a scrollable
reading panel when aimed at a populated Position with text enabled.

Wall text is inset 1.75 m horizontally; low/high Slices display at 0.85/6.15 m.
Floor and ceiling text display at 0.75/6.15 m at their horizontal centers. A
48-pixel font at 0.0055 m/pixel uses adaptive wrapping width and a bounded physical
text area (3.3 m wide, 1.35 m high). TextParagraph measures actual wrapped height;
Label3D's conservative billboard AABB is unsuitable for that measurement. Distance
scaling is bounded rather than fixed screen size, preserving spatial depth cues.
Numbers use screen-plane offsets so they remain above floor/ceiling text too.

**L** independently toggles the Locus under the center crosshair. Targeting tests
the camera-facing text and marker areas, including when hidden, and selects the
nearest intersected Position. Empty Positions do nothing. Per-Locus visibility
persists while moving and looking away. **J** hides all if any text is visible,
otherwise shows all; **K** remains independent. The reading panel follows the
target's actual visibility, including text revealed with L.

## Room surface textures

The repository reads whichever of the six `*ImagePath` columns exist in `Rooms`,
in the same read-only transaction as the title and Loci. Missing columns support
legacy databases; NULL or blank values select default surfaces. Image paths from
the database resolve against its directory. CLI overrides retain their original
Godot-working-directory-relative behavior. Godot sets this to the executable
directory in the Windows export, so absolute CLI paths are recommended; database
image paths always use the database directory. The complete schema and migration contract
are in [ADR 0002](adr/0002-room-surface-image-paths.md).

`--left`, `--right`, `--forward`, and `--back` accept image paths. `--room-textures`
accepts a folder with the corresponding lowercase PNG filenames, plus optional
`floor.png` and `ceiling.png`. Forward maps to
the fixed +Z FRONT wall. `Core/WallTextureOptions.cs` resolves paths and applies
explicit overrides independently of argument order. Missing folder filenames are
optional; invalid supplied paths and decode failures appear in the warning panel.
An invalid explicit override leaves the default wall rather than using the folder.

`RoomGeometry.ApplyTextures` loads external files at runtime with `Image.Load` and
`ImageTexture.CreateFromImage`; no Godot editor import is needed. Each successfully
loaded image gets an opaque, rough `StandardMaterial3D` on an inward-facing
`QuadMesh`. Full 0..1 UVs stretch the whole image once across the wall. Mipmaps and
linear filtering reduce shimmer while walking. Front/back are 12 by 7 m and the
side walls are 18 by 7 m. Floor and ceiling use 12 by 18 m inward-facing quads.
The floor image sits below the existing joints; the ceiling image sits above the
ceiling light strips. Collision geometry is unchanged.
The top edge of both horizontal images points toward the fixed BACK wall.

Image surfaces sit just inside the solid walls, behind the existing grid meshes.
Grid geometry and collision stay in place. A successful forward image hides the
teal inset so it cannot cover the image, and wall cues gain a dark outline for
readability. Walls without a usable image retain their original appearance.

## Code organization

- `Core/`: engine-independent argument parsing, the read-only repository, snapshots,
  validation warnings, and fixed world coordinates.
- `scripts/RoomViewer.cs`: load once, create displays, route controls, and choose
  which Locus to show in the reading panel.
- `scripts/RoomGeometry.cs`, `WalkingCamera.cs`, `LocusDisplay.cs`, `ViewerHud.cs`:
  procedural Room surfaces/collision, walking, text presentation, and interface.
- `Tests/`: isolated SQLite acceptance fixtures and data/layout checks.
- `Migrations/`: separate maintenance executable and reviewable SQL for adding
  the six image columns, with backup and transaction handling.
- `scripts/RuntimeVerification.cs`: opt-in scene checks and screenshots, activated
  only by `PALACE_VIEWER_SMOKE_DIR` from `verify.ps1`. Desktop input is ignored in
  this mode; tagged synthetic input exercises the engine's real event pipeline.

Normal operation never writes to the database. Queries run inside one deferred
read-only transaction and all connections/readers are disposed before rendering.

## Sources

- [Godot Label3D](https://docs.godotengine.org/en/stable/classes/class_label3d.html)
- [Godot Image](https://docs.godotengine.org/en/stable/classes/class_image.html)
- [Godot ImageTexture](https://docs.godotengine.org/en/stable/classes/class_imagetexture.html)
- [Godot C# prerequisites](https://docs.godotengine.org/en/stable/tutorials/scripting/c_sharp/c_sharp_basics.html#prerequisites)
- [Godot command-line arguments](https://docs.godotengine.org/en/stable/tutorials/editor/command_line_tutorial.html)
- [SQLite connection modes](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/connection-strings)
- [SQLite native dependencies](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/custom-versions)

The original requirements and ADR remain the acceptance contract; their dated
review addenda take precedence over the original wording.
