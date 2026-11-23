# Strategic hypotheses

Competing directions for Lunima. None of them is assumed to be right. Each is rated only by
evidence (`evidence/`); the strategist may add hypotheses and change statuses but never
deletes one — it sets `status: dropped` with a reason.

Status: `open | testing | supported | weakened | dropped | superseded`

## H0: Status quo — keep the current direction, build nothing new
- status: open
- statement: The current roadmap (education ladder, visual editor, circuit simulation) is good enough; new strategic initiatives would not pay for their maintenance.
- riskiest assumption: current users stay and grow without new workflows.
- evidence for: E-2026-w48-01–03 (E1) describe existing automation alternatives; avoiding another strategic build preserves attention while demand is unknown.
- evidence against: None directly. Missing analytics and missing public-use records do not establish non-use or weaken H0.

## H1: Visual PIC design
- status: weakened
- statement: Engineers and students want a visual, inspectable PIC editor more than another code-first library.
- riskiest assumption: experts accept a GUI next to KLayout / gdsfactory instead of scripting.
- evidence for: README.md documents a visual workflow (internal capability description, E0; not adoption).
- evidence against: E-2026-w48-02 and E-2026-w48-04 (E1) challenge a GUI's sufficiency and exclusive inspectability advantage. This weakens the comparative claim, not the usefulness of a complementary GUI.

## H2: Agent-driven PIC engineering
- status: open
- statement: An agent that builds, repairs and simulates circuits in a visible, undoable environment saves designers real time.
- riskiest assumption: agent output is trustworthy enough; GDSFactory+ (gfp-mcp) and Flexcompute already occupy agent workflows.
- evidence for: README.md describes MCP and headless workflows (E0 capability context). No indexed E2/E3 demonstrates time saved or better designs.
- evidence against: E-2026-w48-01–03 (E1) describe non-agent automation alternatives; none isolates an agent benefit. Maintainer preference for AI-driven design is direction-setting, not validation.

## H3: Semantic GDS reconstruction
- status: open
- statement: Turning foreign, flat GDS layouts back into editable components, pins and routes solves a frequent, painful problem.
- riskiest assumption: reconstruction quality on real foreign layouts is good enough to be useful.
- evidence for: No current indexed demand entry. The supplied issue summaries mention import requests, but do not establish demand for full semantic reconstruction or verified reporter provenance.
- evidence against: Prior actions describe competing black-box import workflows, but their w47 source entries are unavailable. Treat that as a follow-up lead, not newly verified evidence. Keep opportunity gds-foreign-layout-import closed pending a concrete user account.

## H4: Circuit and system simulation
- status: open
- statement: Circuit-level (S-matrix, transient) simulation in the same tool as layout is a reason to choose Lunima.
- riskiest assumption: gdsfactory + SAX and commercial tools don't already cover the need.
- evidence for: Supplied issue summaries #816–819 report workshop feedback about simulation (potential E2 lead, not an indexed E2 entry; original transcript unavailable). README.md describes existing simulation capabilities (E0).
- evidence against: No indexed comparative evidence establishes preference over the current toolchain, numerical accuracy on a user's task, or repeated productive use.

## H5: Multi-chiplet design
- status: open
- statement: Designers need to model several dies connected by edge couplers in one environment, and open tools lack it.
- riskiest assumption: enough designers work on multi-chiplet photonic systems today.
- evidence for: README.md documents chiplet functionality (E0 capability description).
- evidence against: No supplied indexed demand evidence or observed recurring multi-die task. An internal CPU/chiplet demonstration cannot establish a market.

## H6: Cross-tool interoperability
- status: open
- statement: Moving designs between Nazca, gdsfactory, KLayout, SAX and simulators is painful; a reliable bridge is valuable.
- riskiest assumption: users would adopt a new tool as the hub instead of scripts.
- evidence for: README.md lists open exports (E0); issue summaries identify installed export failures (internal QA, not demand).
- evidence against: No indexed user account establishes that a new hub beats the user's existing scripts. Export availability alone does not prove semantic preservation.

## H7: Educational photonics software
- status: open
- statement: Universities and learners need a NAND2TETRIS-style path into photonic computing; this is Lunima's most distinctive asset.
- riskiest assumption: educators adopt it in courses; learners are a sustainable user base.
- evidence for: Existing opportunity #1611 and internal educational positioning, neither external adoption evidence.
- evidence against: The supplied critic says prior course evidence concerned quantum hardware, not digital photonic logic, and flags physical-model limitations. Its underlying sources are unavailable here; these are unresolved objections, not verified new evidence. No teaching adoption is established.

## H8: Design-to-fabrication workflow
- status: open
- statement: Getting a design from idea to a foundry-ready GDS (PDK, DRC, export) is the bottleneck worth solving.
- riskiest assumption: Lunima can be trusted near tape-out without foundry sign-off tooling.
- evidence for: E-2026-w48-01–03 (E1) identify physical implementation constraints. These overlapping research groups are one related evidence cluster, not three independent customer confirmations.
- evidence against: The same entries offer automated solutions. They do not establish Lunima demand or foundry sign-off. The supplied critic rejects interpreting planned foundry iterations as avoidable user pain; repeating related verification scripts does not independently validate physical performance.

## Weekly comparison — 2026-w48

All four indexed entries are E1. There is no indexed E2/E3 in the supplied evidence; this is not a claim that no users or feedback exist. Hypotheses without new direct evidence remain open, not testing or supported. No new hypothesis is justified. Prior actions mention H9 without supplying its definition; no H9 claim is carried forward.

| Direction | What must be true | Cheapest discriminating next test |
|---|---|---|
| H0: no new strategic build | Existing workflows can deliver useful work without expansion | In the existing usage inquiry, ask what was completed and reused; lack of public posts is inconclusive |
| H1: visual design | Inspection/editing removes more work than it adds | Observe a recent layout correction through existing designer interviews; compare with the person's code workflow |
| H2: agent engineering | Verified output saves total human effort, including checking and repair | Select one real task through existing interviews, then prepare a matched manual-code versus agent comparison |
| H3: reconstruction | Recovering routes/components beyond black-box ports is frequent and valuable | Ask for the last foreign-layout task and what information was actually missing; do not reopen on a generic import request |
| H4: simulation | Integrated simulation changes an engineering decision reliably | Request a recent decision and reference result; later test numerical agreement on the same model |
| H5: chiplets | A designer has a recurring multi-die integration problem | Ask whether the last completed project involved multiple dies and how links were checked |
| H6: interoperability | A handoff loses valuable semantics or consumes recurring work | Trace one recent handoff and its manual repair steps before proposing a hub |
| H7: education | An educator can adopt a physically honest lesson into an actual course | Reuse #1597 when approved; ask about the last taught exercise and its constraints |
| H8: design-to-fab | A supported process has preventable, consequential workflow failures | Reuse #1596 to reconstruct a real verification/re-export history; record all runs, including failures |

## Work classes and evidence gates

1. Small safe improvements: existing installed-release/export bug follow-up belongs to delivery, not new discovery spikes. Reuse #1647 findings and existing smoke coverage; inspect remaining import/export coverage and a clean-machine GUI path before a pilot. Closed bugs are not presumed still broken.
2. Prototype/benchmark experiments: a bounded task comparison can test H2 with H4/H6/H8 as workflow dependencies. Baseline, verification time, failed attempts and compute cost must be measured. No new benchmark is registered before a real task and available original protocols are known.
3. Strategic changes: GUI versus agent prioritization, semantic reconstruction, education positioning and chiplet investment need direct user evidence. Do not infer demand from research, demos, stars or issue absence.
4. Architectural approval: AI-only pivot, PIR/schema or API changes, router/solver replacement and physical chiplet/CPU model expansion need maintainer approval after measurable evidence. Simulation changes additionally need numerical reference and correctness tests; large rewrites have no authorization here.

## Record limitations

Only supplied snapshot contents were reviewed, including CLAUDE.md. docs/PERSONAS.md, docs/ROADMAP.md, docs/AGENT_BRIDGE.md and the internal architecture assessment were not supplied; persona references here use only CLAUDE.md's summaries. Original w44–w47 evidence and experiment files are absent from the snapshot. Existing experiment registrations must be recovered before interpreting results or changing any protocol. previous-week.md says no previous run, but the supplied .discovery/out/critic.json contains substantive prior criticism and is addressed in DR-0001. No source file, experiment result or maintainer approval has been inferred from those gaps.
