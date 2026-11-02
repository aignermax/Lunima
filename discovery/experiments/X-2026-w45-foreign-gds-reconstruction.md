# X-2026-w45-foreign-gds-reconstruction: How much circuit structure does Lunima's current GDS import recover from third-party layouts without port labels?
- status: proposed
- method: benchmark
- autonomy: autonomous
- assumption: On real third-party layouts with no port labels or pin markers, Lunima's existing import recovers most device-to-device connections without manual pin work. "Existing import" means explode mode, edge-heuristic pins, and abutment and drawn-route matching.
- hypotheses: H3
- budget: 0 USD, ≤ 1 agent working day. Public files only. No import feature is built or tuned for this run.

## Why this, why now (context, not part of the pre-registration)
H3's riskiest assumption is reconstruction quality on real foreign layouts, and nobody has
measured it. The three fixtures in `Tools/gds-test-data/` are a gdsfactory MZI with port labels
added on purpose and two of Lunima's own exports (E-2026-w45-05). Competitors and libraries recover
ports from labels or markers (E-2026-w45-02, E-2026-w45-04). The open question is how Lunima does
when those are missing.

This is an **E0 measurement of the existing implementation**. It cannot show demand, and it cannot
move H3 to `supported`. It answers two questions:
- Is the current import good enough to try on a layout an interviewee brings and is free to
  share (X-2026-w44-designer-interviews, #1595 / #1596)?
- How big is the gap if the valuable case turns out to be flat layouts (E-2026-w45-06)?

From reading `CAP-DataAccess/Import/Gds/GdsHierarchyImporter.cs`: explode mode turns the top
cell's *direct children* into instances, and pin detection runs per cell. Nothing splits one flat
cell into devices.

## Pre-registration
- corpus: 8 layouts produced outside Lunima.
  - Source: SiEPIC openEBL student submissions (the programme of E-2026-w45-12). The licence
    (MIT) and the layer table with pin-recognition (PinRec 1/10) and device-recognition
    (DevRec 68/0) layers were checked by the strategist on the openEBL-2024-02 repository page on
    2026-11-02. The licence of whichever run's repository is used gets re-checked at selection
    time.
  - Selection rule, applied before any Lunima import runs: start with the `submissions` folder of
    `SiEPIC/openEBL-2024-02` (fabrication completed 2024-03-12, per E-2026-w45-12). In file-name
    order, take the first 8 files whose reference netlist (below) has ≥ 3 devices and ≥ 2
    device-to-device connections, from ≥ 2 different authors. If that folder yields fewer than 8,
    continue with the next openEBL run's repository in date order.
  - Excluded: anything Lunima exported, and the fixtures in `Tools/gds-test-data/`.
- reference: the device-to-device connections of the *unmodified* file, extracted **without
  Lunima's importer**.
  - Preferred: SiEPIC-Tools' netlist export, if it runs headless.
  - Fallback: a small gdstk script that joins PinRec pins coinciding within 0.01 µm and collapses
    waveguide cells into the device-to-device connections they carry.
  - Every cell instance other than a waveguide cell counts as a device.
- variants:
  - **V1, unlabelled with hierarchy kept:** keep only the layers the openEBL README lists as
    fabricated. Remove PinRec, DevRec, every other non-fabricated layer and all TEXT elements.
  - **V2, flat and unlabelled:** V1 flattened into a single cell.
- run: headless `import_gds` with `mode=explode`, the waveguide layer role taken from the openEBL
  README, `auto_connect=false` and `reroute=false`. Import target: the cell that directly
  instantiates the reference's devices (V1), or the single cell (V2).
- metrics, per file:
  - **R (recall):** recovered reference connections / reference connections. A connection counts
    as recovered when Lunima connects the same two devices, directly or through a chain of
    imported route pieces, at pins within 1 µm of the reference pin positions.
  - **P (precision):** Lunima device-to-device connections that match a reference connection /
    all Lunima device-to-device connections.
  - **D (device recall):** reference devices that come out as separate components / reference
    devices.
  - the number of devices left with no detected pin.
- success: on V1, median R ≥ 0.80 and median P ≥ 0.90 over the 8 files, with R ≥ 0.60 in at least
  6 of them.
- failure: on V1, median R < 0.50, or median P < 0.70.
- partial (between success and failure): recorded with the failure mode of each file. H3's status
  does not change on this experiment alone.
- inconclusive: by the timebox there are fewer than 8 usable files, or no reference can be built
  independently of Lunima. Record the reason.
- V2 is descriptive, with the expectation stated now: D < 0.20, because nothing in the importer
  splits a flat cell. A result of D ≥ 0.50 contradicts our reading of the code and is reported as
  a surprise.
- consequences, fixed now:
  - **V1 success:** the existing import may be offered on an interviewee's own layout that they
    are free to share. Nothing gets built.
  - **V1 failure:** H3's riskiest assumption is weakened for the current implementation. Any H3
    build then needs a DR that cites this data.
  - **V2 D < 0.20:** any build for flat layouts (for example #814) needs a DR with a cost
    estimate first.
- measurement: the dev agent runs the import on the headless CLI (`CAP.Desktop agent run`) and computes R, P and D against the independent reference. Third-party files are downloaded into a fresh, empty directory, treated as untrusted, and not committed. For each file it records under `## Results`: source URL, SHA-256, licence, reference size, and R, P and D for V1 and V2.
- timebox: 2026-11-16

## Results
Append dated results here. Never edit the pre-registration; new criteria = new experiment.
