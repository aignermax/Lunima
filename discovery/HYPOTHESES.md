# Strategic hypotheses

Competing directions for Lunima. None of them is assumed to be right. Each is rated only by
evidence (`evidence/`); the strategist may add hypotheses and change statuses but never
deletes one — it sets `status: dropped` with a reason.

Status: `open | testing | supported | weakened | dropped | superseded`

## H0: Status quo — keep the current direction, build nothing new
- status: open
- statement: The current roadmap (education ladder, visual editor, circuit simulation) is good enough; new strategic initiatives would not pay for their maintenance.
- riskiest assumption: current users stay and grow without new workflows.
- evidence for:
- evidence against:

## H1: Visual PIC design
- status: open
- statement: Engineers and students want a visual, inspectable PIC editor more than another code-first library.
- riskiest assumption: experts accept a GUI next to KLayout / gdsfactory instead of scripting.
- evidence for:
- evidence against:

## H2: Agent-driven PIC engineering
- status: open
- statement: An agent that builds, repairs and simulates circuits in a visible, undoable environment saves designers real time.
- riskiest assumption: agent output is trustworthy enough; GDSFactory+ (gfp-mcp) and Flexcompute already occupy agent workflows.
- evidence for:
- evidence against:

## H3: Semantic GDS reconstruction
- status: open
- statement: Turning foreign, flat GDS layouts back into editable components, pins and routes solves a frequent, painful problem.
- riskiest assumption: reconstruction quality on real foreign layouts is good enough to be useful.
- evidence for:
- evidence against:

## H4: Circuit and system simulation
- status: open
- statement: Circuit-level (S-matrix, transient) simulation in the same tool as layout is a reason to choose Lunima.
- riskiest assumption: gdsfactory + SAX and commercial tools don't already cover the need.
- evidence for:
- evidence against:

## H5: Multi-chiplet design
- status: open
- statement: Designers need to model several dies connected by edge couplers in one environment, and open tools lack it.
- riskiest assumption: enough designers work on multi-chiplet photonic systems today.
- evidence for:
- evidence against:

## H6: Cross-tool interoperability
- status: open
- statement: Moving designs between Nazca, gdsfactory, KLayout, SAX and simulators is painful; a reliable bridge is valuable.
- riskiest assumption: users would adopt a new tool as the hub instead of scripts.
- evidence for:
- evidence against:

## H7: Educational photonics software
- status: open
- statement: Universities and learners need a NAND2TETRIS-style path into photonic computing; this is Lunima's most distinctive asset.
- riskiest assumption: educators adopt it in courses; learners are a sustainable user base.
- evidence for:
- evidence against:

## H8: Design-to-fabrication workflow
- status: open
- statement: Getting a design from idea to a foundry-ready GDS (PDK, DRC, export) is the bottleneck worth solving.
- riskiest assumption: Lunima can be trusted near tape-out without foundry sign-off tooling.
- evidence for:
- evidence against:
