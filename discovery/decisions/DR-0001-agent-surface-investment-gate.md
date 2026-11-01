# DR-0001: Should Lunima build more agent tooling now, beyond the shipped bridge and headless host?
- week: 2026-w44
- status: proposed
- decision: need-more-evidence
- hypotheses: H2, H9
- evidence: E-2026-w44-01, E-2026-w44-02, E-2026-w44-03, E-2026-w44-04, E-2026-w44-05, E-2026-w44-06
- revisit: when X-2026-w44-designer-interviews reaches its timebox (2026-12-13) or reports 3 interviews, whichever comes first

## Context
The decision is due now because agent work is being built ahead of evidence:
- Lunima already has an in-app MCP agent bridge (`docs/AGENT_BRIDGE.md`), and since #1591 a headless stdio MCP server and JSON CLI (`CAP.Desktop mcp` / `agent`).
- The maintainer's internal architecture review (2026-11-01) proposes four or five more thin adapters: `get_simulation_result`, `set_component_parameter`, `validate_design`, `place_component` rotation/explicit pins, and `open_design`/`export_nazca`. They would serve a 5-task agent benchmark.
- The same review advised against a headless layer before the hypothesis was tested, and #1591 shipped one anyway.

The open question is whether agent capability gets the next block of maintainer time before any designer has said they would use it. Without a gate, the next adapter will always look cheap.

## Options
### A: Do nothing
Keep what has shipped (bridge, headless host) and add no new agent tools. Run no benchmark and gather no evidence on purpose. Cost: zero. Risk: H2/H9 stays undecided indefinitely. Shipped agent features keep drawing maintenance without anyone knowing whether they matter.

### B: Build the review's adapters now and run its 5-task benchmark
A few evenings of adapter work, then 15 benchmark runs. This produces **internal-test (E0)** evidence on whether an agent *can* do the tasks through Lunima. It produces no evidence that a designer *wants* this, and the E1 evidence shows competitors already sell it (E-2026-w44-01, -02). It is the most expensive way to learn the least about demand.

### C: Gate new agent tooling on designer evidence; fix correctness only
Add no new agent capabilities until X-2026-w44-designer-interviews reports. Fix only correctness defects in what has shipped, so agents cannot mistake a failure for success. If at least 2 of 3 interviewees describe past use of, or a concrete need for, inspecting or correcting agent- or colleague-generated layouts, reopen option B scoped to that workflow (H9 or H2).

## Evidence
External (all E1, public and indirect):
- E-2026-w44-01 (E1): gfp-mcp exposes build_cells, check_drc, check_lvs and simulate_component to agents. "Agent access" is already available in the leading open ecosystem.
- E-2026-w44-02 (E1): Flexcompute PhotonForge sells an agent loop with PDK, layout and circuit simulation, positioned as running "without human-in-the-loop".
- E-2026-w44-03 (E1, weak, for): the same vendor teaches "how to keep an engineer in the loop" and "how to verify what the AI produces". Inspection is a real concern, and a competitor already addresses it.
- E-2026-w44-04 (E1, for): the PhIDO authors say manual PIC design takes "minutes to several days". This is the motivation for automation, not measured demand.
- E-2026-w44-05 (E1, against): agent performance drops noticeably at higher circuit complexity. Output trust is the riskiest assumption and is unresolved.
- E-2026-w44-06 (E1, against): another academic agent framework. The space is crowded.

Agent hypotheses (not proof):
- E-2026-w44-14 (E0): the scout's synthesis that agent access, simulation and DRC are each offered by a competitor.
- The internal review's capability matrix and its "watch the agent, press Ctrl+Z" thesis. These are an engineering assessment ([A]/[H] tags in the review), not demand evidence.

Verified in the repository for the Delivery item: `CAP.Avalonia/Services/AgentBridge/Tools/AiToolAdapter.cs:29-34` wraps every assistant-tool result as `AgentToolResult.Text(...)`. Its own doc comment says failures are reported "in their returned text", so an MCP client sees them as successful calls.

## Decision
**C: need-more-evidence.** No new agent capabilities until the designer interviews report. All six external sources are E1, and four of them weaken H2 as a differentiator. Building option B now would mostly confirm what the maintainer already believes (agents can drive Lunima) and would not test what is unknown (whether designers want that, here, rather than in gfp-mcp or PhotonForge). The one exception is a correctness defect: an agent can misread a failed assistant-tool call as success. That makes everything already shipped less trustworthy, whatever the strategy turns out to be.

This decision does not ask to remove or deprecate the bridge or the headless host.

## Delivery
- [ ] Assistant-tool failures reached through the agent bridge return an MCP tool error (`isError: true`) with a reason instead of success-shaped text (`AiToolAdapter`) — acceptance: a bridge test calls an assistant tool that fails (e.g. `place_component` with an unknown type) over the bridge and asserts `isError: true` plus a non-empty reason; a successful call still returns `isError` false; existing `UnitTests/AgentBridge/` tests stay green; no new tools are added.
