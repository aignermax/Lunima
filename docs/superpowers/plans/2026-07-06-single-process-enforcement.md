# Single-Process Enforcement Implementation Plan

> Historical July 2026 planning record. Code examples describe that baseline, not the current architecture.
> Current rules: [Architecture](../../../ARCHITECTURE.md) and [Codebase map](../../CODEBASE_MAP.md).

The complete original plan is retained in two responsibility-focused documents:

- [Core process policy and PDK metadata](2026-07-06-single-process-enforcement-core-and-pdk.md)
- [Persistence, interface and enforcement](2026-07-06-single-process-enforcement-persistence-and-ui.md)

The original section headings below preserve existing links to this plan.

## Global Constraints

[Read this section in the historical plan](2026-07-06-single-process-enforcement-core-and-pdk.md).

## Phase 1 — Core process logic (pure, no UI)

[Read this section in the historical plan](2026-07-06-single-process-enforcement-core-and-pdk.md).

### Task 1: `ProcessFingerprint` + `ProcessCompatibility`

[Read this section in the historical plan](2026-07-06-single-process-enforcement-core-and-pdk.md).

### Task 2: `ProcessCatalog` — group compatible PDKs

[Read this section in the historical plan](2026-07-06-single-process-enforcement-core-and-pdk.md).

### Task 3: `ActiveProcessSelection` + `SingleProcessPolicy`

[Read this section in the historical plan](2026-07-06-single-process-enforcement-core-and-pdk.md).

## Phase 2 — PDK metadata extraction (DataAccess)

[Read this section in the historical plan](2026-07-06-single-process-enforcement-core-and-pdk.md).

### Task 4: `ProcessDefinition.CoreThicknessNm` + `ProcessFingerprintFactory`

[Read this section in the historical plan](2026-07-06-single-process-enforcement-core-and-pdk.md).

### Task 5: Populate bundled PDK process blocks

[Read this section in the historical plan](2026-07-06-single-process-enforcement-core-and-pdk.md).

## Phase 3 — Persistence & active-process state

[Read this section in the historical plan](2026-07-06-single-process-enforcement-persistence-and-ui.md).

### Task 6: `.lun` persistence — `ActiveProcessData` + save/load/migrate

[Read this section in the historical plan](2026-07-06-single-process-enforcement-persistence-and-ui.md).

### Task 7: Wire active process into `FileOperationsViewModel`

[Read this section in the historical plan](2026-07-06-single-process-enforcement-persistence-and-ui.md).

## Phase 4 — UI: selection, indicator, library filter

[Read this section in the historical plan](2026-07-06-single-process-enforcement-persistence-and-ui.md).

### Task 8: New-Design process-selection dialog

[Read this section in the historical plan](2026-07-06-single-process-enforcement-persistence-and-ui.md).

### Task 9: New-Design flow + active-process indicator (MainViewModel)

[Read this section in the historical plan](2026-07-06-single-process-enforcement-persistence-and-ui.md).

### Task 10: Library filter follows the active process

[Read this section in the historical plan](2026-07-06-single-process-enforcement-persistence-and-ui.md).

## Phase 5 — Enforcement wiring

[Read this section in the historical plan](2026-07-06-single-process-enforcement-persistence-and-ui.md).

### Task 11: Enforce at placement and paste

[Read this section in the historical plan](2026-07-06-single-process-enforcement-persistence-and-ui.md).

## Phase 6 — Finish

[Read this section in the historical plan](2026-07-06-single-process-enforcement-persistence-and-ui.md).

### Task 12: Full suite, PR, close #602

[Read this section in the historical plan](2026-07-06-single-process-enforcement-persistence-and-ui.md).

## Self-Review notes (author)

[Read this section in the historical plan](2026-07-06-single-process-enforcement-persistence-and-ui.md).

