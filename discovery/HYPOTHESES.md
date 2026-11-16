# Strategic hypotheses

Competing directions for Lunima. None of them is assumed to be right. Each is rated only by
evidence (`evidence/`); the strategist may add hypotheses and change statuses but never
deletes one — it sets `status: dropped` with a reason.

Status: `open | testing | supported | weakened | dropped | superseded`

State after 2026-w47: every entry so far is E1 (public, indirect) or E0. No hypothesis has an
E2 or E3 entry, so none can be `supported`, and none is weakened enough to drop.

## H0: Status quo — keep the current direction, build nothing new
- status: open
- statement: The current roadmap (education ladder, visual editor, circuit simulation) is good enough; new strategic initiatives would not pay for their maintenance.
- riskiest assumption: current users stay and grow without new workflows.
- evidence for:
- evidence against:
- open tests: X-2026-w47-public-footprint (autonomous); X-2026-w45-returning-testers (#1609) and X-2026-w46-outside-users (#1620), both awaiting approval
- note (2026-w47): No evidence either way. Nothing in discovery shows a user outside the maintainer, the maintainer's workplace and the agents, so "current users" is not established yet. The footprint test checks whether such users leave any public trace. Maintainer-filed issues #808 and #816–#819 pass on feedback from outside people; one of them took part in a workshop and works close to the maintainer's workplace. These issues are not in the evidence index yet. The scout should log them as second-hand `user-feedback` and note that they are not independent.

## H1: Visual PIC design
- status: open
- statement: Engineers and students want a visual, inspectable PIC editor more than another code-first library.
- riskiest assumption: experts accept a GUI next to KLayout / gdsfactory instead of scripting.
- evidence for: E-2026-w47-07 (E1) — the code-first leader sells a GUI with schematic capture as a paid layer on top of gdsfactory
- evidence against:
- note (2026-w47): This shows demand for a GUI in general. It also shows that the leading ecosystem already has one. It does not show that experts would choose Lunima's.

## H2: Agent-driven PIC engineering
- status: open
- statement: An agent that builds, repairs and simulates circuits in a visible, undoable environment saves designers real time.
- riskiest assumption: agent output is trustworthy enough; GDSFactory+ (gfp-mcp) and Flexcompute already occupy agent workflows.
- evidence for: E-2026-w47-04 (E1) — a research agent built on gdsfactory needed human inspection at every stage and reached about 57 % end-to-end success at best; E-2026-w47-06 (E1) — the market's agent vendor tells users to review everything the agent produces
- evidence against: E-2026-w47-05 (E1) — GDSFactory+ shipped an MCP server (2026-01) and a verify-before-commit agent mode (2026-03)
- internal (E0, not demand): one agent run built, routed, simulated and validated an MZI through the stdio server (`docs/strategy/agent-first-architecture-review.md`, addendum)
- note (2026-w47): E1 evidence supports the need for a review surface. It weakens the uniqueness claim. The strategy review's competitor note ("about 11 tools, no editing or routing") does not reflect the GDSFactory+ agent changelog. Without a hands-on comparison, it should not be repeated as a Lunima advantage. E-04 also argues against "trustworthy enough": with 57 % end-to-end success, agent output needs a human check.

## H3: Semantic GDS reconstruction
- status: open
- statement: Turning foreign, flat GDS layouts back into editable components, pins and routes solves a frequent, painful problem.
- riskiest assumption: reconstruction quality on real foreign layouts is good enough to be useful.
- evidence for: E-2026-w47-01 (E1) — a commercial tool documents the "only the GDS is left" job and leaves ports, netlist and model to the user; E-2026-w47-03 (E1) — gdsfactory recovers ports only from marker layers
- evidence against: E-2026-w47-02 (E1) — a funded competitor auto-detects ports on imported layouts once the waveguide technology is known
- agent hypotheses (E0, not evidence): E-2026-w47-15
- note (2026-w47): The scope is narrower now. Three tools document wrapping a foreign GDS as a black box with ports: by hand, by auto-detection, or from marker layers. Lunima's rung 1 ships the same thing. No tool documents the remaining part, rebuilding routes and sub-components, and the w47 search found no designer asking for it. Opportunity #1598 is closed. It should be reopened only on interview evidence (#1595 / #1596).

## H4: Circuit and system simulation
- status: open
- statement: Circuit-level (S-matrix, transient) simulation in the same tool as layout is a reason to choose Lunima.
- riskiest assumption: gdsfactory + SAX and commercial tools don't already cover the need.
- evidence for:
- evidence against: E-2026-w47-07 (E1) — GDSFactory+ already bundles layout, schematic capture and circuit simulation in one product
- note (2026-w47): This weakens H4 as a reason to choose Lunima. It says nothing against simulation being useful.

## H5: Multi-chiplet design
- status: open
- statement: Designers need to model several dies connected by edge couplers in one environment, and open tools lack it.
- riskiest assumption: enough designers work on multi-chiplet photonic systems today.
- evidence for: E-2026-w47-12 (E1, weak) — a verification-tool vendor sees no verification discipline for the optical side of co-packaged chiplets
- evidence against: E-2026-w47-13 (E1) — an incumbent EDA vendor treats the photonic die as one more die in existing 3D-IC flows
- note (2026-w47): Both sources speak for large-company flows, neither for users of open tools.

## H6: Cross-tool interoperability
- status: open
- statement: Moving designs between Nazca, gdsfactory, KLayout, SAX and simulators is painful; a reliable bridge is valuable.
- riskiest assumption: users would adopt a new tool as the hub instead of scripts.
- evidence for: E-2026-w47-08 (E1) — one three-day MPW training needs a different layout stack per foundry; E-2026-w47-11 (E1) — a foundry ships its PDK for exactly two design tools
- evidence against:
- note (2026-w47): The fragmentation is real. Whether it hurts, and whether anyone wants a hub instead of scripts (the riskiest assumption), is untested.

## H7: Educational photonics software
- status: open
- statement: Universities and learners need a NAND2TETRIS-style path into photonic computing; this is Lunima's most distinctive asset.
- riskiest assumption: educators adopt it in courses; learners are a sustainable user base.
- evidence for: E-2026-w47-14 (E1, weak) — photonic computing is taught hands-on, but as quantum computing on real MZI-mesh hardware
- evidence against:
- open tests: X-2026-w47-public-footprint (autonomous); X-2026-w46-digital-photonic-courses (#1619); interview I:photonics-course-tooling (#1597), awaiting approval
- note (2026-w47): One w47 search pass (the first result for #1619) found no course that teaches digital, logic-level photonic computing hands-on. That is either a gap Lunima could fill or a sign that nobody teaches it because nobody asks for it. Only the course-tooling interview can tell which.

## H8: Design-to-fabrication workflow
- status: open
- statement: Getting a design from idea to a foundry-ready GDS (PDK, DRC, export) is the bottleneck worth solving.
- riskiest assumption: Lunima can be trusted near tape-out without foundry sign-off tooling.
- evidence for: E-2026-w47-09 (E1) — a foundry plans 2–5 DRC iterations per MPW design; E-2026-w47-10 (E1) — its most frequent layout errors are rule-checkable; E-2026-w47-08 (E1) — foundry training centres on DRC compliance and tape-out preparation
- evidence against:
- dependency: E-2026-w47-11 (E1) — foundry PDKs ship for IPKISS and gdsfactory, so a third tool reaches them only by importing one of those
- open tests: X-2026-w47-openebl-outcome
- note (2026-w47): The friction is real and measurable from the foundry's side. The incumbent tools' foundry DRC decks already catch its rule-checkable part, and part of the DRC is manual inspection by the foundry. The riskiest assumption has an external check the roadmap already planned: the openEBL 2026-10 submission. Its outcome is not recorded in discovery.

## H9: Physical verification of photonic logic (virtual tape-out)
- status: open
- statement: People who design photonic logic or photonic computers want a model-based check that a design would physically work (power budget, cascadability, noise, variation) before any fab run, and they trust a verdict built on generic, source-labelled models.
- riskiest assumption: someone outside the maintainer's team acts on such a verdict, although the models are generic rather than measured at a foundry.
- evidence for: E-2026-w47-12 (E1, weak and indirect) — "the optical side … has no comparable discipline yet", said about co-packaged optics by a verification vendor
- evidence against:
- note (2026-w47): Added to track the maintainer's rung-8 decision of 2026-11-15 (`docs/ROADMAP.md`, #1638) as a testable bet. It does not challenge that decision. Rung 8's own outputs are simulated (E0). They count as external evidence only once an outside user or reviewer checks a verdict or acts on it.
