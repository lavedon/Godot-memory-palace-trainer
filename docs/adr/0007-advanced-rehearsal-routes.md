# Advanced rehearsal routes keep their own bests

The user wanted a harder rehearsal, unlocked once a Room earns Gold, that walks the Room
band by band: the 8 wall-slots of one Slice, then the next, ending with floor and
ceiling. Each run picks top-band-first or bottom-band-first at random.

An advanced rehearsal is a full rehearsal of every Locus, so it is saved to
`RehearsalRuns` like any other (the user chose this over practice-only). The run records
its route in a new nullable column:

```sql
Route TEXT   -- TopFirst | BottomFirst; NULL = Position order
```

Older tables gain the column with `ALTER TABLE RehearsalRuns ADD COLUMN Route TEXT` on
their next save, inside the save transaction, exactly as `LocusIds` was added (ADR 0004).
`NULL` keeps every existing row meaning Position order. A row with a route this version
does not know is skipped when loading, since its times compare with nothing.

## What compares by route, and what does not

Split times are cumulative along the walk, so a band-order run's splits cannot be compared
with a Position-order run's. Times are therefore compared only between runs on the same
route: the ghost to beat, "new personal best", "first clear", the **Faster Than Before**
trophy, and the per-route bests in the Palace menu. The menu's main best time and medal
column remain Position-order rehearsals.

Everything else treats an advanced run as the full rehearsal it is: learning a Room
(ADR 0005), the unlock itself (Gold or better on any route), streaks, other trophies, and
FSRS forecasts (ADR 0004), which read `Positions` and `LocusIds` index by index and so do
not depend on order.

## Alternatives considered

- **One shared best time.** Simpler, but ghost splits would compare different walks and
  report misleading "ahead/behind" numbers.
- **Practice only, not saved** (like section rehearsals, ADR 0006). It would leave a full,
  harder test of the Room out of its history.

## Consequences

- The first advanced save in an older `palace.db` adds the `Route` column.
- Other tools reading `RehearsalRuns` should treat a non-`NULL` `Route` as a different walk
  when comparing times.
- A saved key-bindings file from before this version gets **G** for the new action, unless
  G is already bound to something that would clash; then the action starts unbound.
