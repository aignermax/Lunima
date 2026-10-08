# Crossing-aware routing (release blocker for 0.15.0)

## Problem (measured, not assumed)

The large logic examples ship with blocked (red-dashed) wires: ALU + RAM 167,
4-bit adder 90, RAM 4x4 77, RAM 2x4 21, RAM 2x2 18, PC 12, Counter 4, Full Adder 2.

Probe results on Counter / PC / Full Adder / RAM 2x2 (36 blocked wires):

- Every failure is `Contention`; no wire is sealed by a component body. On a grid
  with only components, every blocked wire has a direct path.
- The direct paths cross 1–7 other wires: the netlists are **not planar**
  (feedback in sequential logic, bus fan-out). An avoid-only router cannot connect them.
- The existing crossing pass (Settings → Routing → Crossings) runs (2–15 s) but
  places **0** crossings, even on a full re-route: it only handles a direct path that
  crosses exactly one wire at an axis-aligned right angle with straight run around it.
- Only ~30 % of a naive direct path's intersections are crossable (the rest sit in
  bends, are not 90°, or lack straight run) — so "route, then drop crossings where it
  hits" rescues 7 of 36 wires. The router itself must choose where to cross.
- The examples run on Demo SOI / Playground; only the SiEPIC PDK has a crossing, and
  the crossing factory hard-codes `ebeam_crossing4`.
- The logic layer treats only 2-pin components as transparent: a 4-port crossing
  between two gates would end the trace (gate input silently becomes a network input).

## Design

1. **Demo PDK crossing** — "Waveguide Crossing" (`demo_crossing`), 10 × 10 µm, pins
   mirroring `ebeam_crossing4` (W/E/N/S by angle), S-matrix from the SiEPIC crossing
   (through 0.978, crosstalk 0.02, reflection 0.01); Nazca export via `rawCode`
   (two crossing `demo.shallow` straights). Crossing selection becomes
   process-aware (active process' crossing first, `ebeam_crossing4` fallback).
2. **Logic layer** — a 4-port crossing is pass-through straight on: the far pin is the
   one with the opposite angle (`LogicNetworkBuilder`, wire loss, wire delay).
3. **A\* crossing step** — when the next cell is blocked only by another wire's
   straight-segment footprint perpendicular to the move, the search may jump across it
   (same direction, fixed span), provided it arrived straight and the crossing point
   keeps the straight run the component needs on both wires. Cost = span + a penalty
   equal to the crossing's loss in length-equivalent.
4. **Insertion** — each jump becomes a placed crossing: the crossed wire is split at the
   point (the Cut-tool primitive), the new wire is routed as a chain of legs through
   the free crossing ports; any leg failure rolls the whole wire back.
5. **Re-bake** every example with crossings on; truth tables, DRC and layout sweeps
   must stay green; look at the canvas.

## Verification

- Unit: crossing template selection, pass-through rule, jump legality (perpendicular,
  straight run, no bend, single owner), chain insertion + rollback.
- Integration: the probe numbers above as regression targets (blocked → 0 or a pinned
  small residual), logic example truth tables unchanged, `ExampleLoadRoutingTests`
  counts updated, GDS round-trip tests green.
