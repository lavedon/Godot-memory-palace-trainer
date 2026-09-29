# Record rehearsal runs in palace.db

The user asked for timed rehearsals with personal bests stored in a new table in
`palace.db`. This supersedes ADR 0001's rule that the viewer never writes the shared
database, but only for this table.

The viewer stores each completed rehearsal as one row in `RehearsalRuns`. It creates the
table and its index with `CREATE TABLE IF NOT EXISTS` in the same `BEGIN IMMEDIATE`
transaction as the first INSERT. The write connection uses `Mode=ReadWrite`, never
`ReadWriteCreate`, so a wrong path cannot create an empty database. Loading Rooms and
Loci still uses a read-only connection, and the viewer never alters existing tables.

We did not require an explicit migration step (as ADR 0002 does for image columns).
Creating a new, empty, independent table cannot damage existing data, and a manual
step would put a hurdle in front of the first rehearsal. The existing
`RoomLearningSessions` table was not reused: its dates are `DD-MM-YYYY` text with no
outcome columns, and other tools may depend on its meaning.

Rows keep queryable summary columns (`DurationMs`, `FirstPassKnown`, `Rounds`,
`BestCombo`, `Score`, `Medal`) alongside JSON detail columns (`Positions`,
`MissesByRound`, `SplitsMs`, `FirstPassMissedLocusIds`). Other tools, such as a future
Anki add-on, can read them with plain SQL. Personal bests, streaks and achievements are
derived from these rows, so they need no further tables. Rows that fail to parse are
skipped rather than hiding the whole history.

## Consequences

- `palace.db` changes whenever a rehearsal completes. Backups made before that point
  will not contain the latest runs.
- If the database is locked or read-only, the rehearsal result says the run was not
  saved; the rehearsal itself is unaffected.
- Automated verification sends rehearsal writes to a SQLite backup copy in its output
  directory, so its database-unchanged checks remain meaningful and it never writes the
  shared database.
