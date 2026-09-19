# Validation — 2026-09-19

Built and exercised on Windows x64 with the pinned toolchain in
[IMPLEMENTATION.md](IMPLEMENTATION.md). The renderer used the NVIDIA RTX 3080 Ti
Laptop GPU through Godot's OpenGL compatibility renderer.

## Automated acceptance

- **37/37 core checks passed**: CLI validation, full/partial/empty Rooms, stable gaps,
  overflow, lowest-Id duplicates, null/fractional/text/negative/zero/huge Positions,
  long Unicode text, invalid titles/text, absent/corrupt/incompatible databases,
  exclusive locks, read-only files, connection disposal, unchanged bytes, and the
  independent diagram-to-coordinate checks.
- **15/15 editor runtime cases passed** with isolated fixtures and real Rooms 7/8.
- **15/15 Windows export cases passed**, totaling 453 assertions/capture checks.
  These include actual Godot key-event dispatch for J/K, key-repeat handling,
  four-wall collision, heading-relative movement, fixed eye height, pitch limits,
  stable Anchors, mouse capture/release, and visible errors/warnings.
- The export ran from a separate fixture working directory with PATH restricted
  to Windows system directories. Its process module report confirms both
  `coreclr.dll` and `e_sqlite3.dll` loaded from
  `artifacts/windows/data_PalaceRoomViewer_windows_x86_64/`.
- The fixture database and shared `palace.db` were unchanged. Shared database
  SHA-256 before and after:
  `3a49b5eeecb0e8831c9baa49258234c1672346c0d4c162db0030e53c17e7d9a1`.

Reproduce with PowerShell 7:

```powershell
.\scripts\export.ps1
.\scripts\verify.ps1 -Export -Visual
```

The recorded export run is
`artifacts/verification/20260919-144401-d3c884/summary.json`. Each case contains
`result.json`, stdout/stderr logs, and visual captures. A failed load is a passing
acceptance case only when it displays the expected useful error.

## Visual inspection

Captured all 26 populated Positions in Room 8 with all text enabled, plus front,
corner, floor, and ceiling views. Inspected representative wall/corner Slices and
floor/ceiling text, Room 7's visible warnings for Loci 87–89 at Positions 27–29,
empty markers, and missing-argument instructions in the exported application.

The longest current Locus is Room 2 / Locus 20 / Position 10 (117 characters).
Its full text wraps legibly at a suitable walking viewpoint; the capture is in
`artifacts/verification/longest-current-room2/position-10.png`. A separate synthetic
Room holds more than 1,200 characters per Locus, including Japanese and accented
text, at wall/floor/ceiling Positions. Full content is retained in Billboards and
the scrollable reading panel.

Readability depends on viewing distance. Longer text uses a larger wrapping width
and a bounded physical area; very long text can become small, especially on the
ceiling. The reading panel supplies full text at a consistent screen font size.
This is a local Windows demo; other GPUs and operating systems were not tested.
