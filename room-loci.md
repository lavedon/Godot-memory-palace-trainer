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
