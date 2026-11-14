# Strategic hypotheses

Competing directions for Lunima. None of them is assumed to be right. Each is rated only by
evidence (`evidence/`); the strategist may add hypotheses and change statuses but never
deletes one — it sets `status: dropped` with a reason.

Status: `open | testing | supported | weakened | dropped | superseded`

State after 2026-w44: every external entry so far is E1 (public, indirect). There is no E2 or
E3 evidence for any hypothesis yet, so nothing below is `supported`. `E-2026-w45-01` in the
evidence index is the format example from `evidence/README.md`, not a real entry. It is cited
nowhere.

## H0: Status quo — keep the current direction, build nothing new
- status: open
- statement: The current roadmap (education ladder, visual editor, circuit simulation) is good enough; new strategic initiatives would not pay for their maintenance.
- riskiest assumption: current users stay and grow without new workflows.
- evidence for:
- evidence against:
- note (2026-w44): No evidence either way. There are no usage metrics (`metrics.md`: none configured), so "current users" cannot be counted yet.

## H1: Visual PIC design
- status: open
- statement: Engineers and students want a visual, inspectable PIC editor more than another code-first library.
- riskiest assumption: experts accept a GUI next to KLayout / gdsfactory instead of scripting.
- evidence for:
- evidence against: E-2026-w44-12 (E1: a foundry's PDK docs present code-first layout as the advantage over manual editors. This is the foundry's framing, not user testimony.)
- note (2026-w44): One weak item, which is not enough to change the status. Students (H7) may judge this differently from experts. That split is untested.

## H2: Agent-driven PIC engineering
- status: weakened
- statement: An agent that builds, repairs and simulates circuits in a visible, undoable environment saves designers real time.
- riskiest assumption: agent output is trustworthy enough; GDSFactory+ (gfp-mcp) and Flexcompute already occupy agent workflows.
- evidence for: E-2026-w44-03 (E1, weak: a vendor teaches engineer oversight and verification), E-2026-w44-04 (E1: authors' claim that manual PIC design costs minutes to days)
- evidence against: E-2026-w44-01 (E1: gfp-mcp already exposes build/DRC/LVS/simulate to agents), E-2026-w44-02 (E1: Flexcompute sells an end-to-end agent loop), E-2026-w44-05 (E1: agent success drops sharply on larger circuits), E-2026-w44-06 (E1: another academic agent framework, so the field is crowded)
- agent hypotheses (not evidence): E-2026-w44-14 (E0)
- note (2026-w44): Weakened as a *differentiator*. The second half of the riskiest assumption, that competitors already occupy agent workflows, is now confirmed by four independent E1 sources. Whether a *visible, undoable* agent environment saves a designer time is still untested; there is no E2/E3 for or against. The maintainer merged a headless MCP/CLI host (#1591) before any of this evidence existed. That is sunk cost, not evidence. See DR-0001.

## H3: Semantic GDS reconstruction
- status: testing
- statement: Turning foreign, flat GDS layouts back into editable components, pins and routes solves a frequent, painful problem.
- riskiest assumption: reconstruction quality on real foreign layouts is good enough to be useful.
- evidence for: E-2026-w44-07 (E1: gdsfactory docs say imported GDS arrives without pins and GDS lacks a metadata standard), E-2026-w44-08 (E1, 2015: a researcher reports that recovering connectivity from photonic geometry is hard)
- evidence against:
- agent hypotheses (not evidence): E-2026-w44-14 (E0: no competitor found that reconstructs flat, marker-free GDS)
- note (2026-w44): Both E1 items show the problem *exists* and is *hard*. Neither shows that it is *frequent* or that anyone would switch tools for it. E-2026-w44-08 cuts both ways: hard for others also means hard for Lunima. Both halves are under test: frequency in X-2026-w44-designer-interviews, quality in X-2026-w44-flat-gds-reconstruction. "No competitor found" is a gap in one week's desk search, not evidence of a market.

## H4: Circuit and system simulation
- status: weakened
- statement: Circuit-level (S-matrix, transient) simulation in the same tool as layout is a reason to choose Lunima.
- riskiest assumption: gdsfactory + SAX and commercial tools don't already cover the need.
- evidence for:
- evidence against: E-2026-w44-02 (E1: layout + circuit simulation in one agent loop), E-2026-w44-09 (E1: Luceda runs S-matrix simulation from its layout viewer), E-2026-w44-10 (E1: Luceda netlist → Caphe hand-off), E-2026-w44-11 (E1: circulax adds open transient/mixed-domain simulation to the gdsfactory stack)
- note (2026-w44): The riskiest assumption is contradicted by four E1 sources, so "simulation next to layout" alone is not a reason to switch. This does not cover the education use (H7), where simulation is part of learning rather than a competitive feature.

## H5: Multi-chiplet design
- status: open
- statement: Designers need to model several dies connected by edge couplers in one environment, and open tools lack it.
- riskiest assumption: enough designers work on multi-chiplet photonic systems today.
- evidence for:
- evidence against:
- note (2026-w44): Not searched this week. The next scout run should look for it.

## H6: Cross-tool interoperability
- status: open
- statement: Moving designs between Nazca, gdsfactory, KLayout, SAX and simulators is painful; a reliable bridge is valuable.
- riskiest assumption: users would adopt a new tool as the hub instead of scripts.
- evidence for: E-2026-w44-13 (E1, weak, 2018: vendors build multi-tool export because designers use different layout tools; no pain described)
- evidence against:

## H7: Educational photonics software
- status: open
- statement: Universities and learners need a NAND2TETRIS-style path into photonic computing; this is Lunima's most distinctive asset.
- riskiest assumption: educators adopt it in courses; learners are a sustainable user base.
- evidence for:
- evidence against:
- note (2026-w44): No evidence was collected. The scout's search leaned toward H2/H3/H4, and the one teaching source it tried was a dead end. This is a coverage gap, not a negative result, and the internal review rates this as the most distinctive asset. The next scout run should search educator and course sources deliberately (e.g. SiEPIC/openEBL teaching, photonics course tool choices).

## H8: Design-to-fabrication workflow
- status: open
- statement: Getting a design from idea to a foundry-ready GDS (PDK, DRC, export) is the bottleneck worth solving.
- riskiest assumption: Lunima can be trusted near tape-out without foundry sign-off tooling.
- evidence for:
- evidence against: E-2026-w44-10 (E1: an incumbent integrates native DRC, which raises the bar)
- note (2026-w44): The roadmap mentions an openEBL submission (deadline 2026-10-17) and a planned Cornerstone outreach. If they happened, their outcome (accepted, rejected, foundry reply) would be the first real external validation of a Lunima workflow. It should be recorded as evidence by the maintainer.

## H9: Lunima as the inspection layer for agent-generated layouts (added 2026-w44)
- status: open
- statement: As agents generate PIC layouts in other tools (gdsfactory/gfp-mcp, PhotonForge, research frameworks), designers need an independent place to see, check and correct what the agent produced. A local visual tool that imports the result and shows connectivity, loss and DRC-lite findings is valuable even if the agent runs elsewhere.
- riskiest assumption: designers verify agent output by looking at it in a separate tool at all, rather than in the agent's own environment (VS Code GDS viewer, KLayout) or by trusting DRC/LVS reports.
- evidence for: E-2026-w44-03 (E1: "how to verify what the AI produces" is on a competitor's training agenda), E-2026-w44-05 (E1: agent output degrades on larger circuits, so verification has work to do)
- evidence against: E-2026-w44-01 (E1: gfp-mcp already exposes check_drc / check_lvs to the agent itself, so verification may stay inside the generating tool)
- note: This combines H2 (agents), H3 (import) and H6 (interop), but it is a different bet. Lunima would not compete as the agent's environment; it would sit downstream of other tools. Its value depends on H3's import quality. It is E1-only and must be tested with designers' past behaviour. The question is whether they have actually checked AI-generated or colleague-generated layouts and how, not whether they like the idea.
