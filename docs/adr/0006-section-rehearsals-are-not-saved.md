# Section rehearsals are practice and are not saved

The user wanted to rehearse part of a Room the way a full rehearsal works (walk the
Positions, then bring the camera back to each round's misses until a clean round), while
keeping a flawless rehearsal of the **whole** Room as what counts a Room as known.

A section rehearsal is started from the loop prompt with `r <range>` (for example
`r 1-6`). It uses the same `RehearsalSession` as a full rehearsal, limited to the chosen
Positions, and shows its own result card. It is never written to `RehearsalRuns`, and it
does not start a Room's learning clock (`FirstLoopDrills`).

## Why it is not saved

Everything the viewer derives from history reads `RehearsalRuns`: learned status
(ADR 0005), personal bests and ghost splits, medals, streaks, trophies, and FSRS review
forecasts (ADR 0004). A short section would set unbeatable "best times", earn medals for a
handful of Loci, extend streaks and feed FSRS a partial walk. Keeping sections out of the
table leaves all of these meaning "the whole Room", with no filtering needed anywhere.
A section that happens to cover every Position still does not count; **R** is the test.

## Alternatives considered

- **Save sections with a flag** and filter them out everywhere. More analysis would be
  possible later, but every reader of `RehearsalRuns` (including other tools) would have
  to know about the flag.
- **Count section first passes for FSRS only.** They are real recall tests, but the user
  asked for the whole-Room rehearsal to remain what counts.

## Consequences

- Section practice leaves no trace in `palace.db`, like loop drills.
- Ghost splits are not shown in a section, since they compare with full-Room runs.
