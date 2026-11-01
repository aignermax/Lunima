# X-2026-w44-flat-gds-reconstruction: Does Lunima's GDS import recover ports and connectivity from flat, marker-free layouts?
- status: rejected-by-critic
- method: benchmark
- autonomy: autonomous
- assumption: Lunima's existing GDS import (`CAP-DataAccess/Import/Gds/GdsHierarchyImporter.cs`, `CAP.Avalonia/Services/GdsImport/GdsImportService.cs`) recovers usable optical ports and waveguide connectivity from a **flattened** layout **without** pin-marker layers or port labels.
- hypotheses: H3, H9
- budget: 0 USD; one spike (agent-written harness and corpus script) plus at most 30 min maintainer review; no change to import code during the benchmark

## Pre-registration
- corpus: 10 layouts with a known netlist.
  - 6 synthetic: open-source circuits (e.g. MZI, ring add-drop, 1×4 splitter tree, two ring filters, a crossing network, a grating-coupler test structure) generated hierarchically with known ports, then **flattened into one cell with all pin/port marker layers and text labels removed**.
  - 4 third-party: public layouts from a public open-submission repository (e.g. `github.com/SiEPIC/openEBL-2024-02`), treated the same way.
  - Ground truth (port positions, facings, netlist edges) is extracted from each layout *before* stripping. The corpus is fixed and committed **before** the first import run. Third-party files are committed only as URL plus SHA-256 and fetched by the harness, unless their licence permits redistribution.
- control: the same 10 layouts imported un-flattened and with markers. This separates "marker-free flat GDS" (the H3 gap) from the case gdsfactory already handles (E-2026-w44-07).
- metrics, per layout: port recall and precision (a detected port within 0.5 µm of a true port with the correct facing ±10°), and connection recall (true netlist edges whose two endpoints are connected after import). Report medians over the flat set and over the control set separately.
- success: on the **flat, marker-free** set, median port recall ≥ 0.7 **and** median port precision ≥ 0.7 **and** median connection recall ≥ 0.5.
- failure: on the flat, marker-free set, median port recall < 0.4 **or** median connection recall < 0.25. If so, H3's claim narrows to "hierarchical GDS with markers", which competitors already cover, and H3 should be set to `weakened`.
- inconclusive: anything in between, or fewer than 8 of the 10 layouts importing without a crash. Crashes are reported per layout and count as zero recall.
- measurement: a reproducible harness committed under `scripts/` or as a `Category=Heavy` test, run by the agent. The numbers are copied verbatim into Results with the commit hash. The thresholds are not tuned after the first run, and import code is not changed to pass the benchmark during the timebox.
- timebox: 2026-11-22

## Results
