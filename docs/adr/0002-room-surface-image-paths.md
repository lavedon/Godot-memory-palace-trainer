# Store optional images for all six Room surfaces

The user requested six image links in `Rooms` and automatic image loading when a
Room is opened. Each link is a nullable TEXT file path: `LeftImagePath`,
`RightImagePath`, `ForwardImagePath`, `BackImagePath`, `FloorImagePath`, and
`CeilingImagePath`. `ForwardImagePath` means the fixed FRONT wall. Existing
`RoomImage` values retain their previous meaning and are not repurposed.

Paths can be absolute or relative to the database directory. Relative paths make
the database and its image directory portable as a unit. NULL and whitespace-only
values mean no custom image. Invalid paths, non-text values, and decode failures
produce warnings, preserving the Room and its unaffected surfaces.

Image precedence is individual CLI path, then matching `--room-textures` folder
file, then database value, then default material. Absent folder filenames do not
erase database values. An explicitly selected but unusable override falls back to
the default material with a warning, rather than silently reusing a lower-priority image.

The viewer stays read-only, detects available image columns, and supports both old
and migrated databases. A separate .NET maintenance tool performs the migration;
this adds an explicitly requested migration workflow to ADR 0001 without changing
the viewer's read-only contract. It refuses missing files, checks column types,
holds a write reservation, takes a SQLite API backup through a separate reader,
then adds missing columns transactionally and checks integrity before committing.
Backups include committed WAL content. Repeat runs preserve existing values and
do nothing when all six compatible columns exist. No unrelated schema version or
migration-history table is modified.

The migration leaves all new values NULL. Image assignment is a separate database
edit; the viewer does not edit paths or copy image files.

References: [SQLite ADD COLUMN](https://www.sqlite.org/lang_altertable.html#alter_table_add_column)
and [Microsoft.Data.Sqlite backup](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/backup).
