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
