# DR-0001: Is "agent work you can watch, inspect and undo" still a candidate differentiator for Lunima?
- week: 2026-w46
- status: proposed
- decision: park
- hypotheses: H2
- evidence: E-2026-w46-04, E-2026-w46-05, E-2026-w46-06, E-2026-w46-07
- revisit: after the first three user conversations (X-2026-w46-outside-users or the designer interviews in #1596), or on 2027-01-31, whichever comes first

## Context
The internal review (`docs/strategy/agent-first-architecture-review.md`, 2026-11-01) concluded
that agent access alone is not a differentiator. It named one combination as Lunima's plausible
edge [H]: a local, open, visual environment where an agent's work can be *watched, inspected and
undone*. Since the review, the agent path has grown a headless stdio MCP server, a JSON command
line, a Claude Code plugin (`claude-plugin/lunima/`) and 73 tools (review Addendum). The review's
competitive note still says the GDSFactory+ MCP server has "no editing or routing".

No user has been asked about agent use. This week's scout evidence addresses the claimed edge
directly. This DR does **not** judge whether the maintainer should use agents for Lunima's own
development; the review records that this loop is useful to the maintainer.

## Options
### A: Do nothing
Keep H2 open and leave the review unchanged. New agent-bridge work continues to be justified by
the "watch, inspect, undo" edge.
- Cost: none now.
- Risk: a public strategy note keeps a competitive claim that this week's evidence contradicts,
  and further product investment rests on an edge a competitor has already shipped.

### B: Park H2 as a product differentiator; keep the bridge as maintainer infrastructure
- Existing tools are maintained; defects in them are fixed as usual.
- A new agent-bridge tool or feature is added only when it fixes a bug, or when its issue carries
  a `Failing task:` line naming a task the maintainer actually ran, the step that failed and the
  date. "Users or agents will want this" is not enough on its own.
- The review's competitive note is corrected (see Delivery).
- Discovery stops scouting H2 until the revisit trigger.

### C: Pursue an agent-first product direction
This would mean a headless core as product, the CLI as product, and agent-centred messaging.
- Not permitted: there is no E2/E3 evidence.
- It would compete with GDSFactory+ inside its own ecosystem and with Flexcompute's commercial
  loop.

### D: Drop H2 now
Too strong. All contrary evidence is E1 vendor or academic material. Nobody has been asked
whether they use agents for PIC work, so the user need itself is untested.

## Evidence
External evidence (all E1; no user words):
- **E-2026-w46-04 (E1)**, GDSFactory+ changelog. 1.6.4 (2026-03-17) shows a diff preview with an
  explicit confirmation step before agent edits are applied. 1.7.0 (2026-03-23) adds a
  verification mode that validates design decisions before committing. This is the "inspected
  and undone" part of the claimed edge, shipped in the dominant open-source ecosystem.
- **E-2026-w46-05 (E1)**, GDSFactory+ 2.0 launch (2026-09-03). It has an interactive layout where
  routes follow moved components, and an agent that edits component settings and connections.
  For the product as a whole, this contradicts the review's "no editing or routing". The scout
  could not re-verify gfp-mcp's own tool list.
- **E-2026-w46-06 (E1)**, a Flexcompute press release. It markets an agent loop with PDK access,
  layout and circuit simulation and "minimal human involvement". These are marketing claims, not
  independent results.
- **E-2026-w46-07 (E1)**, PhIDO paper. It reports about 57 % end-to-end pass@5 for designs of
  ≤ 15 components. Agent output at circuit level is not trustworthy unassisted. That argues for a
  human review surface, but E-2026-w46-04 shows a competitor already has one.

Agent hypotheses (E0, not proof):
- The one element no competitor obviously claims is undo of agent edits on a visual canvas,
  combined with gate-level and chiplet simulation. No user has asked for it.
- The bridge's demonstrated value so far is to the maintainer's development loop. That is a real
  benefit, but it is not demand evidence.

## Decision
**B: park.** The specific combination the internal review offered as Lunima's edge is no longer
unclaimed (E-2026-w46-04, -05), and the only evidence on agent reliability argues for review
surfaces that competitors already ship (E-2026-w46-07). With E1 only, the honest move is to stop
treating H2 as a reason to grow the product. The existing investment stays usable, and nothing is
deleted or frozen.

Revisit rules, fixed now:
- **Reopen H2** (status `open`) if any user conversation reports using an AI agent for PIC
  layout or simulation in the past 6 months, and describes how they checked its output.
- **Drop H2** as a product direction if none of the first three conversations reports such use.
- If no conversation has happened by 2027-01-31, H2 stays parked, not dropped: the absence of
  conversations is not evidence.

## Delivery
- [ ] Add a dated correction directly under the Addendum's "Competitive note" in
  `docs/strategy/agent-first-architecture-review.md`, and add a one-line pointer to it at the end
  of the executive-summary bullet that names "watched, inspected and undone" as the plausible
  edge.
  - acceptance: the correction says that GDSFactory+ ships an agent diff preview with explicit
    confirmation (1.6.4) and a verification mode (1.7.0), and that GDSFactory+ 2.0's agent edits
    component settings and connections. It cites https://doplaydo.github.io/gdsfactoryplus/changelog/
    and https://gdsfactory.com/news/introducing-gdsfactory-plus-2-0/. It states that gfp-mcp's own
    tool count was not re-verified. No other text in the review changes.
- [ ] Add a short "When to add a tool" paragraph to `docs/AGENT_BRIDGE.md` (under "Implementation
  notes").
  - acceptance: it says a new tool needs either a bug it fixes or a `Failing task:` line (the
    task, the failing step, the date) in its issue, and it links this DR. No tool, test or code
    changes.
