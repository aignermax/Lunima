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

- [x] Add regression for 130 nonmergeable commands with crossing metadata; undo retained entries and compare exact topology/order.
- [x] Coordinator runs focused wrapper test and observes expected failure.
- [x] Implement precise retirement; test undo/redo/merged/pending/reset cases.
- [x] Run related routing/history regressions, review diff, fix findings, commit.

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

## Current acceptance status

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

| Finding | Status |
|---|---|
| A01 | Resolved, reviewed, 124-case combined verification |
| A02 | Owned-process containment verified on Windows, Linux and macOS (native CI 39940594173); final integration review pending |
| A03 | Bounded cache, warm lookup and PDK edit invalidation verified; final combined review pending |
| A04 | Detached numeric/formula snapshots, UI integration, stale-error rejection and prepared visualization verified against legacy physics; final integration review pending |
| A05 | Export split committed; main/file/input/window extraction implemented, combined verification in progress |
| A06 | Solver, update, MRU, PDK library/reader/writer and diagnostic ports verified; remaining actual dependency edges still require remediation |
| A07 | Semantic collector and explicit repository/external ownership tests pass; warning inventory remains opt-in until actual forbidden edges are removed |
| A08 | Shared runtime verified; fresh desktop publish passed before shell extraction, browser pending |
| A09 | All 22 command budgets pass at the unchanged 100 ms threshold; exact raster reuse and corrected acknowledgement timing committed as fe232861, final integration review pending |
| A10 | Admission/draining gate verified; real commit-boundary integration pending |
| A11 | Pinned uv, authenticated updater download and signed publisher tooling verified; production public-key provisioning and installer handoff review pending |
| A12 | Checked-in runner/docs implemented; 23 tool tests pass |
| A13 | April report removed, active docs reconciled; update as extractions land |
| A14 | Hook/CI guard implemented; tracked source ceiling passes, new files/final legacy guard replacement pending |
| A15 | Main/window, updater, Python, solver scopes, application registry and thumbnail retirement verified; component editor/settings and other direct panel owners remain |
| A16 | Binary/SDK cleanup verified with fresh desktop publish; final branch/platform checks pending |
| A17 | Native DTO extraction verified; persistence workflow extraction undergoing combined regression |
| A18 | Root metadata/native I/O guards and canonical group wires verified; private routing including crossing records/prototypes passes comparison tests; actual UI worker integration, command publication and long-lived configuration admission remain in progress |

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
