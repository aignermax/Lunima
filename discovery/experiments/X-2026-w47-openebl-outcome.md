# X-2026-w47-openebl-outcome: Did a fab-run organiser's own check accept a design exported from Lunima?
- status: proposed
- method: desk-research
- autonomy: autonomous
- assumption: A GDS exported through Lunima's SiEPIC EBeam / openEBL path passes the run organiser's own verification with no edits made outside Lunima (H8's riskiest assumption: Lunima can be trusted near tape-out).
- hypotheses: H8
- budget: 0 USD, about 10 min maintainer time; the maintainer reads their own records and contacts no one
- context: `docs/ROADMAP.md` (rung 7) planned a maintainer submission to the openEBL 2026-10 run (deadline 2026-10-17). Discovery does not record whether it happened. If it did, it is the only external check so far of a Lunima workflow.

## Pre-registration
- success: the maintainer's records show that a Lunima-exported design went into the openEBL 2026-10 run. The organiser's verification reported zero errors or accepted the design, and nobody edited the GDS outside Lunima after export.
- failure: any one of these:
  - the organiser's verification reported at least one error that Lunima's pre-flight had passed
  - the design needed edits outside Lunima
  - the design was rejected
- not run: no submission was made. This is recorded as "not run" and counts as no evidence for or against. It also means Lunima's design-to-fab path has had no external check yet.
- measurement: the maintainer checks their submission records and the organiser's reply, then appends one dated line under Results with:
  - submitted (yes/no)
  - the organiser's verdict
  - the number and type of reported errors, and which of them Lunima's pre-flight also flagged
  - edits outside Lunima (yes/no)

  Nothing from private correspondence beyond the verdict goes into this public file.
- timebox: 2026-11-30

## Results
Append dated results here. Never edit the pre-registration; new criteria = new experiment.
