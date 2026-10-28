# Architecture hardening and responsibility extraction

## Intent and authorization

The user accepted the October 22 architecture audit and explicitly requested all
18 findings be fixed, the obsolete April architecture assessment removed, current
documentation reconciled, and every source file brought below 1,000 lines through
real responsibility separation rather than partial-class splitting. Preserve
existing functionality and verify the result. The active goal tracks completion;
finishing one phase does not complete it.

Baseline: origin/dev `b0230b63`, including the new GDS pass-through explanation and
journey regressions. Work is isolated on `refactor/architecture-hardening` in the
existing clean worktree. Preserve the original user's dirty checkout.

## Architectural decision

Keep Avalonia, CommunityToolkit.Mvvm, and Microsoft.Extensions.DependencyInjection.
Prism provides navigation, region composition and modular loading; none resolves
the demonstrated mutable-state, history, worker-lifetime or cache bugs on its own.
Use the existing DI composition root to inject focused services and child viewmodels.
Do not add a service locator, generic event bus, dependency bag, or framework migration
to conceal coupling. Reconsider Prism only if a concrete unmet navigation/module
requirement appears. This is an engineering choice, not a claim the user chose it.

Primary references checked October 23:
- https://docs.avaloniaui.net/docs/app-development/dependency-injection
- https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/ioc
- https://github.com/AvaloniaCommunity/Prism.Avalonia

## Target responsibilities

- Domain: geometry, components, pins, topology and numerical operations.
- Document/session: stable design ownership, active scope, revision, history and jobs.
- Application use cases: load/save, simulation, routing, export and process changes.
- Infrastructure: filesystem, Python/solver processes, updates and external tool transport.
- Presentation: bindings, dialogs, selection projections and rendering; no file-format
  DTO definitions in MainViewModel and no independent mutable authoritative topology.

Introduce these boundaries incrementally in existing projects. Extract concrete
collaborators first; interfaces belong at substitutable or independently testable
boundaries. Preserve serialized field names, IDs, unknown-field behavior, GDS geometry,
models, processes, undo semantics, UI commands and supported platforms.

## Non-negotiable completion conditions

1. All A01–A18 findings have a committed resolution and verification record; invalid
   premises discovered during implementation are documented with evidence instead.
2. Authored source and documentation, including C#, tests, AXAML, Python, shell,
   JS/TS, HTML/CSS, CI configuration and Markdown/RST/text documentation, have at
   most 999 physical lines per file. No added partial splits,
   minification, encoded source or deleted assertions to evade the rule. Existing
   generator-required partial declarations may remain, but new responsibilities
   must be separate collaborators. Data/fixtures/generated-artifact scope is pending
   the user's optional clarification; report these separately until resolved.
3. A staged-index-aware local pre-commit hook rejects violations. CI independently
   checks the whole tree. A fresh clone has documented setup; hooks are not security
   boundaries and cannot replace the required CI check. No final legacy allowlist.
4. Core behavior retains regression coverage. Tests exercise the released Avalonia
   runtime, command lifetimes, cancellation, persistence, import/export and UI flows.
5. Full feature-specific and architecture reviews find no unresolved material issues.
   Serial local .NET verification excludes Slow/Heavy unless specifically selected;
   full CI runs provide the broader coverage. Never claim unrun platform tests passed.
6. Documentation describes actual code and test commands. The right sidebar stays
   limited to selection properties. Remove the April analysis as requested.

## Audit resolution map

| ID | Required outcome |
|---|---|
| A01 | Undo side effects expire with their owning history entry, including redo/reset |
| A02 | Solver stdin/stdout/stderr/exit share cancellation, timeout and cleanup |
| A03 | Preview caching bounded and invalidated by effective environment generation |
| A04 | Simulation worker input isolated from live mutable UI/document state |
| A05 | Central coordinators decomposed into independently testable responsibilities |
| A06 | Explicit domain/application/infrastructure boundaries and narrow dependencies |
| A07 | Semantic dependency checks with negative tests and declared module coverage |
| A08 | Shared Avalonia runtime versions for production and headless verification |
| A09 | Whole-operation UI heartbeat checks, equivalent cold/warm fixtures |
| A10 | MCP accepted work tracked, cancellation propagated, stop drains mutations |
| A11 | Verified update artifact provenance and pinned/verified Python bootstrap |
| A12 | Reproducible safe checked-in test entry point and correct test docs |
| A13 | Current architecture map and valid agent instructions; obsolete report removed |
| A14 | Enforced source size and meaningful decomposition, no basename exemptions |
| A15 | Session/resource lifetime and repeated-load retention tests; temp cleanup |
| A16 | Single dependency supply, consistent roadmap/workflow and SDK policy |
| A17 | UI-independent current project contract and one explicit persistence workflow |
| A18 | Stable document/scope access instead of caller-owned root/group synchronization |

## Delivery and verification

## Builder-check decisions

- Revision is monotonically increasing for topology, geometry, process/model,
  parameter, source and computation-policy changes, including undo/redo and document
  replacement. Selection, pan/zoom and pure scope navigation do not invalidate domain
  results. Capture a coherent immutable computation input at the session's mutation
  boundary; apply on that boundary only when session identity and revision match.
  Tests must prove both stale rejection and valid-result application.
- Each operation has a session-owned lease. Closing admission invalidates leases
  before awaiting work; queued work fails before execution. UI mutations must check
  the lease at the actual commit, not merely when queued. MCP Stop cancels and awaits
  accepted tasks; a non-cooperative computation cannot retain mutation permission.
- History identity is the actual retained command object. Merge side effects belong
  to the retained merge target; reservation metadata belongs to its original command
  until commit. Cancel/reset retires reservations and must reject their late commits.
  Ordinary undo does not retire an entry while it remains redoable. Retire only on
  truncation, discarded redo, canceled reservation or reset; do not erase live peers.
- Preview cache uses LRU plus retained geometry weight: start with a 32 MiB estimated
  payload budget and a 200-entry ceiling; oversized entries are returned but not held.
  Weight counts coordinate arrays and retained strings, documents approximation, and
  never claims to be exact managed heap size. Environment identity covers resolved
  interpreter activation, renderer version and PDK/code revision. Completion from an
  obsolete generation cannot repopulate a new cache. Reuse an existing owner rather
  than stacking another cache if possible.
- Architectural direction: presentation -> application/session -> domain/contracts;
  infrastructure implements boundary contracts and must not reference presentation.
  Composition root may wire all layers. Existing projects can host these namespaces;
  folder moves alone are not proof. Declare actual namespace ownership and explicit
  transitional edges before enabling semantic gates; remove transitional exemptions
  for resolved findings at completion. No hidden broad Services shared-kernel escape.
- Artifact authenticity target: a versioned manifest (release version, source commit,
  platform/RID, filename, length, SHA-256) signed with an offline-generated ECDSA P-256
  publisher key; app pins public keys by key ID, allowing overlapping rotation.
  CI receives the private key as a protected secret, never a tracked file or log.
  Verify signature, asset bytes, platform and monotonic version before launch.
  Missing/invalid metadata must offer manual download rather than auto-executing an
  unsigned artifact. Establishing the protected signing secret is a final explicit
  operational step once implementation and local verification are reviewable; do not
  claim deployment complete without it. Pin uv version and per-platform SHA-256 in
  reviewed source using upstream release evidence; no mutable latest executable.

These are implementation decisions derived from the approved audit remediation,
not new product features. Independent builder review identified these six areas;
they are now explicit acceptance contracts.

Use small commits and independently reviewable phases. First fix deterministic
correctness defects, then guardrails/resources, then extract the current document
contract and state ownership before simplifying shell/input/export code. Capture
existing behavior before moving it. Do not remove a test just to shrink a file.
Use synthetic public fixtures for concurrency/large geometry; do not commit private
downloaded layouts or screenshots of them. Hook tests must cover the index rather
than just the working tree, deletion/rename, Unicode and filenames with spaces.

For each fix record baseline, reproducer, verification, changed responsibilities,
and remaining dependent work in the plan. No production release or claim of complete
behavioral equivalence until the combined branch passes its verification gates.
