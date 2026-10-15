# Bend-loss model

How Lunima computes the insertion loss of a routed waveguide connection, and where the
default coefficients come from.

## Before (≤ v0.16)

```
loss_dB = α_prop · L_cm + N_90 · 0.05 dB
```

`N_90` was the number of equivalent 90° bends (Σ|θ|/90°). The bend radius did not appear
anywhere, so a 5 µm and a 71 µm bend cost the same 0.05 dB. Smooth polyline curves
(S-bend/Cobra styles, imported Euler bends) contained no arc segments and therefore had no
bend loss at all.

## Now

The loss of a path is split into three non-negative terms
(`CAP_Core.LightCalculation.BendLoss.ConnectionLossCalculator`):

| Term | Formula | Unit |
|---|---|---|
| Propagation | `L_prop = α_prop · length / 10⁴` | α in dB/cm, length in µm |
| Bend radiation | `L_bend = Σ L90(R) · |θ| / 90°` (+ optional flat excess per 90°) | dB |
| Junctions | `L_junction = Σ J_ref · (Δκ · R_jref)²` | dB, κ = 1/R in 1/µm |

with the radiation loss per 90° of turn

```
L90(R) = L_ref · exp(−(R − R_ref) / D)            for R ≥ R_cal
L90(R) = L90(R_cal) · exp(+2 · (R_cal − R) / D)   for R < R_cal   (steeper, conservative)
```

Equivalently the loss per unit arc length is `α_rad(R) = L90(R) / (π·R/2)` dB/µm — the
exponential radiation law of Marcatili [1] and Marcuse [2]. `R_cal` is the smallest radius the
coefficients were fitted at; below it the decay length is halved so the model rises twice as
steeply as the fit (the fit is not trusted outside its range).

**Junctions.** At a straight↔bend transition the bent mode is displaced towards the outside
of the curve by an amount proportional to the curvature; the overlap loss of a Gaussian-like
mode with a displaced copy grows with the square of the displacement [3]. A straight→bend
junction has Δκ = 1/R; an S-bend's direction reversal (bend→opposite bend) has Δκ = 2/R and
therefore costs 4× as much; two consecutive arcs with the same curvature have no junction.
Both ends of every connection meet a straight component port, so a path that starts or ends
in a bend has a junction there.

**Sampled curves (Euler / sine / Cobra).** A polyline of short chords is read as a smooth
curve: at each vertex the local radius is `min(chord₁, chord₂) / (2·sin(Δφ/2))` and the
radiation `L90(R_v) · Δφ / 90°` is integrated vertex by vertex, i.e. ∫α(κ(s)) ds over the
varying curvature. Curvature changes between consecutive vertices of one curve are summed
(they are small: that is what makes Euler bends low-loss [4]) but not counted as junctions in
the UI. Known approximation: a hard kink between two long straights is read as a large-radius
turn; the router never produces such kinks.

**Passivity.** Every term is ≥ 0 and the total is capped at 300 dB, so the amplitude
transmission `|t| = 10^(−loss/20)` is always in (0, 1] — the model can never add gain and
never produces NaN, even for zero or negative radii.

## Default coefficients per waveguide family

`BendLossPresets` (Connect-A-Pic-Core). Values are rounded, conservative fits to representative
published curves — not foundry data. A PDK can override every field (see below).

| Family | L_ref (dB/90°) @ R_ref | D (µm) | R_cal (µm) | J_ref (dB) @ R_jref | 90° corner loss | Source |
|---|---|---|---|---|---|---|
| Generic (no process) | 0.04 @ 10 µm | 2.17 | 5 | 0.005 @ 10 µm | 0.44 dB @ 5 µm · **0.050 @ 10 µm** · 0.003 @ 20 µm · 0.0002 @ 71 µm | calibrated to the former constant |
| SOI 220 nm strip | 0.056 @ 1 µm | 0.43 | 1 | 0.015 @ 1 µm | 0.086 @ 1 µm · 0.013 @ 2 µm · 0.0012 @ 5 µm | fit to [5] |
| SiN strip (~300 nm) | 0.05 @ 30 µm | 8.7 | 20 | 0.01 @ 30 µm | 0.07 @ 30 µm · 0.012 @ 50 µm · 0.002 @ 100 µm | [6], [7] |
| InP deep etch | 0.05 @ 10 µm | 4.3 | 10 | 0.02 @ 10 µm | 0.09 @ 10 µm · 0.015 @ 20 µm | [8] |
| InP shallow rib | 0.05 @ 150 µm | 21.7 | 100 | 0.02 @ 150 µm | 0.09 @ 150 µm · 0.015 @ 250 µm | [8] |

*Generic* reproduces the previous 0.05 dB per 90° at the 10 µm default radius (0.04 dB
radiation + 2 × 0.005 dB junctions), so designs without a process keep comparable numbers
while tighter bends now cost much more and gentle bends almost nothing.

*SOI* is fitted to the measured 0.086 dB/90° at R = 1 µm and 0.013 dB/90° at R = 2 µm of
Vlasov & McNab [5] (junctions included); above ~3 µm the radiation is negligible and the
remaining loss is the transition mismatch.

*SiN*, *InP* sets follow the qualitative behaviour reported for these platforms (moderate
contrast SiN: significant below ~50 µm, negligible at ≥ 100 µm; deep-etched InP usable down to
~10 µm, shallow ribs needing hundreds of µm) and are deliberately on the lossy side.

## Family resolution from the PDK

`BendLossModelResolver` (CAP-DataAccess) looks at the PDK process' **first optical
cross-section** (the same one that defines the default routing waveguide):

1. an explicit `bendLoss` block on that cross-section wins field by field;
2. otherwise the family is inferred from the process' `core` material: `Si3N4`/`SiN`/"nitride"
   → SiN, `InP`/`InGaAs(P)` → InP (shallow rib when `minRadiusUm ≥ 50` or the description says
   "shallow", else deep etch), `Si`/`Silicon` (or no core material and a ~220 nm core) → SOI;
3. no process → the connection uses *Generic*.

The model is stamped on every placed component (`Component.WaveguideBendLoss`) and inherited
by its connections; when the two endpoints carry different models the lossier one governs.
Metal traces have no bend loss.

### PDK JSON

```json
"xsections": [{
  "name": "strip", "kind": "Optical", "widthUm": 0.5, "minRadiusUm": 5,
  "bendLoss": {
    "family": "SoiStrip",
    "radiationLossDbPer90": 0.056, "radiationReferenceRadiusUm": 1.0,
    "radiationDecayLengthUm": 0.43, "calibratedMinRadiusUm": 1.0,
    "junctionLossDb": 0.015, "junctionReferenceRadiusUm": 1.0,
    "source": "foundry bend-loss measurement 2026-03"
  }
}]
```

All fields are optional; the block is omitted when absent (old PDKs round-trip unchanged).

## Where it is used

* routed connections (`WaveguideConnection.UpdateLossFromPath`) — coherent and incoherent
  simulation both use its `TransmissionCoefficient`;
* frozen paths inside groups (`FrozenWaveguidePath.TransmissionCoefficient`), with the same
  model resolution after a `.lun` reload;
* the logic-network assembler's group-internal wires;
* the crossing-vs-detour decision (`CrossingInserter.ComputePathLossDb`);
* the post-routing bend upsizer: each candidate radius is scored by its corner loss and a
  corner is only reshaped when it gains ≥ 1·10⁻⁴ dB;
* the Properties panel ("Insertion loss") and the MCP agent bridge (`list_connections` → `loss`).

Older `.lun` group files stored the constant `bendLossDbPer90Deg = 0.05`; it is read and
ignored (the radius model replaces it — honouring it would double-count).

## References

1. E. A. J. Marcatili, "Bends in optical dielectric guides," *Bell Syst. Tech. J.* 48, 2103–2132 (1969).
2. D. Marcuse, "Bending losses of the asymmetric slab waveguide," *Bell Syst. Tech. J.* 50, 2551–2563 (1971).
3. D. Marcuse, "Field deformation and loss caused by curvature of optical fibers," *J. Opt. Soc. Am.* 66, 311–320 (1976).
4. M. Cherchi, S. Ylinen, M. Harjanne, M. Kapulainen, T. Aalto, "Dramatic size reduction of waveguide bends on a micron-scale silicon photonic platform," *Opt. Express* 21, 17814–17823 (2013).
5. Y. Vlasov, S. McNab, "Losses in single-mode silicon-on-insulator strip waveguides and bends," *Opt. Express* 12, 1622–1631 (2004).
6. C. G. H. Roeloffzen et al., "Low-loss Si₃N₄ TriPleX optical waveguides: technology and applications overview," *IEEE J. Sel. Top. Quantum Electron.* 24(4), 4400321 (2018).
7. W. Bogaerts et al., "Silicon microring resonators," *Laser Photonics Rev.* 6, 47–73 (2012).
8. M. Smit et al., "An introduction to InP-based generic integration technology," *Semicond. Sci. Technol.* 29, 083001 (2014).
