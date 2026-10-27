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

The phase 1–4 lists below describe the original implementation sequence.
The A01–A18 status matrix and dated execution records track actual completion.

## Phase 1 — Correctness and trustworthy constraints

### Task 1: History-owned crossing metadata (A01)

Files: `CAP.Avalonia/Commands/CommandManager.cs`, `CAP.Avalonia/ViewModels/Canvas/CrossingInsertion/CrossingUndoJournal.cs`, focused tests under `UnitTests/Commands/` and existing crossing test area.

Interface: add a history-entry retirement notification carrying `IUndoableCommand`; trimming the oldest entry, dropping redo, canceling an uncommitted reservation and clearing history must have defined ownership semantics. Journal removes only retired entry metadata; generation still rejects late worker changes.

- Add regression for 130 nonmergeable commands with crossing metadata; undo retained entries and compare exact topology/order.
- Coordinator runs focused wrapper test and observes expected failure.
- Implement precise retirement; test undo/redo/merged/pending/reset cases.
- Run related routing/history regressions, review diff, fix findings, commit.

### Task 2: Bounded subprocess lifecycle (A02)

Files: `CAP.Avalonia/Services/Solvers/SubprocessJsonRunner.cs`, tests under `UnitTests/Solvers/`.

Preserve `RunAsync(ProcessStartInfo, string, TimeSpan, CancellationToken, Action<string>?)` and its result contract. Start readers before writing; linked cancellation covers writing, stream collection and process exit; kill/reap on failure or cancellation.

- Add controlled child-process tests for blocked input, early pipe closure and cancellation.
- Observe failing baseline through coordinator, implement, run solver/process tests.
- Independently review platform and resource behavior, commit.

### Task 3: Enforced source ceiling and current documentation (A12–A14)

Files: new `scripts/check_source_size.py`, `.githooks/pre-commit`, hook setup script, CI guard workflow, Python tool tests; `UnitTests/Architecture/FileSizeLimitTests.cs`, `docs/CODEBASE_MAP.md`, `docs/TESTING.md`, `CLAUDE.md`, `ARCHITECTURE.md`, `CONTRIBUTING.md`, `.claude/CLAUDE.md`; remove `docs/ARCHITECTURE_ANALYSIS.md` and obsolete migration claims.

Interface: size checker supports whole-tree and `--staged`; nonzero exit lists path and count for >=1000 lines. Read staged blobs using git, never unstaged content. During migration the whole-tree check reports unresolved inventory; final gate has no legacy exemption.

- Inventory all tracked over-limit sources and separately report non-source data.
- Test 999/1000 boundary, index/worktree mismatch, deleted/renamed files and spaces.
- Implement hook/checker and documented opt-in installation preserving existing hooks.
- Add safe serial test entry point with Slow/Heavy exclusions; verify docs commands against real runner.
- Correct docs and delete requested obsolete assessment; review and commit.

## Phase 2 — Resource ownership and reliable verification

### Task 4: Preview cache lifetime and invalidation (A03, A15)

Files: `Connect-A-Pic-Core/Export/NazcaComponentPreviewService.cs`, `CAP.Avalonia/Controls/Canvas/ComponentPreview/`, `CAP.Avalonia/DI/PdkOffsetFeatureExtensions.cs`, preview tests.

- Specify one bounded geometry cache owner and environment-generation identity; preserve existing public render APIs through adapters.
- Test retained capacity and environment change during in-flight rendering; observe failing baseline.
- Implement and test exact existing preview/PDK behavior and repeated variants; review, commit.

### Task 5: Runtime and performance test integrity (A08, A09, A15)

Files: package/project properties, `UnitTests/UnitTests.csproj`, `UiThreadWatchdog.cs`, responsiveness/repeated-load/lifetime tests.

- Centralize matching Avalonia package versions and verify resolved dependency graph.
- Add a yielding-then-blocking UI command probe that fails prefix-only measurement.
- Measure dispatcher heartbeat across completion; separate warm-up and fresh equivalent measured states.
- Add old-session weak-reference and pending-job retention coverage; build/test/review.

### Task 6: MCP lifecycle and verified external artifacts (A10, A11, A15, A16)

Files: AgentBridge transport/dispatcher/catalog/tool interfaces and implementations; update downloader/installer/checker; `UvBootstrapper`; their tests and release workflow.

- Thread cancellation through tool invocation; track/drain accepted work and serialize mutation admission.
- Regression proves Stop prevents late design mutation and handles concurrent request cancellation.
- Define and test authenticated artifact metadata/verification policy; pin and verify bootstrap bytes; preserve upgrade compatibility and rollback.
- Clean owned temp files on failure/declined install; verify platform paths and quoting.
- Remove duplicate MathNet binary only after build/publish output comparison; align SDK/roadmap docs.
- Independent lifecycle/security review; commit verified changes separately.

## Phase 3 — UI-independent document and persistence

### Task 7: Current document contract and load/save use cases (A05, A06, A17)

Files: DTOs currently in `MainViewModel.cs`, `FileOperationsViewModel.cs`, persistence services and tests. New contract/mapping files live with persistence/application responsibilities rather than presentation.

- Characterize roundtrips for root/nested groups, routes/crossings, process/material/model state, unknown legacy fields and IDs.
- Move DTO ownership without changing JSON names; extract serialization and validation from dialog/UI orchestration.
- Classify and retire/adapt the older persistence path with explicit compatibility tests.
- Reduce FileOperations to focused presentation/use-case dispatch; no partial-only decomposition.
- Run example/load/save/GDS regressions and review public format behavior before commit.

### Task 8: Stable session/scope and simulation snapshots (A04, A06, A18)

Files: `DesignCanvasViewModel`, `GroupEditService`, `ConnectionSync`, `SimulationService`, core connection/state APIs and focused new session/snapshot services.

- Introduce one explicit root/scope accessor and revisioned operation ownership while preserving bindings.
- Capture worker input independently of live VM/component/manager mutation; apply only matching-revision results.
- Test edit/load/group-switch during paused simulation and routing; stale work cannot mutate a replacement design.
- Move authoritative writes behind session operations; view collections become projections with a single synchronization owner.
- Run selection/group/undo/export/simulation roundtrips, review and commit.

## Phase 4 — Genuine decomposition and architectural gates

### Task 9: Focused shell, input, window and export collaborators (A05, A06, A14)

Files: `MainViewModel.cs`, `CanvasInteractionViewModel.cs`, `MainWindow.axaml.cs`, `SimpleNazcaExporter.cs` and newly focused responsibilities; existing partial type families reviewed as aggregates.

- Use the tracked inventory to extract one responsibility at a time with behavior tests.
- Move shell composition into feature coordinators with explicit lifecycle, not a giant constructor bag.
- Split UI code-behind by actual view/control ownership, input by gesture/use-case, export by intermediate model/emission/process boundary.
- Preserve public commands/bindings or migrate callers with equivalence tests; verify all remaining authored source types below ceiling.
- Review aggregate type coupling and independently testable responsibility boundaries.

### Task 10: Semantic restrictions and remaining oversized tests/scripts (A07, A14)

Files: architecture tests/checker, explicit module policy, every remaining path in the source inventory.

- Test forbidden alias/static/global/fully-qualified dependencies and unregistered feature handling.
- Implement semantic dependency validation with explicit current module coverage and narrow justified shared contracts.
- Split oversized tests by behavior with shared fixtures; split scripts by independently meaningful modules, preserving entrypoints.
- Require zero whole-tree size violations; no final baselines/exemptions masking authored source.

## Phase 5 — Combined verification and final review

The implementation and focused review fixes are committed through `96fbc9ef`.
The following execution records are the acceptance evidence; use their actual
reported outcomes rather than treating the existence of a run as success.

- Focused final regression run: 203 passed, zero failed/skipped
  (`tests-20261027-155346-60876.log`). All eight final review regressions pass.
- [Full CI including Heavy](https://github.com/aignermax/Lunima/actions/runs/40526201954),
  on code commit `96fbc9ef`; the local broad run excludes Slow/Heavy.
- [Native Windows, Linux and both macOS targets](https://github.com/aignermax/Lunima/actions/runs/40526212227).
- [Architecture checks](https://github.com/aignermax/Lunima/actions/runs/40526212253),
  plus 28 local Python guard/tool tests and the 4,213-file authored-source inventory.
- Independent final review covered export/coordinate/raster deltas and cross-cutting
  ownership/document integration. Reported material findings were fixed and verified;
  this was not an exhaustive line-by-line review of the complete 6.4 MB diff.
- Fresh Windows Release publish and Browser Release build passed; published runtime
  dependencies and required Python/example assets were checked. The desktop starts
  and responds. Interactive click-through is unverified because Computer Use's
  native pipe was unavailable after recovery attempts; headless UI tests remain
  separate evidence, not a substitute claim of a manual test.
- [Draft PR #1574](https://github.com/aignermax/Lunima/pull/1574) carries the final
  aggregate results and limitations. No merge or release is part of this refactor.

## Execution log

- 2026-10-23: Created active goal; inspected current dev changes; selected clean existing worktree and branch. Source/data ceiling clarification pending. No findings marked fixed yet.
- 2026-10-23: Baseline 53 architecture/command/crossing tests passed. A01 regression: five failures proved overflow/retirement/cancel/reset defects. Fixed with per-entry retirement and weak canceled-reservation ownership. Independent fresh review clean. Combined verification: 124 passed, zero failed/skipped, including conversion/history/architecture and process tests. A01 resolved.
- 2026-10-23: A02 regression: five real-Python pipe/lifetime tests failed (initial interpreter-discovery skips corrected, then actual RED confirmed). Concurrent draining/deadline/cleanup fix passes six process cases in combined run. Independent review found no new regression, but highlighted pre-existing orphan descendants after root exit and an immediate descendant-exit assertion. Track process ownership/robust descendant verification before claiming the full lifecycle contract complete.
- 2026-10-23: Source checker, staged hook, non-destructive installer and checked-in safe test runner implemented. Seventeen Python tests pass, including real Git commit rejection at exactly 1000 lines. Whole-tree checker intentionally fails on ten existing over-limit sources. Required xUnit aggregate will now fail if source checking fails in shard selection. April assessment and false migration report removed; active docs reconciled. Overall goal remains active.
- 2026-10-23: Four oversized test files decomposed by behavior, not partial classes. Hierarchy/pin tests retain 97 methods and 615 assertions (105 cases); coordinate/preview tests retain 60 methods and 166 assertions (88 cases, including 15 Slow coordinate cases not yet rerun). Default baseline for both families was 178 passing cases. Full GDS namespace passed after first decomposition; preview family passed in the later combined 208-pass run. Commits cbdd3961 and f47d84e3.
- 2026-10-23: A08 declarations now share AvaloniaVersion=11.3.13 for desktop/browser/UI/headless, after two observed RED tests. Both architecture tests and subsequent headless regressions pass. Desktop/browser publish verification remains outstanding. Commit bc3d1f71.
- 2026-10-23: A02 follow-up found PythonModeSolverService duplicated the old unbounded IO pattern instead of using the shared runner. Two real child-process RED cases failed at their guards, then passed after migration; error/missing-backend compatibility retained. Descendant test now waits for descendant exit separately from immediate root reaping. Removed its direct-launch exemption. Commit 1bab2053. Root-exits-first descendant containment remains open.
- 2026-10-23: Local runner now rejects all-skipped runs rather than treating a Passed! heading as verification. Actual subprocess-output RED observed, then all 20 Python tooling tests passed. Commit 5d39df01.
- 2026-10-23: Cache lifetime RED reproduced four core environment failures plus presentation activation/in-flight/inline failures. Initial implementation passed its selected tests in a combined run (208 passed, 4 expected new watchdog/document-boundary failures). Review additionally identified hot-path file probing, synchronous disk parsing, derivative geometry ownership and oversized-result repaint loops. Separate regressions/ownership work underway; A03 not yet marked complete.
- 2026-10-23: A09 continuation regressions fail against the old prefix-only watchdog. New heartbeat under review; shutdown/no-pump cleanup regression added. Existing best-of-retry responsiveness harness still needs equivalent fresh measured state. A mixed exporter/heartbeat run was stopped after hanging and is NOT counted as verification; isolate subprocess integration from pure script checks.
- 2026-10-23: A10 transport/call-admission RED confirmed: queued work executes after Stop, Stop returns before accepted work drains, and tools interleave across awaits. Gate1 adds explicit revocable context and drain/serialization; validation pending. Real post-await tool/routing/simulation commits remain Gate2 and depend on A04/A18; compatibility token plumbing alone is not a complete fix.
- 2026-10-23: A17 boundary RED confirmed ten native-format types lived in presentation. Moved these contracts to CAP-DataAccess/Persistence/NativeDesign with unchanged serialized properties and updated consumers. Verification pending. MainViewModel is still oversized; no claim that the namespace move alone completes decoupling.

## October 23 verification checkpoints (historical)

These records preserve the evidence and open work at each checkpoint. The current
A01–A18 matrix below supersedes their provisional status statements.

### Platform and lifetime checkpoint (still in progress)

- Draft PR: https://github.com/aignermax/Lunima/pull/1574. No merge or release.
- Native ownership is committed in `539f1ab6`. The first native CI run passed on
  Ubuntu; macOS arm64 exposed a variadic `fcntl` ABI mismatch and macOS Intel exposed
  Darwin's zombie-only process-group `EPERM`. Fix `3357b28a` uses the .NET PAL's
  atomic duplicate operation and verifies that no live members remain before
  accepting that Darwin result. The native CI rerun is pending; Windows tests pass.
- `tests-20261023-120813-65472.log`: 113 passed, five intended REDs, one Unix-only
  skip. Demonstrated terminal-bridge restart, native input/environment compatibility,
  and HTTP-client header-timeout defects. Corresponding fixes subsequently passed.
- `tests-20261023-121558-45536.log`: 154 passed, 13 intended REDs, one Unix-only
  skip. Main/live/bridge retirement passed; REDs characterized analyzer ownership
  noise and updater file changes during the close confirmation.
- `tests-20261023-122120-53436.log`: 125 passed, ten intended lifecycle REDs and
  one newly introduced namespace inventory failure. Signer interoperability and
  staged-file revalidation passed. The explicit namespace registration was added.
- `tests-20261023-123627-39176.log`: 297 passed, nine failures, one Unix-only skip.
  Seven new shell/application retirement cases and existing startup/live baselines
  passed. Five snapshot stubs and three solver boundary cases remain intentional
  REDs. One updater test incorrectly assumed linked cancellation callbacks execute
  synchronously; its follow-up now awaits the observed callback while retaining
  the actual-drain assertion and awaits verification.
- The real compiler audit, after fixture-backed ownership corrections, reports
  1,649 dependency diagnostics and no unknown-owner noise. These are an unfinished
  migration inventory, not accepted exceptions. The audit remains opt-in until
  actual forbidden edges are removed and unconditional enforcement can be enabled.
- Canonical staged-hook/C# source policy is committed in `000aed5b`; all 23 Python
  tooling tests passed again. Signing tooling has 12 passing Python tests and two
  passing .NET interoperability tests. Publisher key provisioning remains pending.

### Extraction checkpoint (not final acceptance)

- `tests-20261023-112232-63656.log`: 42 intended RED failures and two positive
  controls passed, no skips. Demonstrated three surviving-descendant failures,
  three unintended startup-job/cancellation failures, missing semantic dependency
  diagnostics, and the newly specified publisher-manifest verification contract.
- `tests-20261023-113411-56012.log`: 91 passed, eight intended new download REDs,
  zero skipped. Publisher signature/version/platform fixtures, semantic collector,
  canonical file-size tests, constructor inactivity and real startup cancellation
  passed. Manifest authentication committed as `8497cdd1`; production key setup,
  signed release generation and updater integration are still outstanding.
- Python tooling: all 23 tests pass after sharing `source_size_policy.json` between
  the staged hook/CI checker and the replacement C# guard. The legacy basename
  exemptions are removed in the current patch. The semantic analyzer is deliberately
  not yet attached to shipping projects: repository/external ownership and directed
  edges must be fully inventoried and violations addressed first.
- Shell startup now has explicit activation and cancellation/draining. Retirement
  of live simulation, old shell subscriptions, child projections and terminal MCP
  host shutdown remains in progress; this is not yet complete application teardown.

- `tests-20261023-110942-57176.log`: **700 passed, zero failed/skipped** after
  correcting all extraction/review findings, including process-warning assignment
  order. Covers native persistence, Main wiring, shell anchors, learning journeys,
  canvas gestures, grouping/library actions, process enforcement and export workflows.
  The combined extraction is committed as `5206d1fa`; its staged 106-source-file
  size check passed. Full tracked source also passes after the new files are included.
- `tests-20261023-110611-14052.log`: 37 passed, two intentional warning-order REDs,
  one explicit Unix-permission skip on Windows. The subsequent 700-case run closes
  those two REDs. uv deadline/caller cancellation tests passed in both runs; cached
  Unix execute permissions still require a Unix host. Bootstrap follow-up `18a121f6`.

- `tests-20261023-103246-40700.log`: 357 passed, zero failed/skipped after
  cache edit invalidation, MCP cancellation-callback recovery, verified uv archives,
  watchdog shutdown handling and the first model-ownership extraction.
- `tests-20261023-105614-57476.log`: 650 passed, five failed after the combined
  Main/FileOperations/input/window extraction. Failures exposed FileOperations
  dirty subscriptions surviving disposal, unfinished canvas drag state, an invalid
  pin fixture, virtualized tutorial-anchor timing, and an unbounded download body.
  Corrections and targeted verification are in progress; this is not a green run.
- All tracked authored sources pass the Python 999-line ceiling (3563 files).
  New helpers still require the final staged check. Main is 998 lines and delegates
  process, library, selection, learning and design-check responsibilities to separate
  owners. This size result does not close remaining document/session ownership work.
- The test runner now keeps compiler errors visible despite long warning output;
  the complete log retains warnings. All 23 Python tooling tests pass (`106f9442`).
- Pinned uv installer is committed (`69388c52`); post-header download deadlines
  and Unix cached executable permissions are undergoing follow-up verification.
- Native serialization DTOs and workflow adapters are extracted. Main/FileOperations,
  window namescope and input-gesture behavior remain under combined review.

Earlier verification checkpoints, 2026-10-23:

- `tests-20261023-101544-52012.log`: 531 passed, zero failed/skipped, covering
  cache ownership/scheduling, MCP Gate1, native DTO roundtrips, exporter golden
  scripts, DI and watchdog continuations. Independent review then identified
  warm inline-template scans, missing existing-PDK edit invalidation, cancellation
  callback restart poisoning and diagnostic posting-failure masking. Separate
  regressions are being added; these findings are not considered closed.
- `tests-20261023-101800-48132.log`: 331 passed, zero failed/skipped, before the
  Main/FileOperations coordinator extractions.
- `tests-20261023-102058-59292.log`: 163 canvas/window baseline tests passed;
  two intended RED tests demonstrated mutable uv downloads and watchdog posting
  errors escaping diagnostics. No other failures/skips.
- Desktop Release publish succeeded into a fresh `TestResults/publish-proof-20261023`
  directory. MathNet SHA-256 is unchanged from the removed duplicate and NuGet 5.0.0:
  `83B41A608E438409D6520D01124F52F57409E229791C0E074E315ACE7D9F4134`.
  Browser publish and non-Windows platform checks remain outstanding.
- 22 Python tooling tests passed. SDK now stays within the declared 10.0 line,
  with prereleases disabled. Dependency cleanup committed as `99bb03b9`.
- Export facade is 238 lines with fourteen responsibility-specific writers/helpers
  (35–195 lines). No new partials. Complete script goldens unchanged and verified;
  extraction committed as `2906b5d9`. Five other production files remain over limit.
- Removed unreferenced alternate presentation persistence service/VM and DI entries
  (`0c1afb86`); the tested legacy grid serializer remains. Native DTO move verified
  in the 531-case run, but its Main/FileOperations edits cannot be committed until
  those files satisfy the mandatory staged size gate.

Additional verification, 2026-10-23 afternoon:

- `dc122aa1`: immutable simulation physics capture committed after the legacy-oracle
  and isolation tests passed. UI integration and prepared visualization are verified
  locally but still belong to the uncommitted lifetime/document integration slice.
- `50d54b41`: explicit PDK library/reader/writer and diagnostic contracts committed;
  consumers and mandatory architecture enforcement are not yet complete.
- Architecture audit 3 builds with zero errors and reports 1,203 distinct forbidden
  edges (1,201 presentation-to-infrastructure, two application-to-infrastructure).
  This is a remediation inventory, not a passing architecture gate.
- CI `39945959514` at `84af0862` failed only the German preview-recovery fixture's
  temporary-directory cleanup. The local correction retires the preview producer
  and drains its writes; it does not retry or suppress the deletion failure.
- `tests-20261023-135148-45740.log`: 195 passed, 19 failed. Fifteen failures were
  intended new RED cases; the remainder exposed a second native-load reset path,
  a cancellation-fixture scheduling timeout, Undo grid work and a possible heartbeat
  completion-timing artifact. These were not counted as a green run.
- `tests-20261023-140125-57000.log`: 275 passed, 12 failed. Seven routing snapshot
  cases deliberately remain RED pending implementation. Four new regressions expose
  delayed caller-cancellation publication and native model/template loss while
  saving a nested scope. Undo is the only failed responsiveness case (228 ms);
  measured raster registration accounts for 197 ms. The other 21 command cases,
  preview retirement, Python cancellation ordering and progressive document commits
  pass. A passing Make Gate rerun alone does not close the heartbeat review finding.
- The working-tree source inventory, including untracked additions at that checkpoint,
  passed with 3,750 authored files and no file above 999 physical lines. All 23 Python
  tooling tests passed again, including an actual rejected 1,000-line Git commit.

## Current audit resolution — October 27

These statuses describe implemented resolutions and focused review/verification.
Aggregate execution outcomes are linked in Phase 5 and the PR; they must be green
before accepting the branch. Historical checkpoints below retain their original
failures and pending work so that intermediate runs are not mistaken for acceptance.

| Finding | Resolution and evidence |
|---|---|
| A01 | History-entry retirement and crossing metadata ownership; reviewed, 124-case combined verification |
| A02 | Shared owned process containment plus bounded stream capture, all callers migrated; actual-child native matrix includes preview/export/setup/parser and coordinate diagnostics |
| A03 | Bounded preview cache, warm lookup, effective-environment and PDK-edit invalidation; lifetime controls verified |
| A04 | Main, routing, wavelength, truth-table/logic, parameter, compression, optimization, transient and Monte Carlo inputs detached; legacy numerical policy controls verified |
| A05 | Shell/input/window, export writers, routing, analysis and persistence responsibilities extracted with behavior controls |
| A06 | Capability-based dependency boundaries; desktop/browser builds enforce the declared direction with no dependency diagnostics |
| A07 | Semantic dependency analyzer mandatory as errors in shipping projects; negative tests and namespace/project coverage, no warning baseline |
| A08 | Shared Avalonia 11.3.13 runtime for production/headless hosts; final publish and resolved package checks verified |
| A09 | Whole-operation heartbeat tests retain the 100 ms command budget; exact raster reuse removes repeated scan conversion without weakening the six-second large-import budget |
| A10 | MCP/assistant mutation admission, cancellation, post-cleanup responses and isolated repair trials verified |
| A11 | Pinned uv, authenticated updater artifacts and signed publisher tooling verified; production signing-key provisioning is an external release prerequisite |
| A12 | Checked-in serialized test runner, execution-result validation and matching docs; 28 Python guard/tool tests pass |
| A13 | April assessment removed; architecture and codebase map reconciled, all 72 explicit map paths checked |
| A14 | Staged-index hook and whole-tree CI ceiling; 4,213 authored source/documentation files at most 999 physical lines |
| A15 | Application/shell/window/feature ownership, real process cleanup, repeated-load controls, analysis rebinding and export/settings/coordinate retirement verified; final 203-case run passes |
| A16 | Duplicate binary removed, SDK/package policy aligned, fresh publish has one MathNet 5.0.0 assembly |
| A17 | Native DTO/workflow separation and detached atomic save verified, including native symlink and Unix-mode checks |
| A18 | Canonical root/scope access, native capture, revision-gated routing/grid publication and configured-document admission verified |

Further integration checkpoints (same day):

- `935bb76e`: seven private-routing capture/isolation/parity cases passed before
  committing the immutable input boundary. It is not yet the canvas routing path.
- `tests-20261023-142452-27232.log`: 253 passed, 19 intended new component-window
  retirement failures. All 22 responsiveness commands, native I/O lifetime guards
  and seven routing snapshot controls passed.
- `tests-20261023-144438-69084.log`: 336 passed, nine failed. The 19 retirement
  regressions and cleaned responsiveness fixtures passed. Failures were four new
  group-document cases, four crossing snapshot stubs, and the missing exact routing
  snapshot namespace in the architecture ownership manifest.
- `tests-20261023-145215-47424.log`: 211 passed, 19 failed. Home/learning preference
  ports, canonical group connection continuity, native metadata and the ownership
  manifest passed. Ten new settings publication/configuration cases and seven new
  crossing cases were RED; two existing group-bounds cases exposed a regression.
- `tests-20261023-145854-3404.log`: 297 passed, 16 failed. Both group-bounds
  regressions, all settings publication cases, and all 17 routing snapshot cases
  (including crossing prototypes, floors and undo) passed. The remaining failures
  were 12 newly added editor/import lifetime cases and four accepted-route-intent /
  atomic group-publication cases. No skipped tests in these integration runs.
- Inventory including untracked authored source: 3,784 files, none over 999 physical
  lines. This validates the file ceiling, not completion of dependency remediation.
- Review identified additional admission work: an old settings window must not
  start work against a replacement document, and actual MCP tools must enforce
  revocable commit permission after their awaits. Entry-only adapter checks do not
  establish this guarantee. Neither finding is considered closed.

### 2026-10-23 — Document admission and feature contracts

- `ea1e12f0` integrates the reviewed editor/document/lifetime work; native process CI passed on all three platforms. Full xUnit CI found one obsolete localization fixture expecting constructor-started work; `a8bdbead` migrates it to explicit activation and awaited retirement, retaining weak-reference collection checks. Its next remote run is pending.
- `a13d6bef` commits immutable crossing/prototype/parametric ownership and hierarchy batch construction. Routing UI worker integration (R3) is still incomplete; five controlled isolation tests remain RED.
- `544b32b8` adds non-owning document identity bookmarks and atomic admission for configured features; four identity regressions pass. `a8bdbead` checks both document identity and canonical component reference membership before settings work starts; all eleven admission cases pass.
- `3616adf8` separates GDS execution, interpreter session and discovery contracts, keeping one runtime/cache owner. Existing test assertions and data attributes were preserved. `e2fc7068` replaces concrete preference-store dependencies with ten feature-specific ports and same-instance aliases; both interface-only behavior tests and existing persistence tests pass.
- `tests-20261023-153453-41904.log`: 394 passed, ten intended REDs, one Unix-only skip. GDS/discovery contracts, identity and real MCP load cancellation passed; REDs were five routing isolation and five obsolete settings cases.
- `tests-20261023-154303-25480.log`: six passed and five intended REDs. Demonstrated a stopped/replaced load prompt could still enter Save, and a save picker could write a replacement document or outlive retirement without ownership.
- `tests-20261023-155520-1236.log`: 345 passed, seven intended REDs, one existing MainWindow AI skip. Native prompt/picker fixes, settings admission, localization retirement and preference ports pass. Remaining REDs: five R3 integration cases and two newly added real MCP save cancellation cases. Prior two compilation attempts failed on missing using/fixture argument and are not verification successes.
- Uncommitted follow-ups include revocable native-file requests covering prompts, picks, parse and actual I/O cleanup; explicit MCP save/load commit permission; asynchronous document-operation cleanup; coordinate diagnostic contracts; and sweep retirement regressions. These are not final acceptance or release evidence.
- The source ceiling is green for 3,794 tracked authored sources at this checkpoint; new untracked additions also require the staged hook before commit. Semantic dependency enforcement remains opt-in while actual forbidden edges are being removed. No final architecture/functional-equivalence claim is made.

### 2026-10-23 — Private routing and async ownership verification

- `bd33f166` owns native prompts, file picks and accepted I/O through actual cleanup, with explicit MCP load/save permission at commit boundaries. `14d1a50b`, `bf2cbabd` and `df9f029c` remove coordinate, preview and Python registry implementation dependencies; shared cache identity and generation rejection remain tested.
- `4477e890` integrates private routing into the actual canvas. Progressive frames and final topology are revision-gated; crossing undo, existing model identity and unchanged-path identity are preserved. Two integration regressions exposed missing contention-repair memory and conversion cancellation after an unrelated edit; both are corrected without weakening the original assertions.
- `fa8edda1` and `bb71cee3` own spectrum/analyzer jobs, configured analyzer admission and measured-file work. A further review found synchronous linked-token disposal could block the UI during cancellation; two controlled regressions are still RED and await correction. Physics inputs for these additional analyses are still a separate A04 follow-up.
- `200fd1d6` shares one document operation across MCP commands, routing and multi-step repair application. Response admission is checked after asynchronous cleanup. Actual tool tests cover cancellation, server closure, document replacement, navigation and concurrent editing. Speculative `suggest_fixes` trials still use the live document and require a private workbench; the two asynchronous assistant-adapter paths also remain under remediation.
- `bb4b9d3d` extends the 999-line policy to authored Markdown/RST/text. Three historical July plans are indexed and split by responsibility while retaining their content. All 24 Python tooling tests pass; raw data/assets remain separately reported, not silently treated as approved exclusions from the user's broader request.
- `tests-20261023-160901-34688.log` was aborted after a legacy test blocked a process-floor provider during synchronous snapshot capture. It is not success evidence. Eight affected fixtures now use a controlled worker seam and retain their behavioral assertions.
- `tests-20261023-161651-50388.log`: 261 passed, 23 failed; five routing-isolation cases and actual native I/O controls passed. Twenty-one intentional REDs and two route-identity regressions remained at that checkpoint.
- `tests-20261023-163015-71740.log`: 984 passed, two failed, no skips. The remaining failures were the two routing regressions corrected above.
- `tests-20261023-163901-11736.log`: all 31 responsiveness/gate-view cases passed on the compiled 163015 snapshot, including all 22 command budgets at the unchanged 100 ms threshold. Remote full CI at `a8bdbead` still failed the CPU-example gate-view budget (118 ms); the next integrated remote run must confirm it.
- `tests-20261023-164249-57952.log`: 226 passed, eleven intended REDs. Four response-after-cleanup cases and seven analyzer/overlay admission-retirement cases reproduced the targeted defects. The preceding compile attempt failed on a fixture namespace collision and is not success evidence.
- `tests-20261023-165020-46400.log`: 470 passed, eight failed. All routing, analyzer/overlay and response-cleanup corrections passed. Five new multi-step repair cases were intentionally RED; one new control needed distinct-component moves to avoid normal history merging, and two probe fixtures needed to expect the newly enforced cancellation response.
- `tests-20261023-165920-47532.log`: 215 passed, nine intended REDs, no skips. All MCP edit/repair/response corrections pass. Remaining REDs reproduce seven actual assistant-adapter permission gaps and two UI-blocking analysis cleanup cases.
- A forced desktop rebuild with the real semantic analyzer (`architecture-desktop-audit-4.log`) succeeds with zero errors and finds 280 distinct forbidden dependency edges, down from 1,203 in audit 3; no unclassified ownership/project diagnostics remain. This is still an opt-in remediation inventory, not a passing mandatory architecture gate. Remaining implementation owners include Python setup/process launching, bridge registration/hosting, registry download, calibration/import and file exporters.
- Final combined CI, desktop/browser publish, remaining lifetime/snapshot work, mandatory semantic enforcement, operational signing-key provisioning and whole-branch independent review remain outstanding. The draft PR is not ready to merge or release.


### 2026-10-23 — Save isolation and remaining lifecycle regressions

- `5b0496b6` propagates revocable permission through the actual assistant adapters
  into connection edits and simulation publication. All twelve actual-adapter
  regressions passed in `tests-20261023-170448-37172.log` (259 passed, fifteen
  intentional new failures elsewhere).
- `57fceafb` removes synchronous linked-token cleanup from analyzer/spectrum
  retirement. All six controlled UI-blocking callback cases now pass.
  `c630130a` separates managed Python package sequencing from presentation;
  cancellation lag, late publication, package order and existing setup flows pass.
- `tests-20261023-171506-16196.log`: 335 passed, eight intended save-isolation
  failures, one Unix-only permission test skipped on Windows. Atomic file-write
  failure/visibility and file-format controls passed. The eight failures showed
  live metadata, models, group roles/outlines and imported definitions entering
  the background save without detachment.
- `tests-20261023-172553-54296.log`: 205 passed, twenty failed, no skips.
  All native-save isolation fixes, nested model/dispersion captures and existing
  native/GDS persistence cases pass. Remaining failures comprise seven private
  repair-workbench cases, six analyzer export/window-retirement cases, four
  openEBL process-lifetime cases, two deterministic transport stop races and
  the existing die-drop background-grid regression. One openEBL failure also
  exposed test cleanup deleting a directory before its child exited.
- Independent save review found no remaining aliases in the changed capture
  paths, but identified Unix file-mode and symlink compatibility follow-ups for
  atomic replacement. New platform regressions are pending; no cross-platform
  save-completion claim is made yet.
- Remote `a819bd90` architecture, signing and native-process checks pass. Its full
  xUnit run is still in progress with two confirmed failures: die-drop grid
  extent publication and an MCP listener stop/start race. Both are reproduced
  locally; fixes and re-verification are in progress. No release readiness claim.

- `tests-20261023-174143-6256.log`: 450 passed, 22 failed, one Unix-only skip.
  Private-repair root-scope controls, all analyzer exports, openEBL process cases,
  transport stop races and the die-drop correction pass. Eleven new LogicPanel
  cases and eight new registry cases reproduce lifetime/publication gaps; the
  existing GDS source-geometry identity assertion also fails. Two symbolic-link
  tests fail during Windows fixture creation (missing privilege 1314), **not**
  because the save behavior was exercised. They require precise platform skips
  locally and real execution on the Unix CI matrix. The earlier 173940 attempt
  failed compilation on a remaining old repair-candidate fixture and is not a
  test success. Its fixture was migrated without changing ranking assertions.
- `489752cb`, `073172ed` and `1709f0e1` commit the verified transport/analyzer/
  openEBL/die corrections. Additional private-scope parity, source-geometry
  publication, wired-die undo and registry/LogicPanel corrections remain under
  verification. The final architecture acceptance gate is still open.


### 2026-10-23 — Integrated verification and follow-up review

- `tests-20261023-180153-69092.log`: 548 passed, zero failures, three explicit
  Windows platform skips (two symlink privileges and Unix file mode), 551 total.
  Includes all five expensive repair-family parity cases, LogicPanel retirement,
  registry retirement/latest-selection, native capture, imported source-geometry
  identity, die undo and existing GDS editing regressions.
- `962f4e79` commits native capture detachment and atomic save. Unix atomic-save
  tests are added to the Linux/macOS CI matrix with zero skips permitted; their
  execution on the new commit remains pending. `e9b41f21` commits LogicPanel
  lifetime ownership, and `4d63b655` asserts measured-overlay retirement against
  the actual reader token rather than thread-pool callback dispatch latency.
- Full remote xUnit run 39974830789 at `a819bd90` finished with three failures:
  die-drop bounds, MCP stop/start race and existing GDS geometry identity.
  All three now pass locally. A new full remote run is still required.
- Forced semantic desktop rebuild `architecture-desktop-audit-5.log` succeeds
  with zero errors, 197 distinct LUN001 edges (280 previously), no LUN002/003.
  Semantic enforcement is not yet mandatory or passing.
- Independent review found two additional issues despite the passing test batch:
  actual agent UndoTool can admit a die-grid capture inside its outer document
  commit, and private repair graph construction still rasterizes on the UI thread.
  New boundary regressions and fixes are required before those slices are accepted.

- `tests-20261023-181649-72124.log`: seven intended failures reproduced actual
  agent-undo partial-commit capture, UI-thread private graph construction (four
  controls), and direct server/CLI dependencies in both bridge presentation models.
- `tests-20261023-182540-71460.log`: 160 passed, nine intended spectral-isolation
  failures, no skips. Actual undo/grid controls, private construction retirement,
  all five heavy repair-family parity cases, bridge settings/registration and
  FDTD parsing pass. The nine failures show seven live-physics aliases and two
  actual ONA/Spectrum runner paths; snapshot implementation is still pending.
- `6575c152` separates bridge status/registration capabilities from transport/CLI
  implementations, preserving configured DI owners and existing connection syntax.
  Independent review found no new defect in that cut. `e5c1bef4` routes FDTD parsing
  directly through the existing shared protocol parser. Neither commit claims to
  finish all presentation/process lifetimes.
- Review additionally identified early disposal of an operation while borrowed
  die-grid work is still running, and old-grid publication after a later commit by
  the same operation. Explicit retirement/rebased-input regressions are next.
- Source tooling: 3,994 tracked authored source/documentation files satisfy the
  999-line ceiling; all 24 Python guardrail tests pass. Non-source data inventory
  remains separately reported rather than silently included in this claim.


### 2026-10-23 — Detached analysis, real process checks and dependency audit 6

- `tests-20261023-183350-47536.log`: 299 passed, four intentional failures,
  no skips. New bridge page retirement and borrowed-grid early disposal/rebased
  input cases exposed lifecycle gaps; PDK and GDS capability cuts passed.
- `tests-20261023-184203-19516.log`: 724 passed, one intentional grid callback
  failure, no skips. Sixteen detached spectral controls, GDS boundaries and
  existing physics/import/persistence regressions pass. The remaining test
  demonstrates synchronous callback cleanup blocking the dispatcher.
- `tests-20261023-184749-69348.log`: 121 passed, five failures, no skips.
  Grid/routing callback cleanup, owned undo, private construction/scope and
  openEBL pass. Four new bridge-configuration cases reproduce stale registration
  feedback; an existing Rung5 journey exposes stale logic evaluator publication.
- `a09d51f9` captures wavelength analysis physics before worker scheduling;
  `3a5cf960` drains cancellation callbacks asynchronously. `ec48b111` owns
  private repair trials and derived grid work through actual retirement.
  `974804d0` moves openEBL execution behind its configured submission-check port.
  Each slice has focused regression evidence and review; none is whole-branch
  acceptance on its own.
- Forced desktop rebuild `architecture-desktop-audit-6.log`: zero errors,
  70 distinct LUN001 violations, down from 197; no LUN002/003. Required semantic
  enforcement remains pending until those actual dependencies are removed.
- Native-process/atomic-save CI 39987873122 at `89a2d54d` passed on Ubuntu 24.04,
  macOS 15 ARM and macOS 15 Intel, with zero skips. Symlink and Unix-mode save
  behavior now has actual platform execution evidence. Architecture and signing
  checks also passed. Full xUnit 39987873098 failed only fast-integration: five
  synchronous container-disposal errors after LogicPanel acquired async lifetime.
  The other eight test shards passed. Five fixture sites now use `await using`;
  a fresh complete CI run is still required, including the newly found Rung5 issue.
- `tests-20261023-185924-46240.log`: 33 passed, 24 failed, no skips. Four bridge
  configuration-generation corrections pass. New REDs comprise eleven actual CLI
  admission/process controls, eight bundled-PDK capability controls, two logic
  publication controls and one shell cancellation callback case. Existing Rung5
  still fails; one close-page fixture incorrectly asserted callback scheduling
  immediately and now waits for the actual cancellation signal. Fixes and fresh
  verification are in progress; no merge or release readiness claim.


### 2026-10-27 — Resumed verification and reviewed capability cuts

- `tests-20261027-094726-54388.log`: 543 passed, three failures, zero skips.
  Actual CLI lifecycle/admission, logic publication and all Rung5 journeys pass.
  Remaining failures: the intended measurement-source boundary RED, a new
  library fixture returning null instead of its non-null preferences collection,
  and the older import scanner rejecting shared process infrastructure. The
  fixture now supplies valid empty collections without changing its assertions;
  the scanner receives a narrow infrastructure allowance and eight controls.
- `tests-20261027-095353-45820.log`: 166 passed, four intentional review REDs,
  zero skips. Catalog, measurement, CLI and scanner changes pass. The remaining
  cases prove reentrant shell activation, skipped child cleanup after another
  child fails, and stale settings feedback from synchronous toggle observers.
- `ebf12466` versions logic handoffs and corrects asynchronous container disposal.
  `4be0251b` injects configured PDK catalog and measurement-source capabilities;
  independent reviews found no migration regressions. `16401a58` owns actual
  CLI process drainage and original-token admission. Real CLI cases are also
  added to the Linux/macOS native matrix; new remote execution is pending.
- Independent reviews additionally record two inherited issues requiring bounded
  follow-up: bundled-PDK write protection compares lexical paths and can miss
  a directory symlink alias; Windows npm `claude.cmd` discovery is advertised but
  direct executable launch does not support batch shims. Neither behavior is
  claimed fixed by the capability or CLI lifetime commits.
- `check_source_size.py` passes for 4,037 tracked authored files; all 24 Python
  tooling tests pass. This count excludes untracked additions until staged and
  the separately inventoried data artifacts. Overall goal remains open.

- `tests-20261027-095638-25544.log`: all 66 targeted shell/startup/settings,
  Python-startup and connection controls pass, zero failures/skips. Reentrant
  activation now joins a preinstalled task; all startup children drain even when
  one retirement fails. Settings recheck ownership after toggle observers run.
- Forced desktop rebuild `architecture-desktop-audit-7.log`: zero errors,
  57 distinct LUN001 diagnostics (70 previously), no LUN002/003. Remaining
  dependencies and exact ownership classification are still being remediated;
  semantic enforcement is not yet a required passing gate.


### 2026-10-27 — Mandatory dependency gates and continued runtime verification

- `abe363e8` replaces remaining presentation-owned filesystem/process/catalog
  construction with configured capabilities. Exact-member ownership allows only
  the default MainWindow compatibility constructor to compose its default ports.
  Desktop and browser clean rebuilds pass with the semantic analyzer enabled as
  errors by default; no opt-in warning configuration remains.
- `tests-20261027-121332-56952.log`: 166 passed, one GateView responsiveness failure
  (142 ms versus the unchanged 100 ms limit). A previous pass was insufficient
  evidence of stable performance. Compact matrix capture is being measured and
  verified against existing numeric, formula, routing and clone-ID behavior.
- `tests-20261027-121803-23924.log`: 19 passed, four intentional PDK process REDs.
  Closed pipes bypassed the deadline, cancellation returned before root reaping,
  and both descendant-output variants escaped correct cleanup. Coarse retry
  callback/budget/counter controls pass before extraction.
- `tests-20261027-122250-18964.log`: 892 passed, one GateView responsiveness failure
  (107 ms), one external PhotonTorch skip. PDK process retirement and extracted
  coarse retry controls pass. These are scoped results, not whole-branch acceptance.
- Full PR CI on `0408129f`: eight test shards passed; slow-export was canceled
  before tests because apt setup took 27 minutes 47 seconds. `14e1e9df` retains
  install progress and bounds individual network retries/timeouts; actual fresh
  CI remains necessary. Native process CI passed Ubuntu and both macOS targets.
- Source checker: 4,088 tracked authored files below 1,000 lines; 24 Python
  guard-tool tests passed. Untracked additions require staging and a new check.

Implemented in `76347951` — original parameter-sweep scope: parameter sweeps must capture the selected slider,
range, source fields and optical circuit before scheduling; trials update private
matrices only. Keep the legacy ParameterSweeper API/semantics for its direct
callers. The panel owns accepted work through cleanup, guards result/status and
CSV publication against document/selection replacement, and joins shell shutdown.
Existing CSV formatting, source detection, 1550 nm sample behavior and pin display
names remain compatible. Tests first reproduce live trial-slider writes and
reentrant selection replacement, then hold a real detached solve across edits,
replacement, cancellation and retirement. No other panel is implicitly covered.


Implemented in `76347951` — original layout-compression scope: reproduce reconfiguration during running
feedback, then detach force calculation from live editor components. Retain one
force algorithm and its existing iteration, ordering, spacing, locked-component
and centering rules; the compatibility facade continues applying positions for
direct callers. The panel captures values and the original target array before
feedback, owns actual worker and routing cleanup, and applies immutable progress
frames only on the dispatcher under a revision lease. Reconfiguration or shutdown
revokes further commits, including queued progress and derived routing results.
No new layout algorithm or UI flow is introduced. Verify legacy numeric controls,
private snapshot isolation, stale-progress rejection, and actual retirement.

### 2026-10-27 — Review-driven runtime isolation and concurrent retirement

- `tests-20261027-130633-67676.log`: 143 passed, 16 intentional regression
  failures, zero skips. Windows CMD arguments/lifetime and parameter sweep
  corrections pass. New failures prove seven MakeGate callback/retention defects,
  five shell admission/retirement races, three transient live-state leaks, and
  replacement-canvas compression during preparation.
- `tests-20261027-131310-58672.log`: all 303 selected tests pass with zero skips,
  including the above fixes, existing transient physics, MakeGate commands,
  selection controls and compression snapshot/retirement controls. This is a
  scoped checkpoint, not final acceptance. Further callback and already-applied
  compression-progress cleanup controls are being added after review.
- `3a000ac5` captures transient circuit physics and diagnostic ownership.
  `c77aa3a9` preserves real Windows batch-forwarder arguments and adds a Windows
  native CI job requiring all 14 batch cases with zero skips. `cfde323c`
  synchronizes shell admission, completion and retirement; independent review
  found no material issue. The corrected Windows diagnosis is argument parsing,
  not unsupported batch process admission: direct dispatch worked on the tested OS.
- Full PR CI at `e042c785` completed: all shards except fast-rest passed. The
  latter exposed concurrent HashSet mutation in ShellWorkScope from selection
  rebuild completion; five local regressions reproduce it before `cfde323c`.
  Native Linux/macOS jobs passed. New `cfde323c` CI is running; architecture
  guardrails and release-signing tooling have passed.
- Python guard tooling: 24 tests pass. Size checker passes for 4,108 tracked
  authored files. New untracked analysis additions still require final staging
  and a repeated whole-tree check.

### 2026-10-27 — Analysis parity and adversarial UI publication

- `tests-20261027-134112-7008.log`: 770 passed, one failure, zero skips.
  Snapshot parity, source-size/dependency guards, actual Windows batch argv,
  analysis retirement and accepted compression-progress routing cleanup passed.
  The failure was an old reflection fixture calling a changed private plot method;
  it now drives the real transient command and retains its original zoom/buffer
  assertions. That replacement passed the following run.
- `tests-20261027-134903-46224.log`: 55 passed, 25 intentional review failures,
  zero skips. The failures cover Monte Carlo publication/retirement (13), nested
  observable collection rebuilds and stale collection senders (5), cleanup after
  a throwing completion observer (4), and three corrupted legacy error strings;
  the collection total includes the MakeGate case and overlaps no other category.
  Corrections are in progress; no whole-branch acceptance is claimed.
- Native CI on `cfde323c` passed Windows, Linux and both macOS targets. On
  `a435b3c7`, all 14 Windows batch controls still pass, but one additional native
  CLI test fails deleting its temporary directory after process-exit assertions.
  A real Windows sharing-lock cleanup regression is being added. Linux/macOS,
  semantic architecture checks and signing tooling pass on that head.
- `tests-20261027-140223-28004.log`: 812 passed, two intentional regressions,
  zero skips. All 25 preceding review failures now pass. New failures reproduce
  the gate editor's lease leak after a throwing completion observer and Windows
  fixture cleanup under a held sharing lock.
- `tests-20261027-140721-23484.log`: 114 passed, five intentional regressions,
  zero skips. Both previous fixes pass, including every Windows CLI case. New
  failures demonstrate deferred transient plot publication outliving its lease
  and four Monte Carlo eye live-input leaks. Three legacy eye metric controls
  pass, including fixed receiver RIN versus the eye panel's different policy.
- Verified commits: `76347951` isolates parameter sweeps/layout compression;
  `eedfe2c1` captures fabrication spectrum trials; `a4655899` owns Monte Carlo
  panel work; `2c8e9ba5` sequences display notifications; `cb2e112e` isolates
  circuit optimization; `dba4f663` bounds Windows fixture sharing-lock cleanup;
  `9906939c` owns gate editor work. Shared shell shutdown integration awaits the
  transient slice commit. These commits have not yet received final full CI.
- `tests-20261027-141244-46852.log`: 299 passed, zero failed/skipped. Awaited
  transient display publication fixes the immediate nested-run regression;
  existing transient/eye physics, trace visibility and shell retirement controls
  remain green. `190a76d2` commits the transient slice and shared shell shutdown.
- Whole-tree source check now covers 4,168 tracked authored files below 1,000
  lines. All 24 Python guard-tool tests pass. The branch through `190a76d2` is
  pushed for fresh CI; the uncommitted Monte Carlo eye regressions remain open.
- `tests-20261027-141534-58280.log`: seven Monte Carlo eye failures and three
  passing legacy-policy controls, zero skips. Further inspection is required for
  the output-removal fixture's sensitivity assertion; it must not be counted as
  demonstrated production isolation solely from the failure count.
- Final integration review found persistent shell panels were not rebound after
  accepted New/Load, and the separate visible TruthTable panel lacked owned
  extraction/publication. Both are required functionality gaps, not optional
  architecture enhancements. Their public-command regressions are now included.
- `tests-20261027-142328-44288.log`: 161 passed, 12 failed, zero skips. Four real
  shell replacement regressions and seven truth-table ownership regressions fail
  as intended. Rejected replacement controls and legacy truth-table behavior pass.
  Monte Carlo eye numeric and panel controls pass except the output-removal
  sensitivity fixture, which still reports no changed live metric and needs repair.
- Native CI on `190a76d2` passes Windows, Linux and both macOS targets; architecture
  and signing-tool checks also pass. Full PR CI is still running. The previous
  superseded run spent 26 minutes downloading packages from the Azure Ubuntu
  mirror; `8d655f69` bounds download-only preparation and retries the canonical
  mirror without interrupting dpkg. Extracted Bash syntax validation passes;
  actual final-head workflow verification remains required.

- `tests-20261027-143606-38816.log`: 391 passed, three failed, zero skips.
  Primary project-replacement and truth-table fixes pass. Two additional coordinator
  regressions expose retirement during rebinding and an observer error stranding
  other panels; the remaining Monte Carlo failure is fixture sensitivity.
- `tests-20261027-144407-29008.log`: 474 passed, one failed, zero skips.
  Both coordinator regressions now pass. `86830706` commits reviewed truth-table
  ownership and persistent-panel rebinding, including accepted/rejected New/Load.
  The Monte Carlo output-removal fixture now distinguishes upstream/downstream
  traces, but its severe attenuation closes the eye and needs a realistic level.
  An earlier compile attempt failed on an IReadOnlyDictionary assertion overload;
  this was corrected without changing its assertion.
- CI `40512905887` on `8d655f69`: seven xUnit shards passed; fast-rest exhausted
  package preparation because a file-based apt mirror list escaped the fallback.
  Slow-integration passed 229 tests and exceeded the unchanged six-second imported
  routing budget at 6.314 seconds. Both are under investigation. Native process
  CI passes all four OS targets; architecture and signing-tool checks pass.

- `tests-20261027-144956-33636.log`: all 109 selected Monte Carlo and production
  routing tests pass, zero skips. Output-policy fixture uses moderate attenuation
  and zero-delay routes, preserving positive eye and changed-live sensitivity.
  `ba148c04` commits the captured Monte Carlo eye metric and lifetime controls.
- Production routing phase measurement: total 3.006 s locally, of which private
  router/grid reconstruction is 2.649 s; manager work is 221 ms and capture/apply
  phases each below 35 ms. Imported routes remain frozen. Temporary diagnostics
  are uncommitted; the unchanged six-second CI budget remains under remediation.
- `99dacab2` handles the runner's actual file-based APT mirror list. All 28 Python
  guard/tool tests pass; the four new tests exercise the action's embedded fallback
  and preserve unrelated repository URLs. Bash syntax checks pass. Branch through
  `ba148c04` is pushed; 4,185 tracked authored files satisfy the 999-line ceiling.
- Final process inventory found real A02/A15 gaps in preview, GDS script execution,
  uv setup and Nazca PDK parsing: deadlines omit some pipe/exit phases or cancellation
  returns without actual process cleanup. Controlled-child regressions and fixes
  are required before closing these findings; standalone updater/shell launches
  intentionally have independent ownership and are not these workflows.

- `tests-20261027-145714-56692.log`: 46 passed, 15 expected lifetime regression
  failures, zero skips. Failures demonstrate preview/export, uv and parser root or
  descendant survival, inherited-pipe hangs, cancelled admission and missing exit
  deadlines. Existing parser/uv/export controls pass. One raw-code fixture failure
  still needs classification before being counted as a production defect.
  The preceding attempt failed compilation because temporary domain profiling
  referenced an application namespace; the probe was corrected without relaxing
  the mandatory dependency analyzer.
- Fine routing timings confirm outline scan conversion dominates: 2,908 ms,
  versus 128 ms transformation, 248 ms stamping, 11 ms clearing and <1 ms allocation;
  total 3.812 s, frozen-route assertions pass. Reusing immutable exact raster rows
  is being evaluated with invalidation/parity controls, not by skipping validation.

- `tests-20261027-150533-59364.log`: 161 passed, nine failed, zero skips.
  All 23 new subprocess cases now pass, including raw-code cleanup after correcting
  Windows newline translation in its test fixture. Existing export/preview and
  NAND/register/compression integration controls pass. Eight new exact raster
  reuse/bounds cases are intentionally RED; the ninth failure is a Windows cmd
  echo compatibility regression in the new capture runner and remains open.
- CI on `ba148c04` completed: seven shards pass; fast-integration has two failed
  truth-table journey cases, fast-rest has one related marker case and six UI
  compression cases. These fixtures selected detached viewmodels or blocked UI
  commands off the dispatcher. `786a373d` uses the canonical loaded selection and
  awaited Avalonia UI execution, retaining all assertions; independent review and
  the combined local run pass. All package preparation now completes, and native,
  architecture and signing CI checks are separately tracked for the next head.

- `a98c6775` commits shared owned stream capture and preview/export/setup/parser
  adapters. The 23 new subprocess cases and Windows argument-parity controls pass;
  native CI `40520449033` passes Windows, Linux and both macOS targets. Architecture
  and signing checks also pass. Full PR CI on `786a373d` passes all selected shards.
- `tests-20261027-151832-22168.log`: 108 passed, 11 intentional export-workflow
  failures, one Unix-only skip. The initial raster-cache controls pass, but the
  production import still performs 316 scan conversions and takes 3.378 seconds;
  the optimization is not accepted from isolated controls alone.
- `tests-20261027-152938-43396.log`: 13 passed, eight failed, zero skips. All 11
  export-workflow ownership regressions pass. Six coordinate-helper regressions
  remain, together with oversized retained raster storage and production cache
  misses. The unchanged production pass takes 3.506 seconds, with 3.039 seconds
  rebuilding the grid; original geometry/frozen-path assertions remain intact.

- `tests-20261027-153558-41444.log`: 124 passed, 16 intentional failures, zero
  skips. All original coordinate/extractor cases and 22 raster controls pass.
  Compact exact vertex/boundary storage and merged-span trimming reduce the
  production pass from 3.506 s to 0.810 s; grid rebuilding falls from 3.039 s to
  0.370 s. All 158 entries fit in 31,609,504 accounted bytes under the unchanged
  32 MiB budget, with zero new scan conversions and unchanged frozen routes.
  Remaining REDs are three coordinate-comparison process cases, six coordinate
  workflow cases and seven export DI/window/publication cases. No legacy export
  control fails. Diagnostic instrumentation will be removed before acceptance.
- All 28 Python guard/tool tests pass again; tracked authored-source size check
  passes for 4,193 files. Full PR CI `40520449018` reports success on `a98c6775`;
  this is not the final pending worktree or a Heavy-inclusive manual run.

- Clean raster/coordinate/export checkpoint compiled in
  `tests-20261027-154058-30032.log`, but the coordinator stopped that run after an
  overly broad OpenEbl selection entered the real SiEPIC MZI export. It supplies
  no passing-suite evidence. Those external integration cases remain in full CI;
  the next local selection names the affected workflow controls explicitly.
- Independent final-delta review found no raster correctness blocker. Coordinate
  diagnostic APIs still need input/reset supersession and reentrant-reset guards;
  these have no demonstrated current AXAML caller, but are supported public model
  operations. The real export settings discovery command needs owned cancellation,
  and a synchronous error-console observer can retire gdsfactory export before its
  final status write. Focused regression cases are being added for these findings.
- A separate fresh reviewer thread could not be created because the session thread
  limit was reached. Existing non-author agents independently review the final
  feature deltas; the raster agent also reviews the combined architecture in
  focused passes. This limitation must not be represented as an exhaustive
  fresh-context line-by-line review of the 1,323-file branch.

- `tests-20261027-154751-52600.log`: 154 passed, ten failed, zero skips.
  Clean raster code (all diagnostics removed), all prior export/window/DI fixes
  and selected legacy controls pass. Eight new review regressions fail as intended.
  A comparison success fixture also exhausted its 700 ms startup budget, and the
  explicit-interpreter error lost its executable name in the native Win32 message;
  both are being corrected without relaxing actual timeout/cleanup assertions.
- `207e723c` commits the independently reviewed 11-file raster optimization after
  clean verification. Its exact geometry keys include all polygon boundaries;
  pin corridors, regions and ownership are rebuilt for every private graph.
  The byte cap is per grid/cache, not a global memory guarantee across snapshots.

- `tests-20261027-155346-60876.log`: all 203 selected tests pass, zero failures
  or skips. This includes all eight final review regressions, caller-cancellation
  controls, coordinate process/interpreter diagnostics, shell activation, export
  settings, real DI retirement, dialog close/reopen and clean raster reuse.
  `96fbc9ef` commits the reviewed fixes and expands all four native OS jobs with
  twelve coordinate process/interpreter cases. All three embedded CI verifier
  scripts compile; production timeout policy is unchanged.
- Final runtime verification started on `96fbc9ef`: Heavy-inclusive manual xUnit
  run `40526201954`, PR xUnit `40526212184`, native matrix `40526212227`, architecture
  `40526212253`. Fresh Windows Release publish and Browser Release build both pass;
  published dependencies are Avalonia/Desktop 11.3.13 and MathNet 5.0.0, with one
  MathNet DLL and all nine checked example/Python runtime assets present.
- Fresh published desktop started successfully (Lunima window, responding process).
  Native Computer Use is unavailable: missing native pipe after a retry and kernel
  reset. No interactive click-through is claimed. Existing headless UI regressions
  remain required; a full local non-Slow/non-Heavy run is in progress separately.
