# DR-0001: Keep growing Lunima's agent tool surface as a product bet, or hold until it is measured?
- week: 2026-w45
- status: proposed
- decision: experiment
- hypotheses: H2, H4
- evidence: E-2026-w45-01, E-2026-w45-07, E-2026-w45-08, E-2026-w45-09
- revisit: when spike `phido-testbench-coverage` reports, or when the first interview summary mentions agent use, whichever comes first; at the latest 2026-12-07

## Context
This decision is narrow. It does not ask whether the agent bridge stays. The bridge serves the
maintainer's own development loop (`docs/strategy/agent-first-architecture-review.md`, §7), and
tools that loop needs keep coming. The question is whether to keep building agent tools **because
external users will want them**, when no user has said so.

- Since 2026-11-01 Lunima has a headless agent path: a stdio MCP server, a JSON CLI, a Claude Code
  plugin and 73 tools covering authoring, parameters, simulation, validation, export and GDS
  import (`docs/AGENT_BRIDGE.md`; review addendum). Agent features are still landing: the design
  batch (#1604) and detector outputs (#1605) were merged this week.
- The review proposed a five-task experiment with three runs each. One task was run once (an MZI
  built and checked in 23 tool turns). Its §10 questions have no answers, for example "Is the
  visible, undoable, watch-the-agent workflow valued, or tolerated?"
- This week's external evidence (all E1) says two things: the agent-PIC space is occupied, and
  agent output on multi-component circuits is still unreliable.

## Options
### A: Do nothing — keep adding agent tools whenever they look useful
- For: the marginal build cost is low because agents write the tools. The tools double as an E2E
  test harness and speed up the maintainer's own example generation and repair.
- Against: each tool adds review, docs, tests and GUI-parity upkeep for one part-time maintainer.
  External pull is unmeasured, while a free vendor tool (E-2026-w45-01) and gfp-mcp
  (E-2026-w45-07) already reach the same users. There is no stop rule, and maintainer attention
  goes into PR review instead of the user conversations the Q1 outcome asks for.

### B: Hold product-motivated expansion and measure first
- Agent work continues when it fixes a defect, serves the maintainer's own workflow, or is needed
  for the measurement. New tools justified only by expected external demand wait.
- Measure the one claim competitors do not obviously make: that Lunima's checking tools
  (`validate_design`, `simulate`, `compare_netlist`) make an agent's output more reliable. The
  cheapest way is to find out first whether an external benchmark can serve as the baseline
  (spike `phido-testbench-coverage`, no model spend).
- Ask about agent use as past behaviour in the conversations already planned. No pitching.
- For: costs little, keeps the bridge healthy, and produces the first number that could support
  or kill the remaining H2 claim.
- Against: some useful tools may arrive a few weeks later. A benchmark is still E0 and cannot show
  demand.

### C: Double down on "the agent workspace you can watch and undo"
- More tools, plugin promotion, a landing page positioned against PhotonForge and GDSFactory+.
- Against: this needs E2 evidence that designers value visibility and undo over the alternatives,
  and none exists. `pursue` is not allowed on E1 only.

### D: Reduce the agent surface to maintenance only and drop it from positioning
- Against: there is no evidence against user value either. It would cut the maintainer's own
  productive loop. If B's measurements come back negative, D can still be chosen later at no
  extra cost.

## Evidence
External (all E1; no E2/E3 exists for this question):
- E-2026-w45-01 (E1): PhotonForge 2.0, free since 2026-10-15: "Engineers and agents work on the
  same design, from schematic and layout through circuit simulation"; agents connect "through the
  Model Context Protocol (MCP) or a Python API". This is a vendor press release; nobody has
  checked it hands-on.
- E-2026-w45-07 (E1): gfp-mcp lets Claude build components and run verification checks for
  GDSFactory+. Nine releases between 2026-01-23 and 2026-02-23, none after, so how active it is now
  is unclear.
- E-2026-w45-08 (E1): PhIDO. The best 2025 models reach ~57 % end-to-end pass@5 on circuits of
  ≤ 15 components, and up to 91 % on single devices. From the strategist's reading of the paper's
  full text (https://arxiv.org/html/2508.14123, 2026-11-02): success was judged by manual review as
  "a valid schematic and GDSII layout", not fabrication sign-off, and the 102-prompt testbench is
  published in the authors' GitHub repository with no software licence stated.
- E-2026-w45-09 (E1): PICopilot. Researchers name manual scripting as the bottleneck and answer it
  with LLM-generated scripts. This supports the problem, but the remedy competes with Lunima's.

Internal facts (E0, not demand evidence):
- The headless path and the 73 tools: `docs/AGENT_BRIDGE.md`, review addendum.
- The one real-agent run: review addendum (one task, one run).

Agent hypotheses (not proof):
- The strategist's reading: neither E-2026-w45-01 nor E-2026-w45-07 claims that the environment
  checks the agent's work where a human can see and undo it. Whether that raises agent reliability
  can be measured. Whether users value it is unknown.
- E-2026-w45-08's ~43 % failure rate cuts both ways. It is a reason not to trust agents, or a
  reason to want an environment that catches their mistakes. This week's evidence cannot tell
  which.

## Decision
**experiment: option B.** Until the revisit:

1. Agent-bridge work goes ahead only if it meets one of three exemptions:
   - it fixes a defect in an existing tool;
   - the maintainer's own development or example generation needs it;
   - the spike, or a benchmark registered after it, needs it.
2. New agent tools justified only by "external users will want this" are parked with a link to
   this DR.
3. The spike `phido-testbench-coverage` runs this week with no model spend. Its rule is fixed now:
   - ≥ 60 % of the testbench's ≤ 15-component prompts can be built from component types Lunima
     ships, *and* the repository's terms allow reuse → a comparative benchmark gets registered
     next week, with its pass/fail criteria fixed before any run;
   - otherwise H2's reliability claim stays untested and this hold continues.
4. Agent use is asked about as past behaviour in X-2026-w45-returning-testers and in the designer
   interviews (#1596).

What the revisit DR would need:
- **Option C:** at least one E2 entry (two are better) in which a PIC designer describes a recent
  attempt to use an agent and names checking its output as the problem, plus a comparative
  benchmark in Lunima's favour.
- **Option D:** the spike finds no usable baseline, *and* none of the first conversations reports
  using an agent for PIC work.

## Delivery
- [ ] Every agent-bridge issue opened from 2026-11-02 until the revisit names the exemption it
  falls under (defect / maintainer workflow / benchmark need). Acceptance: each such issue states
  its exemption in the body, and issues without one are parked with a link to DR-0001.

## Critic
- verdict: weaken
- strongest counter-argument: The hold is right, but the measurement track rests on a claim
  competitors already make, and on a factual error. Option B calls "Lunima's checking tools make an
  agent's output more reliable" the one claim competitors do not obviously make. The gfp-mcp package
  cited as E-2026-w45-07 lists `check_drc`, `check_connectivity`, `check_lvs` and
  `simulate_component` as agent tools ("Run Design Rule Check verification with structured
  violation reports"). GDSFactory+ is described as "a graphical interface for chip design … built
  on top of GDSFactory and VSCode". The free PhotonForge Community edition includes DRC and LVS, and
  "engineers and agents work on the same design" in a local GUI. PICopilot (E-2026-w45-09)
  describes "a multi-agent architecture with a feedback mechanism". So a benchmark showing that
  checking tools help an agent would hold for competitors too and would not set Lunima apart. Only
  undo is left, and nobody has asked for it. The Evidence section's statement that the PhIDO
  testbench has "no software licence stated" is wrong for the repository: its sidebar, its LICENSE
  file and its README ("This project is licensed under the MIT License") all state MIT. Only the
  paper page states no licence. Half of the spike's go-rule is therefore already answered. The
  ≤ 15-component prompts (MZI, MMI cascades, MZI/MMI demux, built on a generic 34-PCell gdsfactory
  PDK) map onto generic types Lunima ships, so the 60 % gate will very likely pass. That leads
  straight to a benchmark with model spend and manual grading that nobody has costed. Published
  2025 PhIDO numbers also cannot be a baseline for 2026 models in a different pipeline. The only
  valid design is a within-Lunima ablation (checking tools on vs off, same model, same prompts),
  which needs the prompts but no spike. Finally, exemption 2 ("the maintainer's own development or
  example generation needs it") is self-certified and wide enough to cover almost any tool. Unless
  each issue names the concrete maintainer task it unblocks, the hold will not bind.
- alternative explanation: The 73 tools and the one successful MZI run reflect the maintainer's
  own development loop (as Context already says), not external pull. PhIDO's ~43 % failure rate
  (≤ 15 components, 2025 models, generic PDK, judged by manual review as "structural validity,
  rather than true fabrication sign-off") says nothing about whether PIC designers want an agent in
  their editor at all. They might reject agents outright, or use them only for scripting, which is
  PICopilot's path.
- already solved elsewhere: agent + verification tools: gfp-mcp for GDSFactory+
  (https://pypi.org/project/gfp-mcp/; GDSFactory+ described at https://pypi.org/project/gdsfactory/),
  and PhotonForge 2.0 Community, which offers MCP agents plus DRC/LVS plus a local GUI
  (https://flexcompute.com/news/photonforge-2). Undo of agent edits as a stated feature: not found.
- what would change my mind: An E2 entry in which a PIC designer describes a recent agent attempt
  (gfp-mcp, PhotonForge or plain LLM scripting) that failed because they could not see or roll back
  what the agent changed. In that case, register a within-Lunima ablation on the MIT-licensed
  PhIDO prompts directly, with grading rules, the grader and a model budget fixed in advance, and
  skip the spike. Until then: keep the option B hold, drop the spike, require exemption 2 to name
  the maintainer task, and replace the option D trigger with "no conversation by 2026-12-07 reports
  using an agent for PIC work".
