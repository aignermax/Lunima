# RAM 4×4 feasibility spike (#1337)

Rung-5 decision support: what does a photonic 4-word × 4-bit register file cost **before**
anyone builds it as an example? Measured on a programmatically generated design — the
shipped RAM 2x2 pattern (#1142/#1147) scaled up: 2→4 address decode (`SEL = NOT(NAND(s1,
s0))`), 16 load-enabled register bits (`H = NAND(R, EN)`, `LE = NAND(D, IW)`,
`REG = NAND(H, LE)`), and a 4:1 NAND read-mux tree per bit (`OR(a,b) = NAND(NOT a, NOT b)`
at the sum levels), with copy-cascade fan-outs (one waveguide per driven signal). The
builder lives in the test only — `UnitTests/Integration/RamScale/RamScaleDesignBuilder.cs`;
every number re-takes via the `[Trait("Category", "Slow")]` test `Ram4x4FeasibilityTests`
(route bound: default 60 s so CI stays within its job cap; the numbers below were taken with
`CAP_RAM_SPIKE_ROUTE_TIMEOUT_S=900`, i.e. 15 min). Measured
2026-10-03 on the dev box, router at `dev-ki` HEAD.

## Numbers

| Variant | Gates | Wires | Chip | Assemble | Behaviour (real network) | Full route | At cancel |
|---|---|---|---|---|---|---|---|
| 2 words × 4 bit | 71 | 97 | 19.8 × 3.8 mm | 0.3 s | ✅ store/read/hold every word, 16-value sweep, isolation | **> 15 min — timeout.** Initial pass 362 s (all 97 wires), then ordering cascade still running at cancel (538 s / 2 attempts) | 0 unrouted, 24 blocked |
| 4 words × 4 bit | 183 | 260 | 23.4 × 7.0 mm | 0.5 s | ✅ store/read/hold every word, 16-value sweep, isolation | **> 15 min — timeout.** The *initial pass alone* did not finish: 225/260 wires attempted | 35 unrouted, 118 blocked |

Reference points already on record: shipped RAM 2x2 = 37 gates / 49 wires (18 blocked on
the baked cache); 2-bit Register full re-route ≈ 90 s (#1302/#1303); the 344-gate 4-bit
adder is the largest shipped logic example (90 blocked wires).

## Router initial-pass re-measure (#1342)

Same benchmark (`RamScaleDesignBuilder.Build(2, 4)`), re-taken after the router
initial-pass optimization (#1342: a reachability flood that skips provably-unreachable
searches, one continuous search instead of the quick+extended re-run, memoized proximity
cost, lock-free pin-zone lookups). An endpoint-corridor search window was part of the
first measurement but was reverted in the #1348 review: it changed which valid path A*
finds, which broke the pinned GDS round-trip topology — the kept optimizations leave the
found routes unchanged (pinned by the nazca GDS round-trip suite). Both rows below ran on
one machine with the same 600 s bound:

| Router | Route bound | Initial pass | Blocked at cancel | Unrouted |
|---|---|---|---|---|
| dev-ki (before) | 600 s | 409.3 s | 36 (1 cascade attempt) | 0 |
| #1342 without window (after) | 600 s | 124.8 s | 20 (5 cascade attempts) | 0 |

**Initial pass 409.3 s → 124.8 s = 3.3× faster with unchanged routes.** The full route
still does not converge within 10 min — the ordering cascade is now the dominant share
(~475 s of the 600 s run) and is the next router target, not the initial pass.

## Ordering-cascade parallel re-measure (#1360)

Same benchmark, same dev box, after the cascade parallelization: the orderings are
independent full re-routes from the same component-only grid state, so they are evaluated
speculatively in parallel on isolated router clones, and the sequential selection (first
clean or all-endpoint-blocked attempt, else the lowest failed count with the
`MaxNonImprovingOrderingAttempts` early stop) is replayed over their outcomes. The kept
routes are bit-identical to the sequential cascade — pinned by
`OrderingCascadeOptimizationTests` (same kept ordering, same segments, same attempt
counters, parallel vs. sequential).

Profile of one cascade attempt before the change (per-wire timers, 240 s bound): one attempt
≈ 118 s ≈ a full 97-wire re-route, and ~100% of the wire time is A* search (the #1342
reachability flood accounts for ~20 s of each pass; direct-styled probing, pin corridors,
smoothing and the Manhattan fallback together stay below 1 s). Of the 118 s, ~110 s go to
the 56 wires that FAIL in that attempt (35 contention, 21 sibling-crossing, 0
endpoint-blocked) — they burn their full search budgets again under every ordering — while
the 41 successful wires cost ~8 s combined. So skipping successful wires (rip-up-only
retries) or component-only-unreachable wires (none here) cannot halve the cascade: the cost
is the failing wires' searches, repeated once per ordering, sequentially. The orderings run
in parallel instead.

| Router | Route bound | Initial pass | Ordering cascade | Full route | Blocked |
|---|---|---|---|---|---|
| sequential (before) | 600 s | 122.3 s | 477.7 s — cancelled inside retry 5 | timeout at 600 s | 21 (half-routed snapshot at cancel) |
| parallel (after) | 600 s | 121.0 s | 216.5 s — all 6 orderings evaluated | **347.6 s — finished** | 26 (final) |
| sequential (before), run to completion | 1200 s | 120.5 s | 695.9 s / 6 attempts | 826.5 s — finished | 26 (final) |

Like-for-like once the sequential cascade is allowed to finish: **cascade 695.9 s → 216.5 s
= 3.2× faster, full route 826.5 s → 347.6 s = 2.4×, final blocked count identical (26)**.
The 21 vs. 26 across the two 600 s rows is not a regression: the before-run was cancelled
mid-attempt, so its count is a half-routed mix of two attempts, not a cascade result — the
finished runs agree exactly. The contention-repair pass also gets to run now (it was always
cancelled before it started) and accepted one wire inside its 10 s budget in both finished
runs. The 2×4 RAM now routes end-to-end in ~6 min instead of not converging within
10-15 min — still with 26 blocked wires, so the recommendation below stands.

## Slope

- **Gates:** 37 (2×2) → 71 (2×4) → 183 (4×4) — roughly ×1.9 per dimension doubling.
  Gate count is **not** the blocker: the 344-gate adder already ships.
- **Assembly:** 0.3 s → 0.5 s — linear, trivial; the logic layer scales fine.
- **Behaviour:** correct at both sizes through the real assembler/evaluator path.
- **Route:** 49 wires ships only pre-baked (18 blocked); 97 wires > 15 min; 260 wires ≫
  15 min with the initial pass incomplete. Per-wire cost is superlinear (obstacle density
  + ordering cascade). **The router is the wall, exactly as suspected.**

Caveat: the layout is auto-generated (column-staged, shipped pitch); a hand-tuned floorplan
would shift the constants but not the conclusion — the shipped, hand-laid-out RAM 2x2
already leaves 18 wires blocked, and the 2×4 timeout reproduces that wall at half scale.

## Recommendation

**Needs a router change first — do not build the 4×4 example yet.** Not "ship as example"
(open would route > 15 min), not "ship pre-routed" (the cache cannot be produced within
any interactive bound, and one edit-triggered re-route hangs the canvas again; RAM 2x2's
cache already ships 18 blocked wires). The logic design itself is proven: assembly is
sub-second and behaviour is correct at both sizes, so the moment routing improves (#725:
direct-first / anytime-A*) or a hierarchical chiplet-of-RAM-cells layout lands (route one
2×2 cell once, instance it ×4), the 4×4 RAM becomes buildable — re-take this spike's Slow
test to verify.
