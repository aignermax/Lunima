# DR-0001: Which discovery work deserves maintainer attention next?
- week: 2026-w48
- status: proposed
- decision: need-more-evidence
- hypotheses: H0, H1, H2, H3, H4, H5, H6, H7, H8
- evidence: E-2026-w48-01, E-2026-w48-02, E-2026-w48-03, E-2026-w48-04
- revisit: 2026-11-30, or an explicit response to #1599 or a documented productive-use account

## Context

The Q1 outcome is an externally valuable engineering workflow. The supplied PO notes put productive use before better designs than manual coding; AI superiority remains a hypothesis. Four indexed E1 entries and unavailable analytics cannot decide a pivot. Eight overlapping human-facing items should not become eight new asks. No approval label is present in the supplied issue snapshot; nothing here authorizes contact. No declined label is shown, and any later decline must be respected.

The supplied CLAUDE.md favors physical correctness, architectural discipline and complete workflows. Full persona, roadmap, bridge and internal assessment documents are unavailable in this snapshot, so their detailed claims and five candidate technical tasks have not been assessed. Candidate demonstrations such as MZI simulation, routing repair or chiplet validation are feasibility probes, not demand evidence.

## Options
### A: Do nothing
Build nothing new and leave the discovery queue unchanged. This avoids new work but preserves duplicate review burden and leaves the Q1 learning gap unresolved. It remains the fallback if the maintainer declines interviews.

### B: Consolidate existing evidence requests and prepare one task-based pilot
Use #1599 as the entry decision for the existing designer interview scope, with #1595/#1596 as experiment and method references. Use #1609/#1610/#1620 as one productive-use evidence bundle; #1646 is an overlapping public-record lookup, not another demand test. Prepare a pilot only after an actual task is identified. This is the recommended next step; it does not authorize a pilot or widen existing interview consent.

### C: Start an AI-first build or a new acquisition campaign
Potentially visible progress, but no direct demand evidence justifies the opportunity cost. Research automation alternatives also weaken a generic differentiation claim. No build, campaign or public page is recommended.

## Evidence

External evidence:
- E-2026-w48-01 (E1): research authors describe placement constraints and an automated alternative; not an observed Lunima workflow.
- E-2026-w48-02 (E1): schematic-driven tooling may leave manual routing burden; the paper also proposes a router. This weakens GUI sufficiency, not all visual inspection.
- E-2026-w48-03 (E1): joint optical/electrical routing constraints suggest a specific technical problem, with an existing proposed solution. Entries 01–03 have overlapping authors.
- E-2026-w48-04 (E1): Nazca's source traceability and inspection features weaken an exclusive visual-versus-code advantage.

Internal context and agent hypotheses, separately:
- README.md's capability descriptions and installed-release findings in the issue snapshot are E0 context, not demand validation.
- The issue summaries #816–819 are leads to workshop feedback; recover provenance before adding indexed E2 evidence. Do not erase that lead by declaring that no external feedback exists.
- Agent hypothesis: verified engineering work, including review and repair effort, is a better decision unit than feature count or demo completion.
- Agent hypothesis: existing interviews can identify a suitable comparison task; no participant, commitment, approval or productive use is assumed.

## Decision

Choose B as evidence preparation, with no strategic implementation commitment. Register zero new experiments this week: original protocols for existing experiments are missing from the snapshot, so duplicating or silently rewriting them would reduce learning quality. No experiment results are asserted or appended.

Maintainer decision bundle 1: approve or decline #1599's existing three-designer interview scope; #1595/#1596 describe overlapping methodology, not extra conversations. Include recent workflow and verification questions only within that scope. A later pilot is a separate approval after its exact task and protocol are reviewable. #1600's broader outreach remains deferred; no contact drafts or new approval issue are added.

Maintainer decision bundle 2: resolve #1609/#1620 together, using #1610 as the interview guide. First record any already-known use without contacting anyone. Public-footprint work #1646 is a supporting lookup; no observable trace means unknown use, not no users and not H0 failure. Preserve each original pre-registration and record their overlap before any administrative consolidation. #1597's education interview stays deferred, with #1611 proposed for parking pending an attributable problem statement and relevant indexed evidence; #1619 supplies context, not another urgent decision.

Keep gds-foreign-layout-import closed. Park first-foundry-submission rather than treating the prior proposed opening as an accepted opportunity. The critic's objection stands: foundry service iterations do not isolate avoidable user friction, and a repeated script pass cannot validate physical performance. Preserve #1645's original registration; when its records are available, append actual verification attempts, re-exports, provenance and failures under Results without changing its success rule. Missing records are not success. A maintainer's own submission remains feasibility evidence. Independent measured spectra versus frozen predictions would be useful later, but no device/data availability or justified tolerance is supplied, so do not register a measurement experiment yet.

Do not repeat the installed-release spike. Its supplied findings already identify failures. Existing release smoke is partial coverage, and headless checks cannot establish GUI usability or real-machine dependency setup. Route remaining readiness checks through the existing delivery work. The course-ladder opportunity has no indexed adoption support; quantum-course examples and absence of digital courses do not establish a market gap, and physical-model limitations remain a teaching risk.

### Conditional pilot proposal for the Product Owner

This is a preparation brief, not a registered experiment or permission to contact someone. Reuse the existing designer interviews to select one recent, real engineering task from a precision-layout or system-simulation workflow. Compare two genuinely different workflows: the designer's current manual-code toolchain and Lunima's agent-assisted workflow with human inspection. A shipped MZI example is not a substitute for a user-selected task. Select the smallest useful subtask that can exercise requirements, candidate generation, simulation/checking, optimization where relevant, and independently checked export.

Before proposing execution, freeze the PDK and version, specifications, starting artifacts, model assumptions, machine, time and compute ceilings, available tools, and permitted human assistance for both arms. Use the same requirements and resource ceilings. Record any task familiarity or order effect; one case cannot prove general superiority. Use an existing reference simulator/checker or an independent engineer's review, not only Lunima's own verdict. If independence is unavailable, label the result feasibility only.

The primary question is whether the designer can use the verified output in their real task and later chooses to reuse it. Separately measure total human time (setup, prompts, manual edits, review and repairs), wall time, compute consumption/cost, failed attempts and design quality against the same requirements. For optical results, freeze task-appropriate metrics, reference data and numerical tolerances before running either arm. Do not trade worse correctness for faster completion. Better design quality and lower human effort are separate claims; tie, regression, invalid output or no reuse must remain possible outcomes.

Limit preparation to 30 minutes of maintainer review and 0 USD; stop if no real task, independent check or affordable equal budget can be specified. Before execution, present a separately approved, pre-registered pilot with numerical success/failure thresholds, measurement ownership and a deadline. No paid compute, recruitment, new dependencies or private design transfer is authorized. Keep user designs private; public summaries require consent.

## Delivery
- [ ] Consolidate the local decision brief for the two existing request bundles — acceptance: #1599/#1595/#1596 and #1609/#1610/#1620/#1646 have explicit scope and overlap mappings; original protocols and labels are preserved; #1597/#1600 are marked deferred, not approved.
- [ ] Recover original experiment registrations and missing product-context documents before preparing execution — acceptance: available sources are identified, gaps remain explicit, and no frozen pre-registration is rewritten or reconstructed from an issue title.
- [ ] After approved interviews identify a task, prepare the matched pilot protocol described above — acceptance: both workflows, identical PDK/specifications/resource caps, independent verification, numerical thresholds, failure cases, human-time accounting and reuse observation are concrete; otherwise record no feasible pilot and build nothing.
- [ ] Link pilot readiness to existing release work — acceptance: remaining import/export and clean-machine GUI checks are identified against existing coverage and known bugs; no duplicate discovery spike or claim of outsider success is created.
