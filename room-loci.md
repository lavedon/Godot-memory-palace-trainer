The goal of this project is to create a simple Godot demo project that we will use with our other memory palace flows.

The goal is to take a room from our sqlite3 database:

C:\tools\Data\palace.db 

```sql 
CREATE TABLE Palaces (
    Id          INTEGER PRIMARY KEY,                 -- INTEGER (not INT) = auto-assigned rowid
    Name        TEXT    NOT NULL UNIQUE,
    Description TEXT,
    PreviousId  INTEGER REFERENCES Palaces(Id) ON DELETE SET NULL,
    NextId      INTEGER REFERENCES Palaces(Id) ON DELETE SET NULL,
    CreatedDate TEXT    NOT NULL DEFAULT CURRENT_TIMESTAMP,   -- UTC, auto-filled
    CHECK (PreviousId IS NULL OR PreviousId <> Id),           -- no self-loop
    CHECK (NextId     IS NULL OR NextId     <> Id)
);
CREATE TABLE Rooms (
    Id          INTEGER PRIMARY KEY,
    PalaceId    INTEGER NOT NULL REFERENCES Palaces(Id) ON DELETE CASCADE,
    Title       TEXT    NOT NULL,
    Description TEXT,
    SourceFile  TEXT,                                 -- e.g. the Obsidian .md
filename
    VideoUrl    TEXT,                                 -- room walkthrough (you
keep YouTube shorts of these)
    PreviousId  INTEGER REFERENCES Rooms(Id) ON DELETE SET NULL,
    NextId      INTEGER REFERENCES Rooms(Id) ON DELETE SET NULL,
    CreatedDate TEXT    NOT NULL DEFAULT CURRENT_TIMESTAMP, RoomImage TEXT,
    CHECK (PreviousId IS NULL OR PreviousId <> Id),
    CHECK (NextId     IS NULL OR NextId     <> Id)
);
CREATE TABLE Loci (
    Id          INTEGER PRIMARY KEY,
    RoomId      INTEGER NOT NULL REFERENCES Rooms(Id) ON DELETE CASCADE,
    Position    INTEGER,                              -- 1..N within the room (nullable; unique per room when set)
    Text        TEXT    NOT NULL,                     -- the fact(s) encoded here
    ImageStory  TEXT,                                 -- the mnemonic scene narration
    SourceRef   TEXT,                                 -- pointer to the source
(kata/doc/anki)
    PreviousId  INTEGER REFERENCES Loci(Id) ON DELETE SET NULL,
    NextId      INTEGER REFERENCES Loci(Id) ON DELETE SET NULL,
    CreatedDate TEXT    NOT NULL DEFAULT CURRENT_TIMESTAMP, LocationImage TEXT, ArtImage TEXT,
    CHECK (PreviousId IS NULL OR PreviousId <> Id),
    CHECK (NextId     IS NULL OR NextId     <> Id)
);
CREATE TABLE Pegs (
    Id              INTEGER PRIMARY KEY,
    Term            TEXT    UNIQUE,                    -- the concept it encodes, e.g. "CORS"
    Description     TEXT    NOT NULL,                  -- the image, e.g. "Coors beer"
    Category        TEXT,                              -- person | object | place | action | other
    FilePathToImage TEXT,                              -- OPTIONAL; no BLOB
    CreatedDate     TEXT    NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE INDEX ix_rooms_palace   ON Rooms(PalaceId);
CREATE INDEX ix_loci_room      ON Loci(RoomId);
CREATE UNIQUE INDEX ux_loci_position  ON Loci(RoomId, Position) WHERE Position
IS NOT NULL;
CREATE TRIGGER trg_loci_next_upd
AFTER UPDATE OF NextId ON Loci
FOR EACH ROW WHEN NEW.NextId IS NOT OLD.NextId
BEGIN
    UPDATE Loci SET PreviousId = NULL
      WHERE OLD.NextId IS NOT NULL AND Id = OLD.NextId AND PreviousId IS NEW.Id;
    UPDATE Loci SET PreviousId = NEW.Id
      WHERE NEW.NextId IS NOT NULL AND Id = NEW.NextId AND PreviousId IS NOT NEW.Id;
END;
CREATE TRIGGER trg_loci_prev_upd
AFTER UPDATE OF PreviousId ON Loci
FOR EACH ROW WHEN NEW.PreviousId IS NOT OLD.PreviousId
BEGIN
    UPDATE Loci SET NextId = NULL
      WHERE OLD.PreviousId IS NOT NULL AND Id = OLD.PreviousId AND NextId IS NEW.Id;
    UPDATE Loci SET NextId = NEW.Id
      WHERE NEW.PreviousId IS NOT NULL AND Id = NEW.PreviousId AND NextId IS NOT NEW.Id;
END;
CREATE TRIGGER trg_loci_ins
AFTER INSERT ON Loci
FOR EACH ROW
BEGIN
    UPDATE Loci SET PreviousId = NEW.Id
      WHERE NEW.NextId IS NOT NULL AND Id = NEW.NextId AND PreviousId IS NOT NEW.Id;
    UPDATE Loci SET NextId = NEW.Id
      WHERE NEW.PreviousId IS NOT NULL AND Id = NEW.PreviousId AND NextId IS NOT NEW.Id;
END;
CREATE TRIGGER trg_rooms_next_upd
AFTER UPDATE OF NextId ON Rooms
FOR EACH ROW WHEN NEW.NextId IS NOT OLD.NextId
BEGIN
    UPDATE Rooms SET PreviousId = NULL
      WHERE OLD.NextId IS NOT NULL AND Id = OLD.NextId AND PreviousId IS NEW.Id;
    UPDATE Rooms SET PreviousId = NEW.Id
      WHERE NEW.NextId IS NOT NULL AND Id = NEW.NextId AND PreviousId IS NOT NEW.Id;
END;
CREATE TRIGGER trg_rooms_prev_upd
AFTER UPDATE OF PreviousId ON Rooms
FOR EACH ROW WHEN NEW.PreviousId IS NOT OLD.PreviousId
BEGIN
    UPDATE Rooms SET NextId = NULL
      WHERE OLD.PreviousId IS NOT NULL AND Id = OLD.PreviousId AND NextId IS NEW.Id;
    UPDATE Rooms SET NextId = NEW.Id
      WHERE NEW.PreviousId IS NOT NULL AND Id = NEW.PreviousId AND NextId IS NOT NEW.Id;
END;
CREATE TRIGGER trg_rooms_ins
AFTER INSERT ON Rooms
FOR EACH ROW
BEGIN
    UPDATE Rooms SET PreviousId = NEW.Id
      WHERE NEW.NextId IS NOT NULL AND Id = NEW.NextId AND PreviousId IS NOT NEW.Id;
    UPDATE Rooms SET NextId = NEW.Id
      WHERE NEW.PreviousId IS NOT NULL AND Id = NEW.PreviousId AND NextId IS NOT NEW.Id;
END;
CREATE VIEW v_loci_link_breaks AS
SELECT a.Id AS LocusId, a.Text, a.NextId AS PointsToNext, b.PreviousId AS SuccessorsPrev
FROM Loci a
LEFT JOIN Loci b ON b.Id = a.NextId
WHERE a.NextId IS NOT NULL
  AND (b.Id IS NULL OR b.PreviousId IS NOT a.Id)
/* v_loci_link_breaks(LocusId,Text,PointsToNext,SuccessorsPrev) */;
CREATE VIEW v_room_link_breaks AS
SELECT a.Id AS RoomId, a.Title, a.NextId AS PointsToNext, b.PreviousId AS SuccessorsPrev
FROM Rooms a
LEFT JOIN Rooms b ON b.Id = a.NextId
WHERE a.NextId IS NOT NULL
  AND (b.Id IS NULL OR b.PreviousId IS NOT a.Id)
/* v_room_link_breaks(RoomId,Title,PointsToNext,SuccessorsPrev) */;
CREATE VIEW v_journey_context AS
SELECT pa.Name AS Palace, r.Title AS Room, l.Position, l.Id AS LocusId,
       l.Text, l.PreviousId, l.NextId
FROM Loci l
JOIN Rooms   r  ON r.Id  = l.RoomId
JOIN Palaces pa ON pa.Id = r.PalaceId
/* v_journey_context(Palace,Room,Position,LocusId,Text,PreviousId,NextId) */;
CREATE TABLE "LocusPegs" (
    LocusId INTEGER NOT NULL REFERENCES Loci(Id) ON DELETE CASCADE,
    PegId   INTEGER NOT NULL REFERENCES Pegs(Id) ON DELETE CASCADE,
    Slot    INTEGER,
    PRIMARY KEY (LocusId, PegId),
    CHECK (Slot IS NULL OR Slot BETWEEN 1 AND 5)
);
CREATE INDEX ix_locuspegs_peg ON LocusPegs(PegId);
CREATE VIEW v_locus_peg_count AS
SELECT l.Id AS LocusId, l.RoomId, l.Text, COUNT(lp.PegId) AS PegCount
FROM Loci l LEFT JOIN LocusPegs lp ON lp.LocusId = l.Id
GROUP BY l.Id
/* v_locus_peg_count(LocusId,RoomId,Text,PegCount) */;
CREATE VIEW v_loci_overpacked AS SELECT * FROM v_locus_peg_count WHERE PegCount > 5
/* v_loci_overpacked(LocusId,RoomId,Text,PegCount) */;
CREATE VIEW v_peg_usage AS
SELECT p.Id AS PegId, p.Term, p.Description, COUNT(lp.LocusId) AS UsedInLoci
FROM Pegs p LEFT JOIN LocusPegs lp ON lp.PegId = p.Id
GROUP BY p.Id
/* v_peg_usage(PegId,Term,Description,UsedInLoci) */;
CREATE VIEW v_locus_with_pegs AS
SELECT l.Id AS LocusId, l.Text, GROUP_CONCAT(p.Description, ' | ') AS Pegs
FROM Loci l LEFT JOIN LocusPegs lp ON lp.LocusId = l.Id
LEFT JOIN Pegs p ON p.Id = lp.PegId
GROUP BY l.Id
/* v_locus_with_pegs(LocusId,Text,Pegs) */;
CREATE UNIQUE INDEX ux_pegs_term_nocase ON Pegs(Term COLLATE NOCASE);
CREATE TABLE RoomLearningSessions (
    Id     INTEGER PRIMARY KEY,
    RoomId INTEGER NOT NULL REFERENCES Rooms(Id) ON DELETE CASCADE,
    Start  TEXT,          -- DD-MM-YYYYTHH:MM:SS
    "End"  TEXT           -- DD-MM-YYYYTHH:MM:SS (End is a SQLite keyword, must be quoted)
);
```
When we copy a room over the rooms table. We will import it into a Godot 3d demo with the following properties. 

The demo is like a FPS game, but the camera is inside a simple cube.

The camera moves forward, left, right, and back using the 
W A S D 
keys. 

The 'aim' or look of the camera is with the mouse. 

On press of the 'j' key the following things happen. 

A texture with short printed information appears at 26 points in the single 'cube' room the camera is in. The 'j' key is a toggle - it turns these textures of text on and off. Think of them like billboards.

The text and what position it should appear at comes from here:
```sql 
select Position, Text from loci where RoomId = @ourRoomId;
```
Each 'Position' integer is the position where what is in 'Text' is displayed. 

The 26 positions are as follows:

Think of the main positions as the cube cut into 3 vertical slices.

Slice 1: 

Where floor and wall meet (i.e. corners):
```
  16--13--10 
  |        |
  |        |
  19       7
  |        |
  |        |
  22- 1 -- 4
```

Slice 2: Center 
The center of walls and the center of corners. 

```
  17--14--11 
  |        |
  |        |
  20       8
  |        |
  |        |
  23- 2 -- 5
```

Slice 3: Upper 
Where the wall and ceiling meet. The top slice.
```
  18--15--12 
  |        |
  |        |
  21       9
  |        |
  |        |
  24- 3 -- 6
```

25 is the floor.
26 is the ceiling.

## Agreed evaluation and demo requirements — 2026-09-19

The design is feasible and appropriately scoped for a local Windows demo. Keep one
walkable Room, 26 fixed Anchors, WASD movement, mouse look, and a `j` toggle for Locus
text. The main work before implementation is to make data validation, spatial
orientation, and text readability explicit.

This addendum takes precedence over conflicting wording above. The shared terms are
defined in [CONTEXT.md](CONTEXT.md); database access and deployment decisions are in
[ADR 0001](docs/adr/0001-csharp-godot-reads-palace-db-directly.md).

### Evidence from the review

A read-only inspection of `C:\tools\Data\palace.db` on 2026-09-19 found:

- 20 Rooms and 369 Loci.
- Room 7 has 29 Loci at Positions 1–29, exceeding the viewer's capacity.
- Room 8 is one example with exactly Positions 1–26.
- No null Positions, duplicate Positions within a Room, or numbering gaps were found
  in that snapshot. The viewer must still handle these cases explicitly.
- Locus text ranges from 4 to 117 characters. This supports trying the billboard
  approach, but does not establish a permanent limit on future text length.

The three diagrams cover Positions 1–24 exactly once and agree with
`Position = (wall-slot - 1) * 3 + Slice`. These checks validate numbering, not visual
readability or the orientation of a future implementation. The database observations
are a dated snapshot and must be rechecked when selecting demonstration data.

### Loading and validation

- Require `--room <id>` to select a Room. Support an optional `--db <path>` override,
  defaulting to `C:\tools\Data\palace.db`. These are planned application arguments.
- Display the selected Room's title. Distinguish an existing Room with no Loci from
  a Room ID that does not exist: an empty Room shows all 26 empty markers.
- Place each Locus whose Position is an integer from 1 through 26 and unique within the
  Room. Skip any Locus whose Position is above 26, null, or duplicates an already-placed
  Position, and emit a visible warning naming the skipped Loci. Never silently discard,
  clamp, or renumber them; on a duplicate Position, place the lowest Locus Id and warn
  about the others. The rest of the Room still renders.
- Preserve gaps: a Locus at Position 10 stays at Anchor 10 even if Position 9 is empty.
  Map by the Position value, never by a row's ordinal position in the query results.
- Report missing arguments, invalid or unknown Room IDs, missing or unreadable database
  files, incompatible schemas, and database access failures in a visible error message.
- Keep the shared database unchanged. Validation is a viewer responsibility and must
  not repair or migrate the source database.

### Spatial layout and controls

Before writing layout code, record a single coordinate convention: which diagram edge
is the front wall, the direction from which the diagrams are viewed, world axes, room
dimensions, and the camera's starting position and facing direction. Exact dimensions
and the initial camera pose remain to be selected. Depth must exceed width.

Use a stable mapping for all 26 Anchors and an obvious visual cue for the front wall.
Positions 25 and 26 should use the centers of the floor and ceiling respectively.
The camera's movement or facing direction must never change the mapping.

The first demo should use walking at a fixed eye height, with wall collision and no
flight or jumping. WASD moves relative to the camera's horizontal facing direction;
looking up or down must not move the player vertically. Capture the mouse for looking,
release it with Escape, and allow a click in the viewer to capture it again. Define
and tune movement speed, mouse sensitivity, and vertical look limits during the first
visual pass.

### Billboard readability

Readability is the main visual experiment. Use Godot's `Label3D` as the initial
implementation candidate; it provides billboarding and text wrapping without a
separate image-generation pipeline. See the
[Label3D documentation](https://docs.godotengine.org/en/stable/classes/class_label3d.html).

Recommended first-pass behavior:

- Start with Locus text hidden. Each press of `j` toggles all populated Billboards.
- Keep faint numbered markers visible by default for all 26 Positions, including empty
  Positions. The `k` key toggles the markers, independently of the `j` text toggle.
- Keep logical Anchors fixed, but offset the displayed text inward enough to avoid
  intersections with walls, corners, the floor, and the ceiling as it faces the camera.
- Choose wrapping width, font size, contrast, and distance scaling together. Preserve
  the full Locus text; do not silently truncate it to fit a Billboard.
- Verify floor and ceiling text while looking up and down, and verify corner text
  from several walking positions. Check overlap and clipping with all 26 texts enabled.

These visual settings need a running prototype. The current text-length measurements
are useful sample data, not proof that the final presentation will be readable.

### Scope and document corrections

- "Copy a room over" and "import" are historical wording: read the shared database
  directly when loading a Room. There is no export or copied runtime data source.
- Use "rectangular room" in place of "cube" and "horizontal bands stacked vertically"
  in place of "vertical slices," following the glossary.
- Position 22 is already correct in the current Slice-1 diagram.
- The SQL schema above is a reference transcription, not an executable setup script.
  Wrapped comments leave bare text such as `filename`, `keep YouTube shorts of these)`,
  and `(kata/doc/anki)` on SQL lines. Executing the pasted schema fails. Restore those
  fragments to comments before reusing it in an executable fixture; the viewer itself
  must not run schema creation against the shared database.
- Defer `RoomImage` display, Peg rendering, database editing, learning-session recording,
  and navigation between Rooms. The first demo reads the Room title and its Loci.

### Build order and acceptance

1. Build a walkable rectangular room with 26 numbered markers. Check the diagrams,
   orientation, floor and ceiling centers, collision, and mouse capture/release.
2. Add readable Billboards and the `j` toggle. Check all 26 Positions, the longest
   current text, and a longer wrapping sample before connecting the database.
3. Add validated, read-only database loading and produce a Windows export. Verify the
   exported application as well as running from the editor.

The demo is ready when:

- A valid full Room places every Locus at its numbered Anchor and displays its title.
- A partially populated Room and a fixture with gaps preserve all Position identities;
  an empty Room shows all 26 markers.
- Room 7 renders Positions 1–26 and logs a visible warning listing the skipped
  Positions 27–29. Fixtures also cover null and duplicate Positions, which are skipped
  with a warning rather than silently dropped, while the rest of the Room still renders.
- Missing or invalid inputs and database failures produce useful errors without
  creating a database or changing existing records.
- Walking stays inside the Room, looking does not change eye height, and mouse
  capture/release works. All 26 Billboards are readable from suitable viewing positions,
  including the floor, ceiling, and corners, and repeated `j` presses toggle only text.
- The Windows export loads SQLite through its packaged dependencies, accepts a database
  path override, and runs without relying on the editor or `C:\tools\e_sqlite3.dll`.

Use isolated fixtures for invalid or synthetic data; do not change the shared database
to exercise acceptance cases.
