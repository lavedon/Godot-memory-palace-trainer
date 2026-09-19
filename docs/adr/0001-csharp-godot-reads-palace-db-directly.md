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

## Agreed review addendum — 2026-09-19

The C# and direct-database decisions stand. This addendum supersedes the original
statements about an existing argument override, the shared native library, and
`RoomImage` loading. At review time, this repository contains planning documents only;
the behavior described here is agreed work to implement, not existing functionality.

### Read-only access and loading contract

- Open the database explicitly with `Mode=ReadOnly`, preferably through
  `SqliteConnectionStringBuilder`. The provider's default is `ReadWriteCreate`, which
  can create an empty database at an incorrect path. Never create, migrate, or repair
  the shared database from this viewer.
- Require a `--room <id>` application argument and implement the optional `--db <path>`
  override. Keep `C:\tools\Data\palace.db` as the default path.
- Parameterize the Room ID, load the Room's title and its Loci, then promptly dispose
  of the database connection. Query results are a snapshot at load time; direct access
  does not imply continuous refresh or querying on every rendered frame.
- Map each Locus by its Position value. Validate supported Positions and report missing
  Rooms, malformed data, inaccessible files, incompatible schemas, and access failures
  as described in [the demo plan](../../room-loci.md#loading-and-validation).
- Defer `RoomImage` loading and rendering for the first demo. Its display behavior has
  not been specified, and it is unnecessary to prove the text-placement concept.

See Microsoft's
[connection-string documentation](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/connection-strings)
for connection modes and the connection-string builder.

### Native dependencies and toolchain

Use the project's `Microsoft.Data.Sqlite` NuGet dependency and its native runtime
assets. The main package brings in `SQLitePCLRaw.bundle_e_sqlite3` by default; the
presence of `C:\tools\e_sqlite3.dll` is context about the surrounding tooling, not a
reason to depend on that particular file. C# avoids an engine-specific SQLite addon,
but still requires a native SQLite library to be packaged correctly. See Microsoft's
[SQLite dependency documentation](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/custom-versions).

Before implementation, select and record exact versions of the Godot .NET editor,
matching export templates, .NET SDK, and `Microsoft.Data.Sqlite` package, along with
the initial Windows target architecture. Numerical versions remain to be selected;
"Godot 4.x" alone is not a reproducible toolchain specification. Development requires
the .NET SDK as well as the .NET edition of Godot; see
[Godot's C# prerequisites](https://docs.godotengine.org/en/stable/tutorials/scripting/c_sharp/c_sharp_basics.html#prerequisites).

Verify the exported Windows application can load its native SQLite dependency without
the editor or the shared `C:\tools\e_sqlite3.dll`. Test a database path outside the
default location. The initial target remains a local Windows demo; package-managed
dependencies and a path override remove unnecessary machine coupling without adding
a broader cross-platform distribution requirement.
