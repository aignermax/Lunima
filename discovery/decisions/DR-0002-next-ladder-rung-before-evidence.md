# DR-0002: Should the next ladder rung (a datapath wired on the chip) be built before any learner or educator evidence?
- week: 2026-w46
- status: proposed
- decision: need-more-evidence
- hypotheses: H7, H9, H0
- evidence: E-2026-w46-13, E-2026-w46-14
- revisit: when X-2026-w46-digital-photonic-courses reports (timebox 2026-11-27) and X-2026-w46-outside-users reports (timebox 2026-12-11); at the latest on 2026-12-11

## Context
`docs/ROADMAP.md` positions Lunima as "education first — NAND2TETRIS for photonics". Guiding
principle 3 says: "Never build more than the next rung. Users pull us to the rung after."

Rungs 4 and 5 have absorbed most recent development. The roadmap lists dozens of shipped
issue/PR pairs, from truth-table extraction through registers and RAM to an ISA that runs ALU,
data RAM and accumulator on light. About 30 logic examples ship in `examples/`. The next
north-star step is a datapath wired on the chip itself (ACC.Q → ALU.A instead of the emulator
driving the bus), and the roadmap marks it as a pending maintainer architecture decision.

Discovery has no record of any user pulling towards that rung:
- `discovery/pilot/FEEDBACK.md` is empty.
- No interview summary exists.
- The ladder-related issues in `.discovery/context/lunima-issues.md` are maintainer field
  reports or agent findings.
- Opportunity #1611 (`photonic-computing-course-ladder`) has had no test run yet.

This DR is about evidence and sequencing only. It does not judge the architecture of the
datapath.

## Options
### A: Do nothing
The maintainer decides the datapath on technical grounds whenever ready, as the roadmap says.
- Cost: none for discovery.
- Risk: the next rung is built on conviction while principle 3 is recorded as the rule, and the
  investment would be described as following user pull when none is on record.

### B: Hold the next rung until two cheap tests report
Hold the on-chip datapath and any new rung-5 or rung-6 capability until both experiments below
have reported (by 2026-12-11).
- X-2026-w46-digital-photonic-courses: autonomous, no maintainer time.
- X-2026-w46-outside-users: needs about 15 private minutes first.

During the hold, defects in examples that already ship stay fair game, because they serve every
audience and the demo path (for example #1166, router retry storm in the gate examples, and
#1155, Home example tiles not reacting).

### C: Re-aim the ladder at photonic-computing builders (H9) now
Not yet. H9 has no evidence either way. Changing the audience on an agent hypothesis would repeat
the problem this DR is about.

### D: Pursue the next rung as validated work
Not permitted: there is no E2/E3 evidence.

## Evidence
External evidence (E1 only):
- **E-2026-w46-13 (E1)**: a university online course (UBCx, via an aggregator copy) teaches PIC
  design with existing commercial and open tools plus real fabrication. General PIC-design
  teaching already has an established route, so H7's distinct slot would have to be the
  photonic-computing ladder itself.
- **E-2026-w46-14 (E1, weak)**: one doctoral-school course (EPFL) teaches optical computing as
  ML accelerators, through lectures and student presentations, with no stated software
  component. This is a single data point that academia may frame optical computing differently
  from a gate-to-computer ladder.

No external evidence supports H7 or H9.

Agent hypotheses (E0, not proof):
- If digital photonic computing is rarely taught hands-on, then course adoption would require
  educators to write new course content. That is a much higher bar than replacing a tool in an
  existing course.
- The ladder's gate-to-instruction stack may matter more to people building photonic processors
  (H9) than to courses. This is untested.

## Decision
**need-more-evidence: option B.** The two tests are cheap and report within four weeks. Their
results decide whom #1597 should talk to and whether the ladder's next rung has any pull on
record.

If the maintainer builds the datapath during the hold for reasons outside discovery (own
research, conviction), that is a legitimate choice. It should be recorded in the roadmap as a
conviction bet, not as user pull.

What the results would change, fixed now:
- **Courses test fails** (≤ 2 hands-on digital photonic-computing courses): H7 is weakened. #1597
  targets self-learners and workshop organisers instead of instructors. The next rung needs an
  E2 entry before discovery would call it user-pulled.
- **Courses test succeeds**: those courses' instructors become #1597's target profile, through
  maintainer-approved outreach only.
- **Outside-users test fails**: H0's riskiest assumption is false as stated. Any rung is then
  built for the maintainer's own vision and demo path, and should be described that way.

## Delivery
- none
