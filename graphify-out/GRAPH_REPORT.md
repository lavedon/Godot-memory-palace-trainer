# Graph Report - C:\my-coding-projects\godotPalaceRoomViewer  (2026-09-19)

## Corpus Check
- Corpus is ~3,640 words and fits in one context window; this focused requirements graph supports implementation traceability.

## Summary
- 61 nodes · 101 edges · 8 communities
- Extraction: 100% EXTRACTED · 0% INFERRED · 0% AMBIGUOUS
- Token cost: unavailable; the host delegation API did not report input or output counts.

## Community Hubs (Navigation)
- [Community 0: Room layout and validation](#community-0---room-layout-and-validation)
- [Community 1: Architecture and Windows deployment](#community-1---architecture-and-windows-deployment)
- [Community 2: Stable Position identities](#community-2---stable-position-identities)
- [Community 3: Walking and visual acceptance](#community-3---walking-and-visual-acceptance)
- [Community 4: Safe loading and fixtures](#community-4---safe-loading-and-fixtures)
- [Community 5: Domain and deferred scope](#community-5---domain-and-deferred-scope)
- [Community 6: Independent text and markers](#community-6---independent-text-and-markers)
- [Community 7: Room snapshot queries](#community-7---room-snapshot-queries)

## God Nodes (most connected - your core abstractions)
1. `Single Room Windows demo` - 35 edges
2. `Fixed Anchors and faint markers` - 7 edges
3. `Stable Position identity` - 5 edges
4. `Billboard` - 5 edges
5. `Movement and text-toggle acceptance` - 5 edges
6. `Godot .NET with direct SQLite` - 5 edges
7. `Locus` - 4 edges
8. `Visible skipped-Loci warnings` - 4 edges
9. `Preserve missing Positions` - 4 edges
10. `Shared database unchanged` - 4 edges

## Surprising Connections (you probably didn't know these)
- `Exact wall Position diagrams` --locates--> `Stable Position identity`  [EXTRACTED]
  room-loci.md → CONTEXT.md
- `Centered floor and ceiling Anchors` --locates--> `Fixed Anchors and faint markers`  [EXTRACTED]
  room-loci.md → CONTEXT.md
- `K toggles numbered markers` --toggles_markers--> `Fixed Anchors and faint markers`  [EXTRACTED]
  room-loci.md → CONTEXT.md
- `Inward text offsets` --preserves--> `Fixed Anchors and faint markers`  [EXTRACTED]
  room-loci.md → CONTEXT.md
- `Full wrapped readable text` --requires_readability--> `Billboard`  [EXTRACTED]
  room-loci.md → CONTEXT.md

## Import Cycles
- None detected.

## Hyperedges (group relationships)
- **Read-only Room loading contract** — room_loci_room_arg, room_loci_db_arg, adr_0001_csharp_godot_reads_palace_db_directly_read_only, adr_0001_csharp_godot_reads_palace_db_directly_parameterization, adr_0001_csharp_godot_reads_palace_db_directly_snapshot_load, room_loci_warnings, room_loci_errors [EXTRACTED 1.00]
- **Demo acceptance suite** — room_loci_full_acceptance, room_loci_partial_acceptance, room_loci_overflow_acceptance, room_loci_errors_acceptance, room_loci_controls_acceptance, room_loci_export_acceptance [EXTRACTED 1.00]

## Communities (8 total, 0 thin omitted)

### Community 0 - "Room layout and validation"
Cohesion: 0.24
Nodes (14): World-fixed Room orientation, Three-stage build order, Record coordinate convention first, Single Room Windows demo, Lowest Id wins duplicates, Obvious front-wall cue, Full Room acceptance, Overflow, null and duplicate acceptance (+6 more)

### Community 1 - "Architecture and Windows deployment"
Cohesion: 0.18
Nodes (13): Database ADR addendum, Godot .NET with direct SQLite, Test alternate database path, Godot .NET and SDK prerequisites, Package-managed native SQLite, Rejected integration alternatives, Pin exact Windows toolchain, Position 22 correction resolved (+5 more)

### Community 2 - "Stable Position identities"
Cohesion: 0.38
Nodes (7): Fixed Anchors and faint markers, Stable Position identity, Horizontal Slices, Eight wall-slots, Empty and missing Rooms differ, Preserve missing Positions, Partial, gapped and empty acceptance

### Community 3 - "Walking and visual acceptance"
Cohesion: 0.38
Nodes (7): Tune speed and look limits, Movement and text-toggle acceptance, Centered floor and ceiling Anchors, Full wrapped readable text, Mouse capture and release, All 26 Billboard visual checks, Fixed-height walking with wall collision

### Community 4 - "Safe loading and fixtures"
Cohesion: 0.33
Nodes (6): Explicit Mode=ReadOnly, Shared database unchanged, Visible loading errors, Failure paths never create or mutate data, Isolated validation fixtures, Schema is reference transcription

### Community 5 - "Domain and deferred scope"
Cohesion: 0.33
Nodes (6): Locus, Palace, Peg, Room, Viewer capacity is 26 Positions, Deferred features

### Community 6 - "Independent text and markers"
Cohesion: 0.40
Nodes (5): Billboard, J toggles populated text, K toggles numbered markers, Label3D initial candidate, Inward text offsets

### Community 7 - "Room snapshot queries"
Cohesion: 0.67
Nodes (3): Parameterized Room query, Promptly dispose snapshot connection, Required --room argument

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `Single Room Windows demo` connect `Room layout and validation` to `Architecture and Windows deployment`, `Stable Position identities`, `Walking and visual acceptance`, `Safe loading and fixtures`, `Domain and deferred scope`, `Independent text and markers`, `Room snapshot queries`?**
  _High betweenness centrality (0.803) - this node is a cross-community bridge._
- **Why does `Godot .NET with direct SQLite` connect `Architecture and Windows deployment` to `Safe loading and fixtures`?**
  _High betweenness centrality (0.208) - this node is a cross-community bridge._
- **Why does `Standalone Windows export acceptance` connect `Architecture and Windows deployment` to `Room layout and validation`?**
  _High betweenness centrality (0.179) - this node is a cross-community bridge._
- **What connects `Palace`, `Horizontal Slices`, `Viewer capacity is 26 Positions` to the rest of the system?**
  _8 weakly-connected nodes found - possible documentation gaps or missing edges._

## Scope and precedence

This graph captures requirements from the three original Markdown specifications only. It does not certify implementation or test completion. All extracted relationships have source locations. The 2026-09-19 addenda supersede conflicting original wording. Exact dimensions, camera pose, versions, movement and text settings are intentionally left as implementation decisions.

## Acceptance checklist

- [ ] **Record coordinate convention first**: Record diagram front edge and viewing direction, world axes, dimensions, camera start position and facing before implementation. Source: `room-loci.md:281`.
- [ ] **Pin exact Windows toolchain**: Record exact Godot .NET editor, matching export templates, .NET SDK, Microsoft.Data.Sqlite version and initial Windows architecture before implementation. Source: `docs/adr/0001-csharp-godot-reads-palace-db-directly.md:59`.
- [ ] **Full Room acceptance**: Every Locus appears at its numbered Anchor and the Room title displays. Source: `room-loci.md:345`.
- [ ] **Partial, gapped and empty acceptance**: Partial Rooms and gap fixtures preserve identities; empty Room retains all 26 markers. Source: `room-loci.md:346`.
- [ ] **Overflow, null and duplicate acceptance**: Room 7 warns visibly with skipped 27–29; isolated null and duplicate fixtures warn while rendering valid Loci. Source: `room-loci.md:348`.
- [ ] **Failure paths never create or mutate data**: Useful loading errors without database creation or existing record changes. Source: `room-loci.md:351`.
- [ ] **Movement and text-toggle acceptance**: Verify walls, fixed eye height, mouse release/recapture, all 26 readable from suitable positions, and repeated J affects text only. Source: `room-loci.md:353`.
- [ ] **All 26 Billboard visual checks**: Inspect floor/ceiling while pitching, corners from multiple walking positions, longest current text, longer wrapping sample, and overlap/clipping with all 26 enabled. Source: `room-loci.md:313`.
- [ ] **Standalone Windows export acceptance**: Test exported application as well as editor: packaged SQLite, --db override, no dependency on editor or C:\tools\e_sqlite3.dll. Source: `room-loci.md:356`.
- [ ] **Isolated validation fixtures**: Exercise invalid and synthetic cases in separate test databases; never modify shared palace.db. Source: `room-loci.md:359`.
