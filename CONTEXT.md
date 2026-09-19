# Palace Room Viewer

A Godot 3D demo that renders a single memory-palace **Room** from `palace.db` as a
walkable rectangular room, placing each **Locus**'s text at its numbered **Position**.
This glossary defines the shared language of the memory-palace domain as it appears in
this viewer. It is a glossary only — no implementation details.

## Language

**Palace**:
An ordered collection of Rooms forming one memory journey. A Room belongs to exactly one Palace.
_Avoid_: memory house, building

**Room**:
One node of a Palace, holding up to 26 Loci. The viewer renders exactly one Room at a time as a
rectangular box (depth greater than width — the left/right walls are longer than front/back).
_Avoid_: scene, level, cube

**Locus** (plural **Loci**):
A single memorized item within a Room — its `Text` is the fact encoded, placed at one Position.
The canonical Room holds 26 Loci; more than 26 is malformed data.
_Avoid_: point, spot, item, station

**Position**:
The integer 1–26 giving a Locus's fixed place in the room. 1–24 are wall placements
(8 wall-slots × 3 Slices), 25 is the floor, 26 is the ceiling. Contiguous 1..N in practice.
_Avoid_: index, slot (reserve "slot" for Peg slots and wall-slots)

**Slice**:
One of three horizontal bands of the room stacked bottom-to-top: Slice 1 = where floor
meets wall, Slice 2 = center height, Slice 3 = where wall meets ceiling.
_Avoid_: layer, level, tier

**Wall-slot**:
One of the 8 placements around a Slice's perimeter (4 corners + 4 wall-centers).
Position = (wall-slot − 1) × 3 + Slice for the 24 wall Loci.

**Anchor / Marker**:
The fixed in-world spot for one Position. When a Position has no Locus, the viewer still shows
a faint numbered marker there for orientation. "Anchor" = the location; "marker" = its faint visual.
_Avoid_: placeholder

**Billboard**:
The camera-facing text overlay showing a Locus's `Text` at its Anchor. Toggled with `j`.
_Avoid_: label, sign, card

**Peg**:
A reusable mnemonic image for a concept (e.g. "CORS" → "Coors beer"), linked to Loci.
Present in the data model; not yet rendered by the viewer.

## Flagged ambiguities

- **"Copy a room over"** (from the original plan) was ambiguous. Resolved: the viewer reads
  `palace.db` directly at runtime; nothing is copied or exported.
- **Position 22**: the plan's Slice-1 diagram mislabeled the bottom-left corner as 21
  (a collision with Slice-3's left-middle). Correct value is 22.
