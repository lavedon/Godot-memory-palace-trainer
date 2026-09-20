# Validation — 2026-09-19

Built and exercised on Windows x64 with the pinned toolchain in
[IMPLEMENTATION.md](IMPLEMENTATION.md). The renderer used the NVIDIA RTX 3080 Ti
Laptop GPU through Godot's OpenGL compatibility renderer.

## Automated acceptance

- **37/37 core checks passed**: CLI validation, full/partial/empty Rooms, stable gaps,
  overflow, lowest-Id duplicates, null/fractional/text/negative/zero/huge Positions,
  long Unicode text, invalid titles/text, absent/corrupt/incompatible databases,
  exclusive locks, read-only files, connection disposal, unchanged bytes, and the
  independent diagram-to-coordinate checks.
- **15/15 editor runtime cases passed** with isolated fixtures and real Rooms 7/8.
- **15/15 Windows export cases passed**, totaling 453 assertions/capture checks.
  These include actual Godot key-event dispatch for J/K, key-repeat handling,
  four-wall collision, heading-relative movement, fixed eye height, pitch limits,
  stable Anchors, mouse capture/release, and visible errors/warnings.
- The export ran from a separate fixture working directory with PATH restricted
  to Windows system directories. Its process module report confirms both
  `coreclr.dll` and `e_sqlite3.dll` loaded from
  `artifacts/windows/data_PalaceRoomViewer_windows_x86_64/`.
- The fixture database and shared `palace.db` were unchanged. Shared database
  SHA-256 before and after:
  `3a49b5eeecb0e8831c9baa49258234c1672346c0d4c162db0030e53c17e7d9a1`.

Reproduce with PowerShell 7:

```powershell
.\scripts\export.ps1
.\scripts\verify.ps1 -Export -Visual
```

The recorded export run is
`artifacts/verification/20260919-144401-d3c884/summary.json`. Each case contains
`result.json`, stdout/stderr logs, and visual captures. A failed load is a passing
acceptance case only when it displays the expected useful error.

## Visual inspection

Captured all 26 populated Positions in Room 8 with all text enabled, plus front,
corner, floor, and ceiling views. Inspected representative wall/corner Slices and
floor/ceiling text, Room 7's visible warnings for Loci 87–89 at Positions 27–29,
empty markers, and missing-argument instructions in the exported application.

The longest current Locus is Room 2 / Locus 20 / Position 10 (117 characters).
Its full text wraps legibly at a suitable walking viewpoint; the capture is in
`artifacts/verification/longest-current-room2/position-10.png`. A separate synthetic
Room holds more than 1,200 characters per Locus, including Japanese and accented
text, at wall/floor/ceiling Positions. Full content is retained in Billboards and
the scrollable reading panel.

Readability depends on viewing distance. Longer text uses a larger wrapping width
and a bounded physical area; very long text can become small, especially on the
ceiling. The reading panel supplies full text at a consistent screen font size.
This is a local Windows demo; other GPUs and operating systems were not tested.

## L key enhancement — 2026-09-19

The enhanced Windows export passed all **15 runtime cases** in
`artifacts/verification/20260919-152805-ba7c09/summary.json`, with no unexpected
engine errors. The 37 core checks also passed, and database hashes stayed unchanged.

The runtime checks exercise L through Godot's key-event pipeline for every populated
Position, including the floor and ceiling. They verify revealing hidden text,
hiding only the selected Locus, targeting its numbered marker, ignoring key repeat,
preserving visibility while looking away, revealing multiple Loci independently,
leaving markers unchanged, ignoring empty Positions and blank space, and combining
individual visibility with J's all-text toggle. The reading panel reflects the
selected Locus's visibility. The centered crosshair and the target hint were also
visually checked in the exported application.

## Optional wall images — 2026-09-19

- **61/61 core checks passed**, including the five optional image arguments, path
  validation, folder filenames, partial folders, relative paths, and override
  precedence in either argument order.
- **23/23 Windows export runtime cases passed**, totaling **2,640** assertions and
  capture checks, in `artifacts/verification/20260919-160618-9074a8/summary.json`.
  Eight new cases cover all four folder images, individual PNG/JPEG images, a
  partial folder, an explicit override, a missing override, a missing folder with
  a valid explicit image, a corrupt image with another valid image, and an empty
  folder. Existing placement, L/J/K, walking, collision, and load-error checks pass.
- Inspected the four wall captures from the editor run in
  `artifacts/verification/wall-textures-editor/`, and exported captures of the
  forward wall and image warning cases. Calibration lettering is upright and
  unmirrored, the grid remains over the images, the FRONT cue is readable, and
  unavailable images retain the default wall appearance.
- The corrupt-image case logs the expected Godot PNG decode errors and presents
  a warning without blocking the Room. No unexpected engine errors were found.
- The development launcher also passed with a folder and explicit override whose
  paths contain spaces; its report is in
  `artifacts/verification/wall-textures-launcher/result.json`.
- Exported runtime dependencies loaded from the export folder, and fixture/shared
  database hashes remained unchanged. The Windows export and ZIP were refreshed.

## Database surface images — 2026-09-19

- **79/79 core and migration checks passed**. New checks cover preview without
  writes, six nullable TEXT additions, preservation of existing fields/Loci/indexes,
  backup contents, repeat runs, nonexistent targets, incompatible/partial schemas,
  and backups that include uncheckpointed WAL records. Loading covers six surfaces,
  absolute and database-relative paths, blank/NULL/non-text values, missing files,
  CLI precedence, legacy schemas, and unchanged database bytes during viewing.
- **30/30 Windows export runtime cases passed in headless mode**, with **2,946**
  assertions. Results: `artifacts/verification/20260919-165134-81d142/summary.json`.
  Seven additional cases exercise automatic six-surface images, partial/empty
  image columns, invalid images/types, and individual/folder overrides. All
  previous database, placement, L/J/K, and collision scenarios still pass.
- A separate rendered Windows export run passed **198 checks** and loaded all
  six external images with only `--room` and `--db`. Captures and logs are in
  `artifacts/verification/database-images-export-final/`. Floor joints and ceiling
  strips remain visible; the ceiling calibration text is upright from the test
  viewpoint. Both horizontal images point their top edge toward BACK.
- A launch check confirmed that Godot sets the export's working directory to the
  executable directory. CLI documentation now recommends absolute paths when
  launching elsewhere. Relative image paths stored in the database use the
  database directory and work independently of this engine behavior.
- Only expected PNG decoder errors occurred for intentionally corrupt fixtures.
  The normal six-image visual run has no engine errors.
- Rehearsed the migration on a SQLite backup of the shared database. Compared
  every original field of every row across all six tables: 20 Rooms, 369 Loci,
  383 LocusPegs, 3 Palaces, 326 Pegs, and 2 RoomLearningSessions were unchanged.
  Indexes, triggers, views, and foreign-key checks match the backup; integrity
  passes; exactly six new columns are NULL. Report:
  `artifacts/verification/database-migration-rehearsal/result.json`.
- The live migration was subsequently applied to `C:\Tools\Data\palace.db` on
  2026-09-19 when the user requested image-path assignment. All six columns were
  added and SQLite integrity passed. Every original field of every record matches
  the pre-migration backup at
  `C:\Tools\Data\palace.db.before-room-images-20260919-222520-b1be87d840d545029a1424d3d018c96e.bak`.
