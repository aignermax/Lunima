# GDS interpretation implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Provide a native, reviewable GDS import with explicit layer roles, component and port interpretation, persistence and contextual guidance.

**Architecture:** Keep interpretation DTOs and transformations in CAP-DataAccess/Import/Gds. Keep orchestration and project persistence in the existing GdsImport service and DesignScope. Use a focused review ViewModel/control composed into the existing import dialog; reuse its cancellation, placement and reporting.

**Tech Stack:** C#, Avalonia, CommunityToolkit MVVM, xUnit/Avalonia.Headless.

**Spec:** docs/superpowers/specs/2026-10-20-gds-interpretation-design.md

## Global constraints

- Preserve source geometry, legacy defaults and cross-platform behavior.
- Explicit roles override automatic fallbacks.
- Keep new source files under 500 lines; do not expand unrelated architecture.
- Serialize local dotnet runs; use focused tests, not the unfiltered suite.
- Reuse Lunima resources and TourAnchorOverlay; localize new copy.

## Review focus

- A layer carrying both marker shapes and names must support both meanings.
- Unrecognized annotation layers must not create fake ports.
- Repeated/mirrored cells must retain geometry and consistent pin coordinates.
- Editing a confirmed interpretation must invalidate its confirmation.
- Closing/cancelling must not commit a partly reviewed interpretation.

### Task 1: Explicit role mapping

Files: CAP-DataAccess/Import/Gds/GdsLayerRoleMapping.cs, GdsPinDetectionOptions.cs, detector/session helpers; UnitTests/Import/Gds.

- [x] Add mapping and strict-fallback regression tests; observe failures.
- [x] Implement role flags, assignments, separate marker/label semantics and validation.
- [x] Verify existing detector behavior stays compatible.

### Task 2: Interpretation and persistence

Files: CAP-DataAccess/Import/Gds/Interpretation; CAP.Avalonia/Services/GdsImport and DesignScope.

- [x] Define immutable/snapshot review data with source/top-cell identity.
- [x] Test hierarchy decisions, port edits and explicit model/geometry-only behavior.
- [x] Apply interpretation before placement; retain original source and record decisions in .lun.
- [x] Test capture/restore and repeated import.

### Task 3: Native review UI

Files: CAP.Avalonia/ViewModels/GdsImport/Interpretation; CAP.Avalonia/Controls/GdsImport; import dialog composition.

- [x] Build role rows, confirmation/undo and linked original-geometry preview.
- [x] Compose hierarchy, ports, model and summary steps over backend decisions.
- [x] Add explicit reusable profiles and stale-confirmation handling.
- [x] Exercise actual import command with the reviewed snapshot.

### Task 4: Guided introduction

- [x] Point the existing green cursor at the active review action.
- [x] Support skip/pause/restart and stop animation on close.
- [x] Localize copy and test real EN/DE interaction targets.

### Task 5: End-to-end verification and handoff

- [x] Run focused integration, persistence, UI and architecture tests.
- [x] Build native desktop and inspect rendered screenshots.
- [x] Independent PR review; fix findings and rerun affected tests.
- [x] Push PR and launch the reviewed app without discarding the user's running work.

## Verification and review record

- 637 focused import, persistence, native UI, localization and architecture tests passed (Heavy excluded).
- Final native dialog and actual GDS-file rendering check: 7/7 passed after test preference isolation.
- Release desktop build passed with zero errors; existing analyzer warnings remain.
- Independent whole-feature review found three issues: flat-root component loss, eager/unbounded preview preparation, and incompatible model domains accepted before dialog close. All addressed with regressions.
- Flat roots use black-box component import. Large previews use bounded cancellable traversal, cached exact bounds and lazily loaded cell scenes. Incomplete previews do not infer ports. Model domains and case-insensitive port uniqueness are checked before apply; service rejection preserves the open review.
- Original GDS bytes and reviewed interpretation survive project capture/restore. A modified source file cannot replace the analyzed snapshot.
- Rapid top-cell switching race and stale undo detection state fixed.
- Native six-stage view rendered and inspected; real gdsfactory_mzi_like.gds fixture also rendered. Desktop build launched. Windows automation cannot bind its window because the installed app aliases the same executable identity; direct window interaction was not claimed verified.

## Scope decisions

- Component splitting follows existing GDS cell hierarchy; arbitrary polygon cutting is not implemented. Logical groups include all instances of chosen cell types.
- Simulation mapping requires a persisted explicit S-matrix model. Geometry-only imports deliberately block simulation.
- English and German copy translated; other existing locales use English fallback for new review strings.
- Native review preparation uses bounded source traversal rather than the legacy eager suggestions. The original source remains complete.
