# openEBL Readiness Report — what a Lunima MZI export needs to pass openEBL's automated checks

**Issue:** #1299 (rung 7 spike, decision support for PR #1197) · **Date:** 2026-10-02 · **Deadline assessed:** openEBL-2026-10 submissions close **2026-10-17**
**Verdict up front:** [Feasible: **yes, conditionally**](#4-recommendation) — every check is headless-reproducible today, the current export fails them in fully understood ways (16 submission-check errors + 1 functional-verification error, raw output in §2), and the gap list is closable in ~1–2 focused weeks. The non-technical preconditions (SiEPIC course alumni eligibility, fork + PR under a real username) are maintainer tasks.

## 1. What an openEBL submission requires

Source: [SiEPIC/openEBL-2026-10](https://github.com/SiEPIC/openEBL-2026-10) (mirror: jiesun83/openEBL-2026-10). Process: passive SOI 220 nm, single full etch, oxide cladding, fabricated by Applied Nanotools. Submission = fork of the repo, upload one binary file to `submissions/`, filename `openEBL_<username>.gds` (past-participant category; `EBeam_*`/`ELEC413_*`/`SiEPIC_Passives_*` for current courses), green GitHub Actions, then a PR. Two workflows gate the merge:

**a) Submission checks** — [`.github/workflows/run-submission-check.yml`](https://github.com/SiEPIC/openEBL-2026-10/blob/main/.github/workflows/run-submission-check.yml) runs [`run_submission_checks.py`](https://github.com/SiEPIC/openEBL-2026-10/blob/main/run_submission_checks.py) (`pip install klayout SiEPIC siepic_ebeam_pdk packaging`, headless). The script enforces, per file:

- exactly **1 top cell**;
- **die size**: bounding box of layers (1,0)+(4,0) ≤ **605 µm × 410 µm**;
- **black-box cells**: only 13 allow-listed GC/SWG cells (`ebeam_gc_te1550`, `GC_TE_1550_8degOxide_BB`, …, hardcoded in the script) may contain BB geometry (layer 998/0); these are IP-replaced at fabrication and **must not be renamed, moved, resized or re-origined**;
- **layer conformity**: every layer used in the design must exist in the EBeam PDK layer properties (`EBeam.lyp` from `siepic_ebeam_pdk`).

**b) Functional verification** — [`.github/workflows/run-verification.yml`](https://github.com/SiEPIC/openEBL-2026-10/blob/main/.github/workflows/run-verification.yml) runs [`run_verification.py`](https://github.com/SiEPIC/openEBL-2026-10/blob/main/run_verification.py), which calls `SiEPIC.verification.layout_check` (SiEPIC-Tools 0.5.31, [`verification.py`](https://github.com/SiEPIC/SiEPIC-Tools/blob/master/klayout_dot_config/python/SiEPIC/verification.py)). It requires the SiEPIC layout conventions:

- **components**: hierarchical (not flattened) cells wrapped in **DevRec (68/0)**; device-layer shapes outside any DevRec are errors; overlapping DevRecs are errors;
- **pins**: **PinRec (1/10)** shapes per optical pin; disconnected pins and pin-width mismatches are errors;
- **waveguides**: **Waveguide (1/99)** guiding shapes; Manhattan end segments, bend-radius and 2-point-path rules;
- **design-for-test** ([`DFT.xml`](https://github.com/SiEPIC/SiEPIC_EBeam_PDK/blob/master/klayout/EBeam/DFT.xml), shipped in `siepic_ebeam_pdk` 0.4.53): an **`opt_in_*` label on Text (10/0)** at every laser-injection grating coupler (format `opt_in_TE_1550_device_<name>`, unique, ≤10 µm from the GC tip, wavelength/polarization must match a DFT laser: 1550/1310, TE/TM); GCs at **0° orientation**, **127 µm pitch, vertical array**, ≥60 µm spacing, ≤1 detector GC above / ≤2 below the laser GC.

Layer table (repo README): Si 1/0 (fabricated), Floorplan 99/0, Text 10/0, DevRec 68/0, PinRec 1/10, Waveguide 1/99, SEM 200/0.

## 2. What we ran, and the exact output

Method (all headless, Windows runner, Python 3.14.3 venv; `klayout==0.30.12`, `SiEPIC==0.5.31`, `siepic_ebeam_pdk==0.4.53`, `nazca==0.6.1` — the same pip set as openEBL's CI):

1. Loaded the shipped `examples/Mach-Zehnder Interferometer.lun` (7 components, 7 connections) through the app's real load path.
2. Exported it with `SimpleNazcaExporter` (the app's "Whole Layout GDS" path) and ran the generated script under real nazca → 29 KB GDS, single top cell `ConnectAPIC_Design`, cells: demofab `io`/`pd_dp_50`/`eopm_dc_500`/`mmi1x2_sh`/`mmi2x2_dp` etc.
3. Ran openEBL's **own** `run_submission_checks.py` and `run_verification.py` verbatim against that GDS, plus a klayout-only port of the submission checks that is pinned in CI as the `Category=Slow` test `UnitTests/Export/OpenEbl/OpenEblMziReadinessTests.cs` (its output was byte-identical to the genuine script on the checks both perform).

**`run_submission_checks.py submissions/openEBL_lunima_mzi.gds` → 16 errors (last line = error count):**

```
Running submission checks for file submissions/openEBL_lunima_mzi.gds
Error: Bounding box of selected layers (620.000 µm x 51.725 µm) exceeds allowed size 605.000 µm x 410.000 µm
Performing Black Box cell replacement check
 - Number of black box cells to be replaced: 0
 - Number of unreplaced BB cells: 0
Error: the layer 1600/0 in the design is not defined in the PDK.
Error: the layer 501/0 in the design is not defined in the PDK.
Error: the layer 501/1 in the design is not defined in the PDK.
Error: the layer 10/20 in the design is not defined in the PDK.
Error: the layer 1004/0 in the design is not defined in the PDK.
Error: the layer 502/0 in the design is not defined in the PDK.
Error: the layer 21/0 in the design is not defined in the PDK.
Error: the layer 1003/0 in the design is not defined in the PDK.
Error: the layer 10/10 in the design is not defined in the PDK.
Error: the layer 3/10 in the design is not defined in the PDK.
Error: the layer 4/10 in the design is not defined in the PDK.
Error: the layer 3/20 in the design is not defined in the PDK.
Error: the layer 1/20 in the design is not defined in the PDK.
Error: the layer 2/10 in the design is not defined in the PDK.
Error: the layer 1111/0 in the design is not defined in the PDK.
16
```

Reading: the single-top-cell and black-box rules pass trivially; the die-size rule fails because demofab geometry on 1/0 spans 620 µm (the MZI is ~1.3 mm wide overall); the 15 layer errors are the demofab component layers plus nazca's default interconnect layer 1111/0 and Lunima's `bb_body` frame layer 1003/0 — none exist in the EBeam layer map.

**`run_verification.py submissions/openEBL_lunima_mzi.gds` → 1 error.** The script prints `Unknown error occurred` because `layout_check` raises inside `find_components` — the export contains **no DevRec (68/0) shapes**, so SiEPIC-Tools finds zero components and aborts before any further rule runs:

```
Running SiEPIC-Tools automated verification for file submissions/openEBL_lunima_mzi.gds
Top cell: ConnectAPIC_Design
Unknown error occurred
1
# underlying traceback (reproduced with the same packages):
# SiEPIC/extend.py, line 1212, in find_components
# Exception: SiEPIC.extend.find_components: No component found for cell_selected=None
```

This is a content gap, not a headless limitation: the check itself runs headlessly (openEBL's CI does exactly this), and the waveguide/connectivity/DFT rules were never reached.

**Update 2026-10-02 (#1321, gap #2):** the same vendored `run_verification.py` port was run against the **EBeam MZI** export (the gap #1 design, with the #1309/#1320 layers/DFT markers). With one DevRec (68, 0) per component cell the run **completes** — `layout_check` reports **23 layout errors** in two categories, both the gap #4 surface and deliberately left for the next slice:

```
Running SiEPIC-Tools automated verification (Lunima headless port) for file ebeam_mzi_verify.gds
Top cell: ConnectAPIC_Design
Design for Test rules from PDK: .../siepic_ebeam_pdk/DFT.xml
23 layout errors detected.
category Disconnected pin: 8
category Shapes outside component: 15
23
```

Reading: `find_components` succeeds (4 components), DFT rules load and pass, and the entire remainder is the missing SiEPIC route conventions — the 4 routed connections export as 15 top-cell Si polygons ("Shapes outside component") instead of Waveguide (1, 99) guiding shapes inside the DevRec hierarchy, so none of the 8 optical pins (2 × 4 components) sees a connected waveguide ("Disconnected pin"). The durable pin is the CI test `UnitTests/Export/OpenEbl/OpenEblEBeamMziVerificationTests.cs`.

**Update 2026-10-03 (#1336, gap #4):** the same vendored `run_verification.py` port now reports **0 layout errors** on the EBeam MZI export — every routed connection exports as its own SiEPIC-conformant `Waveguide_<n>` cell (Si 1/0 inside the cell + Waveguide (1/99) guide-outline polygon + DevRec (68/0) + PinRec (1/10) pins snapped onto the real foundry pin centres; see gap #4 of the table below for the mechanism). Census before → after: "Shapes outside component" 15 → 0, "Disconnected pin" 8 → 0.

```
Running SiEPIC-Tools automated verification (Lunima headless port) for file ebeam_mzi_verify.gds
Top cell: ConnectAPIC_Design
Design for Test rules from PDK: .../siepic_ebeam_pdk/DFT.xml
category Disconnected pin: 0
category Shapes outside component: 0
0
```

## 3. Gap list

| # | Gap | Effort |
|---|-----|--------|
| 1 | **EBeam design to export**: the shipped MZI is a Demo-PDK teaching circuit; an EBeam MZI variant must be built from the bundled `CAP-DataAccess/PDKs/siepic-ebeam-pdk.json` (44 components incl. `ebeam_gc_te1550`, MMIs) — GC placement must follow the DFT array rules (127 µm pitch, vertical, 0°) | **M** — **closed** (#1310): `examples/EBeam Mach-Zehnder Interferometer.lun` ships exactly this (2× `ebeam_gc_te1550` at 0°, 127 µm vertical pitch, 2× `ebeam_y_1550`, 307.373 × 147.228 µm on the checked layers); together with gap #3's EBeam layer mapping (#1309) its export passes the submission-check port with **0 errors**, pinned by `OpenEblEBeamMziReadinessTests` |
| 2 | **DevRec (68/0) + component hierarchy** — **verification gate unblocked** (#1321, measured 2026-10-02): a vendored headless port of openEBL's `run_verification.py` (`layout_check` with the same top-cell pick + EBeam technology attach, plus a per-category census from the `.lyrdb`) now runs in CI as `OpenEblEBeamMziVerificationTests` against the real EBeam MZI export. **The run completes — no `Unknown error occurred`**: `find_components` sees one DevRec (68, 0) per component cell. The klayout upgrade pass brings the foundry cells' own DevRec (verified: `ebeam_gc_te1550`/`ebeam_y_1550` carry 3 each); a new ensure-pass in `SiepicCellUpgradeWriter` (`addDevRec`, EBeam-only) draws the cell-footprint DevRec on any cell that kept its stub box (PDK/klayout missing at export time) and never duplicates a foundry one. Remaining errors, all gap #4 and deliberately unfixed: **23 = 15 "Shapes outside component"** (routes flatten to top-cell Si polygons on 1/0) **+ 8 "Disconnected pin"** (2 optical pins × 4 components; routes carry no PinRec/Waveguide (1, 99) conventions). Non-EBeam exports stay byte-identical (`OpenEblEBeamDevRecTests`). Residual limitation: SiEPIC parametric-straight stubs (per-length cell names) are outside the ensure-pass cell list — the EBeam MZI has none | ~~M~~ **done** (gate unblocked; remainder is gap #4) |
| 3 | **Layer mapping** — **CLOSED (#1309, measured 2026-10-02)**: EBeam-only designs (every component from `siepic-ebeam-pdk.json`) now export interconnect on **Si 1/0** at the PDK's 0.5 µm strip width (via the process stamps placed on the pins; `SiepicEBeamExportProfile`) and drop the demofab `bb_body` 1003/0 frame; pin labels stay on 1/10 (PinRec). Measured: a two-`ebeam_gc_te1550` + waveguide design passes the ported submission check with **0 errors** (`OpenEblEBeamSubmissionCheckTests`), and the genuine `run_submission_checks.py` reports **0** on the same GDS. (The 501/* layers in the §2 listing are demofab pin layers — they vanish with the demo PDK, i.e. gap 1's EBeam re-layout, not this gap.) | ~~S~~ **done** |
| 4 | **SiEPIC pin/waveguide conventions** — **CLOSED (#1336, measured 2026-10-03)**: EBeam-only exports now emit every routed optical connection as its own SiEPIC-conformant `Waveguide_<n>` cell placed in the top cell (`SiepicWaveguideCellWriter`): the routed Si (1/0) polygons move inside the cell, and a klayout post-pass (`_lunima_add_waveguide_spines`, after the foundry-cell upgrade) adds the structure of the EBeam `Waveguide` PCell — a Waveguide (1/99) guide along the route centreline stored as the path's outline polygon (a >2-point PATH trips the "Waveguide: Path" rule), a DevRec (68/0) guide-outline polygon (a bbox would falsely overlap waveguides inside a long detour route's bbox) and, at each end, a PinRec (1/10) optical pin that is the exact REVERSED copy of the partner component's pin path (identical integer centre, 180°-opposite direction — the two conditions `identify_nets` nets a connection on; endpoints are snapped to the real foundry pin centres read back from the upgraded GDS, so the F2 export rounding cannot break centre equality). Measured on the EBeam MZI (`OpenEblEBeamMziVerificationTests`, before → after): **"Shapes outside component" 15 → 0**, **"Disconnected pin" 8 → 0**, **total 23 → 0 layout errors**; the submission check stays at **0 errors** (`OpenEblEBeamMziReadinessTests`), and non-EBeam exports stay byte-identical (`OpenEblEBeamWaveguideCellTests`). Residual limitation: only routed segment exports are wrapped — routeless p2p-fallback connections, styled point-to-point primitives and frozen/group paths would still flatten into the top cell (the EBeam MZI has none) | ~~M~~ **done** |
| 5 | **`opt_in_*` measurement labels on Text (10/0)** at each injection GC (unique, ≤10 µm from tip) | **S** — **closed** (#1320, measured 2026-10-02): EBeam-only exports now emit one `opt_in_TE_1550_device_<design>` text on (10,0) per laser-injection GC (the light source with its laser on; detector couplers with the laser off get none), anchored on the GC's cell origin (distance 0 ≤ 10 µm), design name sanitized to `[A-Za-z0-9_]`, unique suffixes for multiple inputs (`NazcaOpenEblDftWriter`). Measured: the EBeam MZI census reads `opt_in labels (10/0): 1`, submission-check error count stays **0** (`OpenEblEBeamMziReadinessTests`) |
| 6 | **Floorplan (99/0) box + die fit**: current MZI is 620 µm wide on the measured layers vs the 605 µm limit; an EBeam re-layout on the 127 µm GC grid fixes this by construction | **S** — **die fit closed** (#1310): the EBeam MZI fits 605 × 410 µm by construction (re-measured 2026-10-02: 317.623 × 147.228 µm on layers (1,0)+(4,0)). **Floorplan box closed** (#1320, measured 2026-10-02): EBeam-only exports now emit one rectangle on (99,0) of exactly 605 × 410 µm whose lower-left corner is the design bbox lower-left, so the design sits inside it; the EBeam MZI census reads `Floorplan (99/0) shapes: 1`, error count stays **0** |
| 7 | **Black-box GC fidelity**: the existing klayout post-pass (`SiepicCellUpgradeWriter`) already swaps stubs for real foundry cells, but openEBL requires the PDK's unmodified cell names/origins — the upgrade keeps stub names/labels, so an EBeam tapeout profile must place the real GC cells as-is | **M** |
| 8 | **Tapeout export profile**: top-cell/filename convention (`openEBL_<user>.gds`), single top cell (already holds: `ConnectAPIC_Design`) | **S** |

## 4. Recommendation

**Feasible for 2026-10-17: yes, conditionally.** Every automated check is reproducible headlessly today (proven above, and the ported submission check now runs in CI), the failures are conventional gaps rather than architectural ones, and the sum of the gaps is ~2 M + 3 M + 3 S ≈ 1–2 focused weeks against a 15-day runway — with the EBeam-MZI example + DevRec/layer export slice (gaps 1–3) as the critical path. Conditions: (a) the slice is green against both openEBL scripts by ~2026-10-10, else defer to the next run; (b) the maintainer confirms openEBL eligibility (the `openEBL_<username>` category is for **past SiEPIC course/workshop participants**) and performs the fork + PR by hand — nothing may be submitted from CI/agents.

**Not feasible** without that focused slice: today's MZI export fails both gates (16 + 1 errors) for structural reasons (demofab cells, no DevRec, no labels, too wide).

---
*Artifacts of this spike (export script, GDS, checker, raw outputs) were produced locally under `artifacts/openebl-issue-1299/` (gitignored); the durable pin is the CI test `UnitTests/Export/OpenEbl/OpenEblMziReadinessTests.cs`. Nothing was submitted to openEBL.*
