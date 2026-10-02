# Forecast reviews with FSRS through a Rust wrapper

The user wanted the viewer to suggest which Rooms to review next. Anki already schedules
each Locus as a flashcard, but walking a Room tests a different skill (recalling each
image from its place, in order), so the viewer schedules from its own rehearsal history
and never reads or writes Anki.

We use FSRS, called through a small Rust library in `Native/fsrs-ffi` that wraps the
official `fsrs` crate (pinned with `=6.6.2` and `Cargo.lock`). It exports four C
functions: an ABI version, the default parameters, batched memory states from review
histories, and retrievability. `Core/Fsrs.cs` calls them with `LibraryImport` and checks
the ABI version before the first call.

Alternatives considered:

- **Port the formulas to C#.** About 100 lines with no new toolchain, but we would track
  FSRS changes by hand and would have no parameter optimiser.
- **dotnet-fsrs.** A small community port with unclear FSRS version, licence and
  maintenance.
- **fsrs-rs-c (the official C binding).** Its exported surface and release cadence were
  unclear. Our own wrapper exposes only what the viewer needs and pins the crate version.

The wrapper keeps the official implementation, leaves room to add the optimiser later
(`compute_parameters`) for personal parameters, and was the user's choice.

## How history becomes FSRS reviews

- Only completed rehearsals (`RehearsalRuns`) count. Loop drills are cramming and are
  never saved.
- Only the first pass counts: later rounds re-ask misses in the same session.
- Only a Room's first rehearsal each day counts, because FSRS schedules in whole days.
- Knew it maps to *Good* (3) and missed to *Again* (1).
- Rooms are ranked by expected forgotten Loci, the sum of (1 − recall) over the Room's
  Loci. A Locus never tested in a counted rehearsal counts as fully forgotten. Rooms
  never rehearsed are "new", not due.

## Locus identity

Runs saved before this change record Locus IDs only for first-pass misses. To keep
history attached to the right Locus when a Room is edited, each run now also stores
`LocusIds`, aligned with `Positions`. The viewer adds this nullable column with
`ALTER TABLE RehearsalRuns ADD COLUMN LocusIds TEXT` on the first save after upgrading,
inside the same `BEGIN IMMEDIATE` transaction as the INSERT. This changes only the
viewer's own table; ADR 0003's rule that no other table is altered still holds. Older
rows map Positions to the Room's current Loci.

## Consequences

- Building now needs Rust (cargo 1.85+). The Core project's build compiles the
  wrapper, and `bootstrap.ps1` fails early with instructions if cargo is missing.
- `fsrs_ffi.dll` ships beside the viewer's assemblies. Because Godot loads assemblies
  into its own load context, `Fsrs` registers a resolver that loads the DLL from the
  assembly's own directory. The export script and verification check that the exported
  viewer loads its own copy.
- If the DLL cannot load, the Palace menu shows "review forecasts unavailable" and
  everything else keeps working.
