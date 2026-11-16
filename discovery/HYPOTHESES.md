# Strategic hypotheses

Competing directions for Lunima. None of them is assumed to be right. Each is rated only by
evidence (`evidence/`); the strategist may add hypotheses and change statuses but never
deletes one — it sets `status: dropped` with a reason.

Status: `open | testing | supported | weakened | dropped | superseded`

All evidence so far is E1 (public, external, indirect). No E2 or E3 entry exists for any
hypothesis, so none can be `supported`. `weakened` below means "weakened as a direction for
Lunima", usually because a competitor already occupies it, not that the underlying user
problem has been shown to be absent.

## H0: Status quo — keep the current direction, build nothing new
- status: testing
- statement: The current roadmap (education ladder, visual editor, circuit simulation) is good enough; new strategic initiatives would not pay for their maintenance.
- riskiest assumption: current users stay and grow without new workflows.
- evidence for:
- evidence against:
- note (2026-w46): E-2026-w46-01 to -07 and -09 make the new initiatives (H2, H3, H4) look less distinctive, but they say nothing about the riskiest assumption. Whether Lunima has current users outside the maintainer's workplace at all is unknown; X-2026-w46-outside-users tests that.

## H1: Visual PIC design
- status: open
- statement: Engineers and students want a visual, inspectable PIC editor more than another code-first library.
- riskiest assumption: experts accept a GUI next to KLayout / gdsfactory instead of scripting.
- evidence for: E-2026-w46-03 (E1), E-2026-w46-05 (E1)
- evidence against:
- note (2026-w46): both entries are vendor bets, not user statements. Two vendors investing in visual PIC editing suggests they see demand. It also shows the space is contested: E-2026-w46-03 is a commercial cross-platform desktop tool with a layout canvas, gdsfactory, real-time DRC, an AI assistant, guided walkthroughs and free Pro licences for universities, which is close to Lunima's own positioning.

## H2: Agent-driven PIC engineering
- status: weakened
- statement: An agent that builds, repairs and simulates circuits in a visible, undoable environment saves designers real time.
- riskiest assumption: agent output is trustworthy enough; GDSFactory+ (gfp-mcp) and Flexcompute already occupy agent workflows.
- evidence for:
- evidence against: E-2026-w46-04 (E1), E-2026-w46-05 (E1), E-2026-w46-06 (E1), E-2026-w46-07 (E1)
- note (2026-w46): weakened as a differentiator, not as a user need. The internal review named "watched, inspected and undone" agent work as Lunima's plausible edge. GDSFactory+ ships a diff preview with explicit confirmation and a verification mode (E-2026-w46-04), and its 2.0 agent edits components and connections (E-2026-w46-05). E-2026-w46-07 (~57 % end-to-end success on ≤ 15-component circuits) says agent output still needs human review, but a competitor already offers that review surface. Nobody has been asked whether they use agents for PIC work at all. See DR-0001.

## H3: Semantic GDS reconstruction
- status: weakened
- statement: Turning foreign, flat GDS layouts back into editable components, pins and routes solves a frequent, painful problem.
- riskiest assumption: reconstruction quality on real foreign layouts is good enough to be useful.
- evidence for: E-2026-w46-10 (E1), E-2026-w46-11 (E1)
- evidence against: E-2026-w46-01 (E1), E-2026-w46-02 (E1), E-2026-w46-12 (E1)
- note (2026-w46): open tools recover ports only by hand or from pin-marker conventions (-10, -11). Two commercial tools already derive pins on foreign cells and recognise PDK cells in hierarchical imports (-01, -02, -12). The only unoccupied case is flat layouts without labels. That case has no first-hand demand statement in the evidence, and Lunima cannot handle it either. Opportunity `gds-foreign-layout-import` is parked this week. Reopen if a conversation describes receiving an unlabelled circuit layout in the past 6 months.

## H4: Circuit and system simulation
- status: weakened
- statement: Circuit-level (S-matrix, transient) simulation in the same tool as layout is a reason to choose Lunima.
- riskiest assumption: gdsfactory + SAX and commercial tools don't already cover the need.
- evidence for: E-2026-w46-08 (E1)
- evidence against: E-2026-w46-09 (E1)
- note (2026-w46): -08 shows a real, years-long request for time-domain simulation in the leading open S-matrix simulator. Its maintainers now point that request to circulax (-09), which offers transient, DC, AC and harmonic-balance analysis next to gdsfactory. This hits the riskiest assumption directly. What remains untested is the narrower claim that having it in the same visual tool matters to anyone.

## H5: Multi-chiplet design
- status: open
- statement: Designers need to model several dies connected by edge couplers in one environment, and open tools lack it.
- riskiest assumption: enough designers work on multi-chiplet photonic systems today.
- evidence for:
- evidence against:
- note (2026-w46): the scout found nothing usable this week.

## H6: Cross-tool interoperability
- status: open
- statement: Moving designs between Nazca, gdsfactory, KLayout, SAX and simulators is painful; a reliable bridge is valuable.
- riskiest assumption: users would adopt a new tool as the hub instead of scripts.
- evidence for: E-2026-w46-15 (E1)
- evidence against:
- note (2026-w46): -15 supports only the premise: no finished vendor-neutral photonic netlist standard is visible. It says nothing about whether anyone would adopt a hub tool.

## H7: Educational photonics software
- status: testing
- statement: Universities and learners need a NAND2TETRIS-style path into photonic computing; this is Lunima's most distinctive asset.
- riskiest assumption: educators adopt it in courses; learners are a sustainable user base.
- evidence for:
- evidence against: E-2026-w46-13 (E1), E-2026-w46-14 (E1, weak)
- note (2026-w46): -13 shows that general PIC design teaching already has an established route (existing tools plus real fabrication). -14 is a single course where optical computing is taught as ML accelerators through lectures and presentations, not digital logic. Neither entry rules out a course slot for a digital photonic-computing ladder, but nothing supports one. X-2026-w46-digital-photonic-courses tests whether such courses exist. DR-0002 asks whether the next ladder rung should wait for that result.

## H8: Design-to-fabrication workflow
- status: open
- statement: Getting a design from idea to a foundry-ready GDS (PDK, DRC, export) is the bottleneck worth solving.
- riskiest assumption: Lunima can be trusted near tape-out without foundry sign-off tooling.
- evidence for:
- evidence against:
- note (2026-w46): the scout found nothing usable this week.

## H9: Photonic-computing builders
- status: open
- statement: Teams building digital, logic-level photonic processors (research groups, start-ups) need to go from device S-matrices to gate, timing and instruction-level behaviour in one inspectable tool, and no open tool spans those levels.
- riskiest assumption: enough teams work on digital photonic logic today, and they would use a visual tool rather than their own scripts.
- evidence for:
- evidence against: E-2026-w46-14 (E1, weak and indirect)
- note (2026-w46): added this week as an agent hypothesis, not from external evidence. Its origin is that three of the five personas in `docs/PERSONAS.md` (Peter, Mirko, Jonas) are building toward a photonic computer, and the internal review asks (§10, question 5) whether the ladder is for education or for professional design. The evidence weakens the general-education framing of H7 (-13). If the ladder's gate-to-instruction stack is distinctive, its audience may be builders rather than courses. -14 hints that academic optical computing centres on ML accelerators rather than digital logic, which would make this audience small. No test is registered yet. The returning-tester interview asks what people were building toward.
