# Strategic hypotheses

Competing directions for Lunima. None of them is assumed to be right. Each is rated only by
evidence (`evidence/`); the strategist may add hypotheses and change statuses but never
deletes one — it sets `status: dropped` with a reason.

Status: `open | testing | supported | weakened | dropped | superseded`

Evidence lines give the id, the level and a one-line reading. Tags follow the scout's `supports` /
`contradicts` fields. Lines marked *(strategist reading)* are the strategist's extension and are
not tagged by the scout. As of 2026-w45 every entry is E0 or E1: **no hypothesis has any E2/E3
evidence yet.**

## H0: Status quo — keep the current direction, build nothing new
- status: open
- statement: The current roadmap (education ladder, visual editor, circuit simulation) is good enough; new strategic initiatives would not pay for their maintenance.
- riskiest assumption: current users stay and grow without new workflows.
- evidence for:
- evidence against:
- notes (2026-w45): nobody has checked whether the "current users" this depends on exist or come back. Tested by X-2026-w45-returning-testers (needs approval).

## H1: Visual PIC design
- status: open
- statement: Engineers and students want a visual, inspectable PIC editor more than another code-first library.
- riskiest assumption: experts accept a GUI next to KLayout / gdsfactory instead of scripting.
- evidence for:
  - E-2026-w45-09 (E1): researchers name manual scripting as the PIC productivity bottleneck. This supports the premise only; their remedy is LLM-generated scripts, a third path that competes with a GUI.
- evidence against:
  - E-2026-w45-01 (E1) *(strategist reading, after the scout's note)*: a free vendor tool now offers a local GUI with schematic and layout, so a visual editor alone is no longer rare.
- notes (2026-w45): the returning-tester follow-up (X-2026-w45-returning-testers) asks what people actually came back for.

## H2: Agent-driven PIC engineering
- status: weakened
- statement: An agent that builds, repairs and simulates circuits in a visible, undoable environment saves designers real time.
- riskiest assumption: agent output is trustworthy enough; GDSFactory+ (gfp-mcp) and Flexcompute already occupy agent workflows.
- evidence for:
  - E-2026-w45-09 (E1): manual scripting is named as the bottleneck (premise only; the 48-task benchmark is the authors' own).
- evidence against:
  - E-2026-w45-01 (E1): PhotonForge 2.0, free since 2026-10-15, claims agent access over MCP plus GUI, layout and circuit simulation. This is a vendor claim we have not checked hands-on.
  - E-2026-w45-07 (E1): gfp-mcp has given Claude build, DRC, LVS and simulation tools for gdsfactory since 2026-01. No release since 2026-02-23.
  - E-2026-w45-08 (E1): the best 2025 models reach ~57 % end-to-end pass@5 on circuits of ≤ 15 components.
- status reason (2026-w45): both halves of the riskiest assumption now have E1 evidence against them, and no E2/E3 shows anyone wanting agent-driven design in Lunima. H2 is weakened as a differentiator but not dropped. The remaining distinct claim is untested: an agent that checks its own work (validate, simulate, compare netlist) in a design a human watches and can undo. See DR-0001 and the spike `phido-testbench-coverage`.
- internal facts (E0, not demand evidence): a headless agent path with 73 tools shipped on 2026-11-01. One real-agent run built and checked an MZI end-to-end (`docs/strategy/agent-first-architecture-review.md`, addendum).

## H3: Semantic GDS reconstruction
- status: testing
- statement: Turning foreign, flat GDS layouts back into editable components, pins and routes solves a frequent, painful problem.
- riskiest assumption: reconstruction quality on real foreign layouts is good enough to be useful.
- evidence for:
  - E-2026-w45-03 (E1): Luceda's official sample sets the ports of an imported GDS cell by hand ("this info is not in the gdsii file yet").
  - E-2026-w45-04 (E1): gdsfactory's `import_gds` adds no pins; recovery works only from pin shapes or labels (v7 docs).
- evidence against:
  - E-2026-w45-02 (E1): PhotonForge documents a foreign-GDS → ports → connections → circuit-simulation flow. The use case is real for a vendor, which weakens "open tools lack it" as Lunima's edge.
- internal / agent hypotheses (E0, not proof):
  - E-2026-w45-05 (E0): the repo has no third-party, unlabelled GDS fixture, so reconstruction quality on foreign layouts is unmeasured.
  - E-2026-w45-06 (E0, agent hypothesis): if H3 has value, it probably lies in layouts with neither labels nor hierarchy. The importer cannot split a single flat cell into devices today (`CAP-DataAccess/Import/Gds/GdsHierarchyImporter.cs`: explode instantiates only the top cell's direct children).
- tests: X-2026-w45-foreign-gds-reconstruction (autonomous benchmark, timebox 2026-11-16); X-2026-w44-designer-interviews (#1595, approval #1599 pending). Demand frequency is still unknown.

## H4: Circuit and system simulation
- status: weakened
- statement: Circuit-level (S-matrix, transient) simulation in the same tool as layout is a reason to choose Lunima.
- riskiest assumption: gdsfactory + SAX and commercial tools don't already cover the need.
- evidence for:
- evidence against:
  - E-2026-w45-01 (E1): a free commercial tool claims schematic, layout and circuit simulation in one environment, which hits the riskiest assumption directly.
  - E-2026-w45-02 (E1) *(strategist reading)*: the same vendor documents circuit simulation straight from an imported layout.
- status reason (2026-w45): weakened as a *reason to choose Lunima*, pending a hands-on check of PhotonForge's free tier. This is not evidence that circuit simulation is unwanted. Lunima's possible remaining edges are transient/eye analysis and an open, local, inspectable stack, and both are unvalidated.

## H5: Multi-chiplet design
- status: open
- statement: Designers need to model several dies connected by edge couplers in one environment, and open tools lack it.
- riskiest assumption: enough designers work on multi-chiplet photonic systems today.
- evidence for:
  - E-2026-w45-10 (E1, weak): photonic-accelerator architects leave out inter-chiplet optical detail for lack of a device-aware model. This shows a modelling gap among architects, not demand from PIC designers.
- evidence against:

## H6: Cross-tool interoperability
- status: open
- statement: Moving designs between Nazca, gdsfactory, KLayout, SAX and simulators is painful; a reliable bridge is valuable.
- riskiest assumption: users would adopt a new tool as the hub instead of scripts.
- evidence for:
  - E-2026-w45-11 (E1): Luceda sells an IPKISS → Cadence bridge against "manual transfer errors". The pain is real enough to pay for, but only in commercial-to-commercial flows.
- evidence against:
- notes (2026-w45): vendors already sell connectors (PhotonForge Enterprise "EDA connectors", per the E-2026-w45-01 source), so a hub role would have to come from openness, not from being first.

## H7: Educational photonics software
- status: open
- statement: Universities and learners need a NAND2TETRIS-style path into photonic computing; this is Lunima's most distinctive asset.
- riskiest assumption: educators adopt it in courses; learners are a sustainable user base.
- evidence for:
- evidence against:
  - E-2026-w45-12 (E1, partial): a free, established course → fab → test path (SiEPIC/openEBL on KLayout) already serves device and layout teaching. The photonic-computing ladder niche is untouched, but nothing shows it exists as a course need.
- notes (2026-w45): the roadmap puts most effort here and there is no educator evidence at all. It is tracked as opportunity `photonic-computing-course-ladder`; interview #1597 is pending.

## H8: Design-to-fabrication workflow
- status: open
- statement: Getting a design from idea to a foundry-ready GDS (PDK, DRC, export) is the bottleneck worth solving.
- riskiest assumption: Lunima can be trusted near tape-out without foundry sign-off tooling.
- evidence for:
- evidence against:
  - E-2026-w45-12 (E1) *(strategist reading)*: for students, a free design-to-fab path already exists, so this bottleneck may already be served for that segment.
- notes (2026-w45): the roadmap planned an openEBL 2026-10 submission of a Lunima design (deadline 2026-10-17). Its outcome is not recorded as evidence. It would validate the export path technically, not show demand.
