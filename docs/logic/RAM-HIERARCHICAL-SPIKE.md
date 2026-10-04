# Hierarchical RAM spike (#1366)

Rung-5 follow-up to the flat feasibility spike (#1337, `docs/logic/RAM-4x4-FEASIBILITY.md`):
its recommendation named exactly this experiment — *"a hierarchical chiplet-of-RAM-cells
layout (route one cell once, instance it)"* — as one of the two ways the 4×4 RAM becomes
buildable. This spike measures it: the word slice of the RAM (the enable chain, the
select fan-out, and per bit the hold/load arms, register, read tap and read-mux arm) is
laid out as ONE standalone 4-bit word cell, routed once, and frozen into an instancing
template (group with frozen internal paths + external pins SEL, LOAD, D0–D3 in, M0–M3
out). The full RAM is then cell instances plus a top level of only the address stage,
the LOAD/data distribution and the read-mux combine — the inter-cell wires are the whole
top-level route.

Everything lives test-side in `UnitTests/Integration/RamScale/` (no product code):
`RamWordCellBuilder` (cell emitter), `RamWordCellTemplate`/`RamWordCellInstance`
(template extraction from the routed canvas + instancing), `RamHierarchicalDesignBuilder`
(instances + top level), `RamHierarchicalNetwork` (logic-assembly adapter, see below) and
the `[Trait("Category", "Slow")]` `RamHierarchicalFeasibilityTests`, which reuses the
flat spike's route measurement and behavioural assertions (store/read/hold every word,
16-value sweep, isolation) through the real assembler/evaluator. Measured 2026-10-04 on
the dev box, router at `dev-ki` HEAD (post-#1342/#1360). Bounds: cell route
`CAP_RAM_SPIKE_CELL_TIMEOUT_S` (default 300 s — the cell MUST route fully, it is frozen
into the template), top-level route `CAP_RAM_SPIKE_ROUTE_TIMEOUT_S` (default 60 s; the
4×4 row below re-taken with 900, like the flat doc).

## Numbers

The word cell: **33 gates, 44 intra-cell wires, routes once in 119.1 s** (0 unrouted,
**17 blocked** — the blocked wires freeze into the template and replicate per instance;
they are routing-geometry fallbacks, not logic failures — behaviour is asserted through
the real assembled network and is exact).

| Variant | Gates | Top-level wires (frozen per instance) | Assemble | Behaviour (real network) | Top-level route | At end |
|---|---|---|---|---|---|---|
| 2 words × 4 bit hierarchical | 71 = 2×33 + 5 | **9** (+ 2×44) | 0.2 s | ✅ store/read/hold, sweep, isolation | **42.3 s — finished** (within the 60 s bound) | 0 unrouted, 3 blocked |
| 4 words × 4 bit hierarchical | 183 = 4×33 + 51 | **84** (+ 4×44) | 0.6 s | ✅ store/read/hold, sweep, isolation | **546.1 s — finished** (900 s bound; 60 s bound times out at 50/84 unrouted) | 0 unrouted, 42 blocked |

Against the flat measurements (same machine, same router):

| Variant | Flat wires → hier top wires | Flat route → hier route (+ one-time cell) | Flat blocked → hier blocked |
|---|---|---|---|
| 2 words × 4 bit | 97 → **9** | 347.6 s → **42.3 s** (+ 119.1 s once) | 26 → **3** |
| 4 words × 4 bit | 260 → **84** | **> 15 min DNF** (initial pass incomplete) → **546.1 s finished** (+ 119.5 s once) | 118 at cancel → **42** final |

The gate census is identical flat vs. hierarchical (71 / 183) — the instanced topology
matches the flat design gate for gate, which the behaviour assertions confirm.

## What the numbers say

- **The hypothesis holds.** Moving the intra-word wiring into a frozen cell cuts the
  routed wire count ~10× (97→9) at 2×4 and ~3× (260→84) at 4×4, and the route time drops
  from 6 min to 42 s at 2×4 (8×) and from "does not finish in 15 min" to a finished
  9-minute route at 4×4. The 4×4 RAM — unroutable flat — **routes end-to-end
  hierarchically** on the current router.
- **The cell route amortizes.** 119 s once, then every instance is free: 4×4 pays
  119.5 + 546.1 ≈ 11 min total vs. the flat design's > 15 min unfinished; at 8 words the
  flat cost explodes while the hierarchical cost grows only in the inter-cell wires.
- **Blocked count improves but does not vanish.** 26→3 at 2×4; the 4×4 keeps 42 blocked
  (plus the 17 frozen per cell). The auto-generated cell floorplan leaves 17/44
  intra-cell wires blocked — a hand-tuned cell layout is the obvious next lever, since
  every improvement replicates across all instances for free.
- **60 s interactivity bound:** the hierarchical 2×4 routes within it (42 s); the
  hierarchical 4×4 does not (~9 min). Pre-routed examples remain the ship vehicle, but
  unlike the flat 4×4 the cache can now actually be produced.

## Product gap found (ship blocker for real hierarchical designs)

The spike assembles behaviour through a **test-side adapter** because three product
behaviours are missing for nested designs — this is the defect the issue asked to look
for, and it blocks any real (non-spike) hierarchical example:

1. **Loader drops nested truth-table assignments.** `TruthTablePinAssignment` is
   restored only on TOP-LEVEL groups; a gate nested inside a cell instance loads with a
   null assignment and is invisible to the logic pipeline.
2. **The assembler does not recurse.** `LogicNetworkAssembler` gates only components
   with a restored assignment at the level it is handed; nested gate groups are never
   seen.
3. **Frozen group-internal paths are not wires.** A cell's `InternalPaths` carry the
   intra-cell connectivity physically but never become connections, so even a
   recursed assembler would find the gates unwired.

The test-side `RamHierarchicalNetwork` emulates exactly these three changes
(re-attaches the persisted assignments from the document, flattens nested gate groups,
wraps frozen paths as virtual connections) and the network then behaves bit-identically
to the flat RAM — so the product change is small and well-scoped. Loading, nested
binding (#1060/#1065), instancing and routing need nothing: those all work today, as
the green route/behaviour runs prove.

## Recommendation

**Build the 4×4 RAM hierarchically — the router wall is gone at this scale.** The flat
spike's precondition ("the moment … a hierarchical chiplet-of-RAM-cells layout lands,
the 4×4 RAM becomes buildable") is met: 84 inter-cell wires route in ~9 min with
42 blocked, behaviour exact, gate count unchanged. Suggested follow-ups:

1. **Product: logic pipeline over nested groups** (the three gaps above: loader restores
   nested `TruthTablePinAssignment`s, assembler recurses, frozen internal paths count as
   wires). Small, bounded, and the only thing standing between this spike and a
   shippable hierarchical example. Follow-up issue text below.
2. **Hand-tune the word-cell floorplan**: 17/44 blocked intra-cell wires is the worst
   ratio in the design and replicates per instance; a better cell is a multiplier.
3. **Example: hierarchical RAM 4x4** once (1) lands — pre-routed cache like RAM 2x2,
   producible now.

### Follow-up issue (ready to file)

> **Logic assembly over nested groups: restore nested truth-table assignments, recurse
> the assembler, count frozen internal paths as wires**
>
> The hierarchical-RAM spike (#1366) builds a RAM from a routed-once word-cell group
> (frozen internal paths, external pins) instanced 2×/4×: loads, binds (#1060/#1065) and
> routes correctly, but logic assembly only works through a test-side adapter
> (`UnitTests/Integration/RamScale/RamHierarchicalNetwork.cs`) because (a) the loader
> restores `TruthTablePinAssignment` solely on top-level groups, (b)
> `LogicNetworkAssembler` never recurses into groups, and (c) a group's frozen
> `InternalPaths` never become connections. Emulate the adapter in product code
> (test: hierarchical RAM assembles and behaves identically to the flat RAM WITHOUT the
> adapter — the spike's `RamHierarchicalFeasibilityTests` then drops
> `RamHierarchicalNetwork`). Numbers and context: `docs/logic/RAM-HIERARCHICAL-SPIKE.md`.
