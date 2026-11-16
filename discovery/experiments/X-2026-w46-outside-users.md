# X-2026-w46-outside-users: Has anyone outside the maintainer's workplace used Lunima on their own work?
- status: proposed
- method: interview
- autonomy: needs-approval
- assumption: At least two people outside the maintainer's workplace have used Lunima on their own work, not a workshop exercise, in the past 90 days.
- hypotheses: H0, H1, H7, H9
- budget: 0 USD; maintainer time ≤ 15 min for the private list, plus at most 5 conversations of ≤ 30 min

Supersedes X-2026-w45-returning-testers (#1609). That pre-registration could end with neither
success nor failure (an all-colleague first three), so it is replaced rather than edited. If this
experiment is approved, #1609 can be closed without an answer. The interview guide is
`I:returning-testers` (#1610), updated this week.

## Pre-registration
- step 0 (private, nobody is contacted, needs no approval):
  - The maintainer writes a private list of everyone other than the maintainer and agents who is
    known to have opened Lunima at least once.
  - Each person is marked `workplace` (same employer or a direct collaborator of it) or
    `outside`.
  - The contact order for `outside` people is fixed on the list before anyone is contacted: most
    recent known use first.
  - Only counts are ever published.
- success: at least 2 `outside` people each show (screen-share or send) a dated artefact they
  made in Lunima for their own work within the 90 days before the conversation. Artefacts that
  count:
  - a `.lun` file
  - an exported GDS or netlist
  - a plot or figure used elsewhere

  Workshop exercises and shipped examples opened unchanged do not count. A described but unshown
  session does not count.
- failure: fewer than 2 `outside` people meet the success bar after every `outside` person on
  the list has been asked once (at most 5, in the fixed order). No reply within 10 days counts as
  "not shown". A list with fewer than 2 `outside` people is an immediate failure, with nobody
  contacted.
- measurement: the maintainer appends to Results:
  - the number of `outside` and `workplace` people on the list
  - the number asked, the number who replied, and the number of qualifying artefacts (type and
    month only)
  - links to anonymised `discovery/interviews/I-NNNN.md` summaries

  Artefacts are never committed. `workplace` conversations may happen, but they are recorded as
  exploratory and never count towards success. Which workflow people came back for (H1, H7, H9)
  is read as exploratory only; it is not scored.
- interpretation, fixed now:
  - Failure means Lunima has no outside user base yet. H0's riskiest assumption is then false as
    stated, and cold designer interviews (#1599, #1600) become the only route to E2.
  - Success gives the first E2 entries and the first candidates for a pilot (`discovery/pilot/`).
- timebox: 2026-12-11

## Results
