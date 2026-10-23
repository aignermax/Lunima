# Architecture Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans for coordinated execution. Independent bounded tasks can use superpowers:dispatching-parallel-agents. Only the coordinator runs .NET commands; each phase receives independent review.

**Goal:** Resolve audit A01–A18, genuinely separate responsibilities, preserve functionality, and enforce fewer than 1,000 lines per authored source file.

**Architecture:** Keep Avalonia/MVVM Toolkit/MS DI. Establish controlled document/session state, immutable worker inputs, explicit operation lifetime, and focused persistence/application services. Extract responsibilities rather than hiding them in partial classes.

**Tech Stack:** C#/.NET 10, Avalonia, xUnit, Python tooling, Git hooks/GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-10-23-architecture-hardening-design.md`

## Global Constraints

- Baseline `b0230b63`; preserve current GDS review journeys and pass-through warnings.
- At most 999 physical lines per authored source file, including tests and AXAML.
- No partial-class-only split, minification, lost assertions, or dependency bags.
- Serialized formats, IDs, model/geometry semantics and existing UX remain compatible.
- Windows/Linux/macOS parity; retain process-launch abstraction and invariant formatting.
- Serialize all .NET runs; DOTNET_PROCESSOR_COUNT=2; use the checked-in safe test wrapper once introduced, external smart_test.py until then.
- Keep explicit A01–A18 status; an intermediate green batch is not overall completion.

## Review Focus

- History overflow, redo after undo, cancellation and history reset while jobs finish: phase 1 and phase 4 tests preserve only live entries and prevent late mutation.
- A solver that fills stdout before reading stdin, closes streams early, or spawns children: phase 1 tests enforce one deadline and process reaping.
- Environment switch during an in-flight preview: phase 2 tests forbid stale cache insertion and bound retained geometry.
- Save/export/undo with an open nested group while the root changes: phases 3–4 tests preserve scope, IDs and full document state.
- Large selected imported scenes and repeated project replacement: phases 2/5 tests measure full-operation heartbeat and release old owners, without sacrificing exact saved geometry.

## Phase 1 — Correctness and trustworthy constraints

### Task 1: History-owned crossing metadata (A01)

Files: `CAP.Avalonia/Commands/CommandManager.cs`, `CAP.Avalonia/ViewModels/Canvas/CrossingInsertion/CrossingUndoJournal.cs`, focused tests under `UnitTests/Commands/` and existing crossing test area.

Interface: add a history-entry retirement notification carrying `IUndoableCommand`; trimming the oldest entry, dropping redo, canceling an uncommitted reservation and clearing history must have defined ownership semantics. Journal removes only retired entry metadata; generation still rejects late worker changes.

- [ ] Add regression for 130 nonmergeable commands with crossing metadata; undo retained entries and compare exact topology/order.
- [ ] Coordinator runs focused wrapper test and observes expected failure.
- [ ] Implement precise retirement; test undo/redo/merged/pending/reset cases.
- [ ] Run related routing/history regressions, review diff, fix findings, commit.

### Task 2: Bounded subprocess lifecycle (A02)

Files: `CAP.Avalonia/Services/Solvers/SubprocessJsonRunner.cs`, tests under `UnitTests/Solvers/`.

Preserve `RunAsync(ProcessStartInfo, string, TimeSpan, CancellationToken, Action<string>?)` and its result contract. Start readers before writing; linked cancellation covers writing, stream collection and process exit; kill/reap on failure or cancellation.

- [ ] Add controlled child-process tests for blocked input, early pipe closure and cancellation.
- [ ] Observe failing baseline through coordinator, implement, run solver/process tests.
- [ ] Independently review platform and resource behavior, commit.

### Task 3: Enforced source ceiling and current documentation (A12–A14)

Files: new `scripts/check_source_size.py`, `.githooks/pre-commit`, hook setup script, CI guard workflow, Python tool tests; `UnitTests/Architecture/FileSizeLimitTests.cs`, `docs/CODEBASE_MAP.md`, `docs/TESTING.md`, `CLAUDE.md`, `ARCHITECTURE.md`, `CONTRIBUTING.md`, `.claude/CLAUDE.md`; remove `docs/ARCHITECTURE_ANALYSIS.md` and obsolete migration claims.

Interface: size checker supports whole-tree and `--staged`; nonzero exit lists path and count for >=1000 lines. Read staged blobs using git, never unstaged content. During migration the whole-tree check reports unresolved inventory; final gate has no legacy exemption.

- [ ] Inventory all tracked over-limit sources and separately report non-source data.
- [ ] Test 999/1000 boundary, index/worktree mismatch, deleted/renamed files and spaces.
- [ ] Implement hook/checker and documented opt-in installation preserving existing hooks.
- [ ] Add safe serial test entry point with Slow/Heavy exclusions; verify docs commands against real runner.
- [ ] Correct docs and delete requested obsolete assessment; review and commit.

## Phase 2 — Resource ownership and reliable verification

### Task 4: Preview cache lifetime and invalidation (A03, A15)

Files: `Connect-A-Pic-Core/Export/NazcaComponentPreviewService.cs`, `CAP.Avalonia/Controls/Canvas/ComponentPreview/`, `CAP.Avalonia/DI/PdkOffsetFeatureExtensions.cs`, preview tests.

- [ ] Specify one bounded geometry cache owner and environment-generation identity; preserve existing public render APIs through adapters.
- [ ] Test retained capacity and environment change during in-flight rendering; observe failing baseline.
- [ ] Implement and test exact existing preview/PDK behavior and repeated variants; review, commit.

### Task 5: Runtime and performance test integrity (A08, A09, A15)

Files: package/project properties, `UnitTests/UnitTests.csproj`, `UiThreadWatchdog.cs`, responsiveness/repeated-load/lifetime tests.

- [ ] Centralize matching Avalonia package versions and verify resolved dependency graph.
- [ ] Add a yielding-then-blocking UI command probe that fails prefix-only measurement.
- [ ] Measure dispatcher heartbeat across completion; separate warm-up and fresh equivalent measured states.
- [ ] Add old-session weak-reference and pending-job retention coverage; build/test/review.

### Task 6: MCP lifecycle and verified external artifacts (A10, A11, A15, A16)

Files: AgentBridge transport/dispatcher/catalog/tool interfaces and implementations; update downloader/installer/checker; `UvBootstrapper`; their tests and release workflow.

- [ ] Thread cancellation through tool invocation; track/drain accepted work and serialize mutation admission.
- [ ] Regression proves Stop prevents late design mutation and handles concurrent request cancellation.
- [ ] Define and test authenticated artifact metadata/verification policy; pin and verify bootstrap bytes; preserve upgrade compatibility and rollback.
- [ ] Clean owned temp files on failure/declined install; verify platform paths and quoting.
- [ ] Remove duplicate MathNet binary only after build/publish output comparison; align SDK/roadmap docs.
- [ ] Independent lifecycle/security review; commit verified changes separately.

## Phase 3 — UI-independent document and persistence

### Task 7: Current document contract and load/save use cases (A05, A06, A17)

Files: DTOs currently in `MainViewModel.cs`, `FileOperationsViewModel.cs`, persistence services and tests. New contract/mapping files live with persistence/application responsibilities rather than presentation.

- [ ] Characterize roundtrips for root/nested groups, routes/crossings, process/material/model state, unknown legacy fields and IDs.
- [ ] Move DTO ownership without changing JSON names; extract serialization and validation from dialog/UI orchestration.
- [ ] Classify and retire/adapt the older persistence path with explicit compatibility tests.
- [ ] Reduce FileOperations to focused presentation/use-case dispatch; no partial-only decomposition.
- [ ] Run example/load/save/GDS regressions and review public format behavior before commit.

### Task 8: Stable session/scope and simulation snapshots (A04, A06, A18)

Files: `DesignCanvasViewModel`, `GroupEditService`, `ConnectionSync`, `SimulationService`, core connection/state APIs and focused new session/snapshot services.

- [ ] Introduce one explicit root/scope accessor and revisioned operation ownership while preserving bindings.
- [ ] Capture worker input independently of live VM/component/manager mutation; apply only matching-revision results.
- [ ] Test edit/load/group-switch during paused simulation and routing; stale work cannot mutate a replacement design.
- [ ] Move authoritative writes behind session operations; view collections become projections with a single synchronization owner.
- [ ] Run selection/group/undo/export/simulation roundtrips, review and commit.

## Phase 4 — Genuine decomposition and architectural gates

### Task 9: Focused shell, input, window and export collaborators (A05, A06, A14)

Files: `MainViewModel.cs`, `CanvasInteractionViewModel.cs`, `MainWindow.axaml.cs`, `SimpleNazcaExporter.cs` and newly focused responsibilities; existing partial type families reviewed as aggregates.

- [ ] Use the tracked inventory to extract one responsibility at a time with behavior tests.
- [ ] Move shell composition into feature coordinators with explicit lifecycle, not a giant constructor bag.
- [ ] Split UI code-behind by actual view/control ownership, input by gesture/use-case, export by intermediate model/emission/process boundary.
- [ ] Preserve public commands/bindings or migrate callers with equivalence tests; verify all remaining authored source types below ceiling.
- [ ] Review aggregate type coupling and independently testable responsibility boundaries.

### Task 10: Semantic restrictions and remaining oversized tests/scripts (A07, A14)

Files: architecture tests/checker, explicit module policy, every remaining path in the source inventory.

- [ ] Test forbidden alias/static/global/fully-qualified dependencies and unregistered feature handling.
- [ ] Implement semantic dependency validation with explicit current module coverage and narrow justified shared contracts.
- [ ] Split oversized tests by behavior with shared fixtures; split scripts by independently meaningful modules, preserving entrypoints.
- [ ] Require zero whole-tree size violations; no final baselines/exemptions masking authored source.

## Phase 5 — Combined verification and final review

- [ ] Record resolution/evidence for every A01–A18 item and the source inventory.
- [ ] Build and run appropriately scoped local tests serially; run full CI including Heavy and platform-specific smoke checks.
- [ ] Independently review complete diff for regressions and responsibility boundaries; fix findings and reverify affected paths.
- [ ] Check docs/quickstarts on clean state, staged hook and CI checker, resolved package graphs and publish outputs.
- [ ] Prepare reviewable PR(s); report exact tests and limitations. Mark goal complete only after all required acceptance conditions hold.

## Execution log

- 2026-10-23: Created active goal; inspected current dev changes; selected clean existing worktree and branch. Source/data ceiling clarification pending. No findings marked fixed yet.
