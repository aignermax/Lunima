# GDS Export Coordinate System: Root Cause Analysis

**Issue #458** | Date: 2026-04-07
**Status: Active Bug — Multi-Segment Waveguides Off by Component Height**

---

## Executive Summary

There are **three distinct, fundamental bugs** in the GDS export pipeline that cause waveguide endpoints to miss component pins by tens to hundreds of micrometers:

1. **Multi-segment waveguide coordinate discontinuity** — Segment 1 uses the correct Nazca pin position, but all subsequent segments use a naive Y-flip of routing coordinates. For components where `NazcaOriginOffsetY ≠ HeightMicrometers/2`, this causes a systematic offset equal to `HeightMicrometers - 2 × NazcaOriginOffsetY` at every segment transition.

2. **Legacy NazcaExporter.cs is completely broken** — The old exporter (`Connect-A-Pic-Core/CodeExporter/NazcaExporter.cs`) places components without Y-flip, without `NazcaOriginOffset`, and with the wrong rotation sign.

3. **Routing operates in editor space, export in Nazca space — never reconciled** — The pathfinding algorithm computes waypoints from `GetAbsolutePosition()` (editor Y-down), but the export infrastructure uses `GetAbsoluteNazcaPosition()` (Nazca Y-up with origin offset). These two representations are only equivalent for a special subset of component configurations.

---

## Coordinate System Dictionary

| System | Origin | Y direction | Used by |
|--------|--------|-------------|---------|
| **Editor space** | Top-left of canvas | Down (+Y = lower on screen) | `GetAbsolutePosition()`, routing algorithm, all `PhysicalX/Y` coordinates |
| **Nazca space** | Cell `.put()` origin | Up (+Y = higher in GDS viewer) | Nazca/GDS export, `GetAbsoluteNazcaPosition()` |

The conversion is **not** simply `Y_nazca = -Y_editor`. The correct formula depends on `NazcaOriginOffset`.

---

## How Pins Are Defined (PDK JSON → C# → GDS)

1. **PDK JSON** defines each component with `physicalPins[].offsetX/Y` relative to the component's **top-left corner** in editor space.
2. **`PdkLoader`** reads these into `PhysicalPin.OffsetXMicrometers` / `OffsetYMicrometers`.
3. **`Component.NazcaOriginOffsetX/Y`** stores the Nazca cell's `(0,0)` origin position relative to the top-left corner in editor coordinates.
4. **`GetAbsoluteNazcaPosition()`** converts a pin to Nazca world coordinates accounting for origin offset and rotation.

### The Critical Formula (`PhysicalPin.cs:38-58`)

For a component at editor position `(PhysX, PhysY)` with `NazcaOriginOffset = (ox, oy)`, at `rotation = 0°`:

```
nazcaCompX = PhysX + ox
nazcaCompY = -(PhysY + oy)

localPinNazcaX = OffsetX - ox
localPinNazcaY = (H - OffsetY) - oy      # H = HeightMicrometers

pinNazcaX = nazcaCompX + localPinNazcaX = PhysX + OffsetX
pinNazcaY = nazcaCompY + localPinNazcaY  = -(PhysY + oy) + (H - OffsetY - oy)
                                         = -(PhysY + OffsetY) + H - 2·oy
```

**Key insight**: `pinNazcaY = -(PhysY + OffsetY) + (H - 2·oy)`

The simple Y-flip `-(PhysY + OffsetY)` would only be correct if `H - 2·oy = 0`, i.e., `oy = H/2`.

---

## Bug 1: Multi-Segment Path Coordinate Discontinuity

### Location

`CAP.Avalonia/Services/SimpleNazcaExporter.cs:420-443` — method `AppendSegmentExport()`

### The Bug

```csharp
for (int i = 0; i < segments.Count; i++)
{
    bool isFirst = (i == 0);
    double nX, nY;

    if (isFirst && startPin != null)
    {
        // Correct: uses full NazcaOriginOffset + rotation math
        (nX, nY) = startPin.GetAbsoluteNazcaPosition();
    }
    else
    {
        // BUG: naive Y-flip of routing coordinates
        // Routing was computed from GetAbsolutePosition(), NOT GetAbsoluteNazcaPosition()
        nX = segments[i].StartPoint.X;
        nY = -segments[i].StartPoint.Y;      // ← WRONG for non-trivial NazcaOriginOffset
    }

    sb.AppendLine(FormatSegmentAbsolute(segments[i], nX, nY));
}
```

### The Root Problem

The pathfinding algorithm builds segments starting from `GetAbsolutePosition()`:

```csharp
var (sx, sy) = startPin.GetAbsolutePosition();     // editor coords: (PhysX + OffsetX, PhysY + OffsetY)
path.Segments.Add(new StraightSegment(sx, sy, ...));
```

So `segments[1].StartPoint` is in **editor space**. Applying `-Y` gives `-(PhysY + OffsetY)`.

But Segment 1 starts at `GetAbsoluteNazcaPosition().Y = -(PhysY + OffsetY) + H - 2·oy`.

**The coordinate jump at segment 1→2 boundary = `H - 2·oy` µm.**

### Quantified Impact

| Component type | NazcaOriginOffsetY `oy` | Component height `H` | Error at transition |
|----------------|------------------------|---------------------|---------------------|
| Legacy (no PDK func) | `H` | any | `H - 2H = -H` µm ← **off by full height** |
| demo_pdk with offset=(0,0) | `0` | 50 µm | `50 - 0 = 50` µm |
| demo_pdk MMI2x2 pin a0 (OffsetY=12.5, H=50) | `0` | 50 µm | `50` µm at transition |
| SiEPIC GC (oy=9.5, H=19) | `9.5` | 19 µm | `19 - 19 = 0` µm ← **correct!** |
| 1-tile legacy component | `250` | 250 µm | `-250` µm ← **off by 250 µm!** |

**This explains the ~100 µm reported offset.** A multi-tile component with H=250 µm and a multi-segment waveguide will show a 250 µm misalignment for all segments after the first.

### Why Single-Segment Paths Work

For single straight segments, `FormatStraightSegmentFromPins()` is called, which recomputes BOTH endpoints directly from `GetAbsoluteNazcaPosition()` — bypassing the routing coordinates entirely:

```csharp
var (sx, sy) = startPin.GetAbsoluteNazcaPosition();   // correct
var (ex, ey) = endPin.GetAbsoluteNazcaPosition();     // correct
```

This is why simple GC-to-GC straight connections look right, but curved/multi-hop routes fail.

---

## Bug 2: Legacy NazcaExporter.cs Is Completely Wrong

### Location

`Connect-A-Pic-Core/CodeExporter/NazcaExporter.cs:66-78`

### All Three Mistakes

```csharp
private string ExportComponentPhysical(Component component)
{
    // BUG 1: No Y-flip. Nazca uses Y-up, so posY should be -(component.PhysicalY + oy)
    var posY = component.PhysicalY.ToString("F3", CultureInfo.InvariantCulture);

    // BUG 2: Wrong rotation sign. Nazca requires -RotationDegrees (Y-flip inverts rotation)
    var rotation = component.RotationDegrees.ToString("F1", CultureInfo.InvariantCulture);

    // BUG 3: No NazcaOriginOffset applied. PDK components need offset compensation.
    return $"        {cellName} = CAPICPDK.{component.NazcaFunctionName}({parameters})" +
           $".put({posX}, {posY}, {rotation})\n";   // all three values wrong
}
```

**This exporter produces scripts where every component is placed at the wrong Y position with the wrong orientation.** It should be considered non-functional and replaced or retired.

---

## Bug 3: Routing–Export Coordinate System Mismatch (Architectural)

### The Architecture Problem

The system has a fundamental split:

```
Routing (pathfinding)     → editor space  (Y-down, origin top-left)
Stub cell pin definitions → Nazca space  (Y-up, local cell origin)
Multi-segment export      → mixed!       (segment 1 = Nazca, segments 2+ = editor Y-flip)
```

The routing algorithm does not know about Nazca coordinates. It produces waypoints in editor space. The export then needs to convert these, but the conversion function (`GetAbsoluteNazcaPosition`) is pin-centric and applies `NazcaOriginOffset` — it is NOT equivalent to a simple Y-flip for most components.

### Why This Was Hidden Until Now

Single-segment exports (the common case for simple designs) use `FormatStraightSegmentFromPins()` which entirely ignores routing coordinates and recomputes from pins in Nazca space. This masks the underlying mismatch.

The bug manifests ONLY when:
1. A waveguide has more than one segment (i.e., any bend or indirect route)
2. The component's `NazcaOriginOffsetY ≠ HeightMicrometers / 2`

For GC components (oy=9.5, H=19 → oy=H/2), even multi-segment paths accidentally work. But for almost any other component type, multi-segment paths will be misaligned.

---

## The Coordinate Transform Chain (Visual)

```
PDK JSON                Editor Canvas              Nazca Export
(component def)         (UI display)               (GDS output)
─────────────────────────────────────────────────────────────────

offsetX, offsetY        PhysX + offsetX            same (X unchanged)
(relative to           PhysY + offsetY            -(PhysY + oy) + (H - offsetY - oy)
 top-left)              Y-down                     Y-up, with origin offset

NazcaOriginOffset       (stored but not            Applied via CalculateOriginOffset()
(oy, ox)                displayed in UI)           in both PhysicalPin and Exporter

RotationDegrees         Displayed in UI            Negated for Nazca: -RotationDegrees

Path segments           Computed from              Should derive from Nazca pin coords
                        GetAbsolutePosition()      but currently only segment 1 does
```

---

## Where Coordinates Are Currently Handled Correctly

| Location | Status | Notes |
|----------|--------|-------|
| `PhysicalPin.GetAbsoluteNazcaPosition()` | ✅ Correct | Full origin offset + rotation math |
| `SimpleNazcaExporter.AppendSingleComponent()` | ✅ Correct | Uses `CalculateOriginOffset()` properly |
| `SimpleNazcaExporter.FormatStraightSegmentFromPins()` | ✅ Correct | Single-segment pin-to-pin |
| `SimpleNazcaExporter.AppendStandardComponentStub()` | ✅ Correct | Stub pin definitions |
| `SimpleNazcaExporter.AppendSegmentExport()` segment 1 | ✅ Correct | Uses `GetAbsoluteNazcaPosition()` |
| `SimpleNazcaExporter.AppendSegmentExport()` segments 2+ | ❌ **BUG** | Naive Y-flip of routing coords |
| `NazcaExporter.ExportComponentPhysical()` | ❌ **BUG** | No Y-flip, wrong rotation, no origin offset |
| Routing algorithm (pathfinding) | ⚠️ N/A | Correct in its own coordinate system |

---

## How to Fix

### Fix 1: Multi-Segment Path Export (Priority: High)

**Option A (Minimal):** At the start of `AppendSegmentExport()`, compute the translation delta between `GetAbsoluteNazcaPosition()` and the simple Y-flip, then apply it to ALL routing segments:

```csharp
// Compute offset between Nazca pin position and naive Y-flip
var (pinNazcaX, pinNazcaY) = startPin.GetAbsoluteNazcaPosition();
var (editorX, editorY) = startPin.GetAbsolutePosition();
double deltaX = pinNazcaX - editorX;
double deltaY = pinNazcaY - (-editorY);

for (int i = 0; i < segments.Count; i++)
{
    double nX = segments[i].StartPoint.X + deltaX;
    double nY = -segments[i].StartPoint.Y + deltaY;
    sb.AppendLine(FormatSegmentAbsolute(segments[i], nX, nY));
}
```

**Option B (Thorough):** Make the routing algorithm produce waypoints relative to `GetAbsoluteNazcaPosition()` from the start. This requires passing Nazca coordinates into the pathfinder.

### Fix 2: Retire or Rewrite NazcaExporter.cs (Priority: Medium)

Either:
- Delete `NazcaExporter.cs` (it's unused in the Avalonia UI — `SimpleNazcaExporter` is used)
- Or fix all three bugs: add Y-flip, negate rotation, apply NazcaOriginOffset

### Fix 3: Validate Coordinate Consistency in Tests (Priority: High)

The test gap is: no existing test checks that **segment 2+ of a multi-segment path starts where segment 1 ends in Nazca space**. Add integration tests (see below) that verify path continuity.

---

## Recommended Testing Strategy

### Level 1: Unit tests for coordinate math (already partially exists)

- `Mmi2x2PinAlignmentTests.cs` — pin positions at all rotations ✅
- `GdsWaveguideAlignmentTests.cs` — single-segment GC-to-GC ✅
- **MISSING**: Multi-segment path continuity test (see integration test below)

### Level 2: Export script structure tests

Check that exported Python scripts have correct `.put(x, y, angle)` coordinates by parsing the script and comparing expected vs actual positions.

### Level 3: End-to-end GDS binary tests (requires Python + Nazca)

Run the exported `.py` file and parse the resulting `.gds` binary to extract actual polygon/path coordinates. Compare against expected pin positions with ≤0.01 µm tolerance.

### Level 4: Python diagnostic script comparison

Use `scripts/compare_gds_coords.py` to compare a reference GDS generated by a known-correct Nazca script against the system-generated GDS.

---

## Integration Test: Demonstrating All Bugs

See `UnitTests/Integration/GdsCoordinateSystemBugTests.cs` — this test file:

1. **Proves Bug 1** by constructing a multi-segment path and verifying the coordinate jump between segment 1 and segment 2.
2. **Proves Bug 2** by checking the legacy `NazcaExporter` output for Y-flip and rotation sign.
3. **Proves Bug 3** by showing that `GetAbsoluteNazcaPosition()` ≠ `(editorX, -editorY)` for non-trivial components.

---

## Appendix: Key File Reference

| File | Purpose |
|------|---------|
| `Connect-A-Pic-Core/Components/Core/PhysicalPin.cs` | Pin Nazca coordinate calculation |
| `CAP.Avalonia/Services/SimpleNazcaExporter.cs` | Main (Avalonia) exporter — mostly correct |
| `Connect-A-Pic-Core/CodeExporter/NazcaExporter.cs` | Legacy exporter — broken |
| `Connect-A-Pic-Core/Routing/PathSegment.cs` | Routing segment model (editor space) |
| `UnitTests/Integration/Mmi2x2PinAlignmentTests.cs` | Pin alignment tests |
| `UnitTests/Integration/GdsWaveguideAlignmentTests.cs` | Single-segment alignment tests |
| `UnitTests/Integration/GdsCoordinateSystemBugTests.cs` | **NEW** multi-segment bug tests |
| `scripts/compare_gds_coords.py` | GDS binary comparison tool |
| `scripts/extract_gds_coords.py` | Extract coordinates from GDS to JSON |
