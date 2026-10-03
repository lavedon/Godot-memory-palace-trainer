# Time how long a Room takes to learn

The user wanted to record how long each Room takes to learn. The clock starts the first
time a loop drill (T or /) is run in a Room. It stops at the first rehearsal that walks
every Locus the Room has with no first-pass miss.

## Only the start is stored

Loop drills were never saved (ADR 0004), so nothing recorded when a Room's first drill
happened. The viewer now records exactly that: one row per Room in a new table.

```sql
CREATE TABLE IF NOT EXISTS FirstLoopDrills (
    RoomId    INTEGER PRIMARY KEY REFERENCES Rooms(Id) ON DELETE CASCADE,
    StartedAt TEXT    NOT NULL    -- ISO 8601 with UTC offset
);
```

When a drill starts, the viewer first checks for the Room's row. If one exists, it writes
nothing and takes no write lock. Otherwise, in one `BEGIN IMMEDIATE` transaction, it
creates the table if needed and runs `INSERT OR IGNORE`. The write connection uses
`Mode=ReadWrite`, as in ADR 0003. This is the second additive table the viewer writes.
Rooms, Loci and every other table are still never changed.

The stop time is not stored. It comes from `RehearsalRuns`: the Room's earliest `Perfect`
run whose `Positions` include every Position the Room has now. Personal
bests, streaks and achievements are worked out from history in the same way, and a stored
`LearnedAt` could disagree with the runs.

## Edge cases

- **Learned before the clock started.** The Room's earliest learning run comes before its
  first recorded loop drill. This covers Rooms learned before this version, or learned
  without loop drills. The Room is shown as learned, with no time. The viewer does not use
  a later flawless run instead, because that would time a review, not the learning.
- **No loop drill yet.** The same applies: learned with no time.
- **Fewer than 26 Loci.** Such a Room can be learned: the user chose "all of the Room's
  Loci" over a fixed 26.
- **Loci added after learning.** The earlier flawless run no longer covers the Room, so it
  is learning again until a flawless run includes the new Loci. The check uses Positions,
  not Locus IDs, so tools that re-create Loci in place do not reset a Room.
- **Loop drills from earlier versions** were not recorded. Such a Room's clock starts at
  its next drill.

## Alternatives considered

- **Reuse `RoomLearningSessions`.** It already has `Start` and `End` per Room. But other
  tools maintain it, it stores `DD-MM-YYYY` text, and ADR 0003 decided not to write it.
- **Save every loop drill.** That would allow more analysis, such as the number of drills
  before learning. But it turns practice into stored history, and the user asked only
  for the first start.
- **Start the clock at the first rehearsal.** Rejected: the user defined the start as the
  first loop drill.

## Consequences

- `palace.db` changes the first time each Room is loop-drilled, not only when a rehearsal
  completes.
- If the database is locked or read-only, the drill panel says the learning clock was not
  saved. The drill itself still runs.
- Deleting a Room's `FirstLoopDrills` row restarts its clock at the next drill.
- Verification sends these writes to its progress copy, as with rehearsals.
