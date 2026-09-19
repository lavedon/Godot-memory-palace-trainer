# Read palace.db directly from Godot via C# / Microsoft.Data.Sqlite

The viewer is built with the Godot 4.x **.NET (C#)** build and reads the shared
`C:\tools\Data\palace.db` SQLite file **directly at runtime** using
`Microsoft.Data.Sqlite`, running `SELECT Position, Text FROM Loci WHERE RoomId = @id`
(plus the room's `Title`/`RoomImage`) when a room loads.

We chose C# because the surrounding memory-palace tooling is already .NET
(`b.exe`, `pal.exe`, `quiz.exe`, and the `e_sqlite3.dll` native lib all live in
`C:\tools`), so C# reuses that ecosystem with no third-party engine addon. The two
alternatives were rejected: **GDScript** would need the `godot-sqlite` GDExtension
(a vendored C++ binary) to touch SQLite; an **offline JSON export** step would add a
build stage and a second, drift-prone copy of the data. Reading the DB directly keeps
`palace.db` the single source of truth.

## Consequences

- The demo is coupled to the local machine layout (the absolute DB path and the native
  `e_sqlite3.dll`). A `--db <path>` override exists, but there is no cross-machine story.
- Running the project requires the Godot **.NET** build, not the standard build.
