# Connect-A-PIC-Pro: Comprehensive Architecture Analysis

**Issue:** #320
**Date:** 2026-03-28 (updated 2026-03-29)
**Analyst:** Autonomous Agent (Claude Sonnet 4.6)
**Maturity Score:** 4/5

---

## Executive Summary

Connect-A-PIC-Pro has a **well-structured, modular architecture** that successfully follows its
own CLAUDE.md guidelines. The project is at **maturity level 4/5** — functional, organized, and
testable, with clear paths for further improvement.

**PRISM migration is NOT recommended.** The current 28-ViewModel, 14-service architecture is
well-served by CommunityToolkit.Mvvm + manual DI. Hybrid modularization (Option C) delivers
80% of PRISM's benefits with 20% of the migration cost.

**Top 3 improvements (ordered by impact):**

1. ✅ **COMPLETED (Issue #365)** Extract `MainWindow.axaml` panels into `UserControl` files — reduced 951 → 819 lines
2. Remove backward-compatibility delegates from `MainViewModel` (once AXAML bindings updated)
3. Split `DesignCanvasViewModel` (1,562 lines, 7 partial files) into focused sub-ViewModels

---

## 1. Current Architecture Assessment

### 1.1 File Structure Summary

| Layer | Files | Notes |
|-------|-------|-------|
| `CAP.Avalonia/ViewModels/` | 34 VMs in 8 subfolders | Well-organized per CLAUDE.md |
| `CAP.Avalonia/Views/` | 10 files (root only) | **No subfolders yet — extraction needed** |
| `Connect-A-Pic-Core/` | ~128 files across 10 modules | Proper domain separation |
| `UnitTests/` | 163 test files in 15+ folders | Excellent coverage |

### 1.2 Key File Metrics

| File | Lines | Status |
|------|-------|--------|
| `MainViewModel.cs` | 654 | Acceptable coordinator; ~150 lines are backward-compat delegates |
| `MainWindow.axaml` | 819 (was 951) | Partially extracted — 5 panels moved to `Views/Panels/` (Issue #365) |
| `DesignCanvasViewModel.cs` | 1,562 (7 partial files) | Manageable via partials; could split further |
| `App.axaml.cs` | 68 | Clean DI registration |
| `DesignCanvas.MouseHandling.cs` | 873 | **Complex** — could extract further |

### 1.3 Dependency Map

```
App.axaml.cs (14 DI registrations)
  └── MainViewModel (coordinator)
        ├── CanvasInteractionViewModel  ──► DesignCanvasViewModel
        ├── FileOperationsViewModel     ──► DesignCanvasViewModel, CommandManager
        ├── ViewportControlViewModel    ──► DesignCanvasViewModel
        ├── LeftPanelViewModel          ──► DesignCanvasViewModel, GroupLibraryManager, PdkLoader
        │     ├── ComponentLibraryViewModel
        │     ├── PdkManagerViewModel
        │     └── HierarchyPanelViewModel
        ├── RightPanelViewModel         ──► DesignCanvasViewModel
        │     ├── ParameterSweepViewModel
        │     ├── RoutingDiagnosticsViewModel
        │     ├── DesignValidationViewModel
        │     ├── ComponentDimensionDiagnosticsViewModel
        │     ├── ComponentDimensionViewModel
        │     ├── ExportValidationViewModel
        │     ├── SMatrixPerformanceViewModel
        │     ├── CompressLayoutViewModel
        │     ├── GroupSMatrixViewModel
        │     └── ArchitectureReportViewModel (NEW — Issue #320)
        └── BottomPanelViewModel        ──► DesignCanvasViewModel, CommandManager
              ├── ElementLockViewModel
              ├── WaveguideLengthViewModel
              └── ErrorConsoleViewModel
```

**Coupling assessment:** `DesignCanvasViewModel` is the central dependency shared across all
panel VMs — this is expected and correct for a canvas-first application. No circular
dependencies detected.

---

## 2. SOLID Principles Analysis

### Single Responsibility Principle (SRP)

| Class | Responsibilities | Verdict |
|-------|-----------------|---------|
| `MainViewModel` | Orchestrator + backward-compat delegates + simulation trigger | **Borderline** — 150 lines of backward-compat delegates inflate it |
| `DesignCanvasViewModel` | Canvas state, simulation data, grid, component management | **Violation** — too many responsibilities despite partial file split |
| `CanvasInteractionViewModel` | User input + selection + placement | **OK** — focused |
| `RightPanelViewModel` | Right sidebar composition | **OK** — pure aggregator |
| `DesignCanvas.MouseHandling.cs` | Mouse events | **OK** — single concern per partial file |

**Recommendation:** Extract canvas state management from `DesignCanvasViewModel` into
`CanvasStateService` (components, connections, grid) and keep `DesignCanvasViewModel` as the
simulation + rendering coordinator.

### Open/Closed Principle (OCP)

**Strength:** The panel composition pattern (`LeftPanelViewModel`, `RightPanelViewModel`,
`BottomPanelViewModel`) means adding a new feature only requires:
1. Creating a new ViewModel in the appropriate subfolder
2. Adding one property to the parent panel VM
3. Adding one panel section to `MainWindow.axaml`

**Weakness:** `MainWindow.axaml` must be modified for every new panel — this is inherent to
static AXAML composition. Extracting panels into `UserControl` files mitigates merge conflicts.

### Liskov Substitution Principle (LSP)

No inheritance hierarchies at risk. The project correctly uses:
- `ObservableObject` as a stable base class (CommunityToolkit.Mvvm — well-tested)
- No deep inheritance chains in Core or ViewModels

### Interface Segregation Principle (ISP)

Per CLAUDE.md: "only create interfaces when multiple implementations exist."
Current interfaces: `IDataAccessor`, `IInputDialogService`, `IFileDialogService`
All are appropriately scoped. No fat interfaces detected.

### Dependency Inversion Principle (DIP)

**Strength:** Core services injected via constructor DI in `MainViewModel` — clean.
**Weakness:** Sub-ViewModels (`ParameterSweepViewModel`, `RoutingDiagnosticsViewModel`, etc.)
are instantiated directly inside panel VMs (`new ParameterSweepViewModel(...)`) rather than
injected. This works at the current scale but limits testability slightly.

---

## 3. Scalability Analysis

### Current State: ~28 Feature ViewModels, 14 DI Services

| Dimension | Current | At 50 Features | Risk |
|-----------|---------|----------------|------|
| `MainViewModel` lines | 654 | ~1,000+ | MEDIUM — backward-compat delegates grow linearly |
| `MainWindow.axaml` lines | 1,117 | ~2,000+ | HIGH — merge conflicts, readability |
| DI registrations | 14 | ~25-30 | LOW — simple to manage |
| Build time | Fast | Moderate | LOW — Avalonia compiles AXAML lazily |
| Test isolation | Good | Good | LOW — 163 files already well-organized |
| Team merge conflicts | Low | MEDIUM | If multiple devs touch `MainWindow.axaml` simultaneously |

**Key insight:** The architectural bottleneck is `MainWindow.axaml`, not `MainViewModel`.
Extracting panels into `UserControl` files is the highest-leverage improvement.

---

## 4. Alternative Architecture Options

### Option A: Feature Folder Structure (Vertical Slices)

**Structure:**
```
CAP.Avalonia/Features/
  ParameterSweep/
    ParameterSweepViewModel.cs
    ParameterSweepView.axaml
  GdsExport/
    GdsExportViewModel.cs
    GdsExportView.axaml
  ...
```

| | Detail |
|--|--------|
| **Effort** | HIGH — ~80+ files to move/rename, AXAML namespace changes |
| **Risk** | MEDIUM — git history discontinuity, potential missed references |
| **Benefit** | Better file co-location per feature; easier to delete a feature |
| **Verdict** | **OPTIONAL** — current subfolder organization delivers similar benefits |

The current `ViewModels/Analysis/`, `ViewModels/Canvas/`, `ViewModels/Diagnostics/` subfolders
already provide ~70% of feature co-location benefits. Full vertical slice migration has
diminishing returns at the current scale.

### Option B: PRISM Framework Migration

| | Detail |
|--|--------|
| **Effort** | VERY HIGH — 6-12 weeks, touches nearly every file |
| **Risk** | HIGH — framework learning curve, Avalonia PRISM maturity concerns |
| **Benefit** | Region-based UI injection, EventAggregator, module loading |
| **Verdict** | **NOT RECOMMENDED** — overkill for 28 ViewModels |

**Concrete PRISM benefits for this project:**
- `EventAggregator` would replace callback wiring in `MainViewModel` (e.g., `OnSelectionChanged`, `UpdateStatus`)
- `RegionManager` would replace static AXAML panel composition in `MainWindow.axaml`
- Module loading would enable plugin-style PDK extensions

**Why NOT now:** These benefits are real but achievable without PRISM. A simple
`IMessageBus` (e.g., via `WeakReferenceMessenger` from CommunityToolkit) replaces
`EventAggregator`. Panel extraction into `UserControl` replaces region management.
The migration cost outweighs the benefit until the project exceeds 40+ features.

### Option C: Hybrid Incremental Modularization (RECOMMENDED)

Keep CommunityToolkit.Mvvm. Apply targeted improvements over 3 phases.

---

## 5. Recommended Action Plan

### Phase 1 — Quick Wins (1-2 days, LOW risk)

**1.1 Extract right panel sections into UserControls** ✅ COMPLETED — Issue #365 (2026-03-29)

Created `CAP.Avalonia/Views/Panels/` with 5 extracted UserControls:
```
Views/Panels/
  LayoutCompressionPanel.axaml
  DesignChecksPanel.axaml
  RoutingDiagnosticsPanel.axaml
  ComponentDimensionDiagnosticsPanel.axaml
  GdsExportPanel.axaml
```

All panels use `x:DataType="vm:MainViewModel"` and inherit DataContext from MainWindow.
`MainWindow.axaml` reduced from 951 → 819 lines (−132 lines). 1312 tests passing.

Remaining candidates for further extraction: ParameterSweepPanel, WaveguideLengthPanel,
LockElementsPanel (all have cross-VM binding dependencies that require additional refactoring).

**1.2 Remove backward-compatibility delegates from MainViewModel**

`MainViewModel` currently has ~15 passthrough properties like:
```csharp
public ParameterSweepViewModel Sweep => RightPanel.Sweep;  // backward-compat
```

These exist because `MainWindow.axaml` uses `{Binding Sweep.*}` instead of
`{Binding RightPanel.Sweep.*}`. Once AXAML bindings are updated to use `RightPanel.Sweep.*`,
these delegates can be removed, reducing `MainViewModel` from 654 → ~450 lines.

### Phase 2 — Structural Improvements (3-5 days, MEDIUM risk)

**2.1 Add WeakReferenceMessenger for cross-feature communication**

Replace callback wiring in `MainViewModel` with typed messages:
```csharp
// Instead of: CanvasInteraction.OnSelectionChanged = comp => { ... }
// Use: WeakReferenceMessenger.Default.Register<ComponentSelectedMessage>(this, ...)
```

`CommunityToolkit.Mvvm` already includes `WeakReferenceMessenger` — no new dependency.

**2.2 Split DesignCanvasViewModel responsibilities**

Current: `DesignCanvasViewModel` manages components, connections, simulation state, grid,
waveguide visualization, and power flow.

Proposal (gradual, using partial classes as intermediate step):
- `CanvasComponentsViewModel` — component collection, add/remove/move
- `CanvasConnectionsViewModel` — connection collection, waveguide routing
- `CanvasSimulationStateViewModel` — power flow, S-matrix results, visualization

`DesignCanvasViewModel` becomes a facade delegating to these sub-VMs.

### Phase 3 — Optional (4-6 weeks, LOW urgency)

**3.1 Revisit PRISM if feature count exceeds 40**

If the project grows to 40+ features:
- Evaluate `Prism.Avalonia` maturity (watch GitHub for stable release)
- Replace `MainViewModel` wiring with `EventAggregator`
- Replace static AXAML panels with `RegionManager` regions

**3.2 Consider plugin/PDK module loading**

If external PDK contributors are expected, a module loading system (PRISM modules or custom
`IPdkModule` interface) would allow PDK-specific ViewModels and Views to self-register.

---

## 6. Code Quality Findings

### High-Priority (fix in next sprint)

| File | Issue | Recommendation |
|------|-------|----------------|
| `MainWindow.axaml:1` | 819 lines — partially extracted (Issue #365) | Continue extracting ParameterSweep, WaveguideLength, LockElements panels |
| `DesignCanvas.MouseHandling.cs:873` | Complex mouse handling with >5 responsibilities | Extract gesture recognizers |
| `MainViewModel.cs:67-85` | 15 backward-compat delegates with TODO comments | Update AXAML bindings and remove |

### Medium-Priority (monitor)

| File | Issue | Recommendation |
|------|-------|----------------|
| `DesignCanvasViewModel.cs:1562` | Very large even split across 7 partials | Consider sub-ViewModel extraction |
| `Connect-A-Pic-Core/Analysis/` | 11 files at folder limit | Create `Sweep/` subfolder for sweep-specific files |
| `RightPanelViewModel.cs` | 10 feature VMs in one panel — will grow | Consider collapsible section registration pattern |

### Low-Priority (future consideration)

| File | Issue | Recommendation |
|------|-------|----------------|
| `App.axaml.cs` | Sub-ViewModels not DI-registered | Register panel VMs in DI for better testability |
| `MainViewModel` constructor | File loading callback (`LeftPanel.OnGroupTemplateSelected`) is 25 lines inline | Extract to named method |

---

## 7. Test Architecture Quality

**Current:** 163 test files, excellent coverage across all layers.

**Strengths:**
- Clean separation: unit tests vs integration tests in dedicated folders
- Commands tested independently (17 files) — enables undo/redo confidence
- Persistence tests present (3 files) — save/load roundtrips protected

**Gap identified:** Sub-ViewModels inside panel VMs (e.g., `ParameterSweepViewModel` inside
`RightPanelViewModel`) are tested via `new ParameterSweepViewModel()` directly. If sub-VMs
were DI-registered, they could be mocked in integration tests for better isolation.

---

## 8. Summary Table

| Concern | Severity | Phase | Estimated Effort |
|---------|----------|-------|-----------------|
| `MainWindow.axaml` too large | MEDIUM (partial) | 1 | ✅ Partially done (Issue #365) |
| Backward-compat delegates in MainViewModel | MEDIUM | 1 | 0.5 days |
| DesignCanvasViewModel too large | MEDIUM | 2 | 2-3 days |
| Cross-feature callback wiring | LOW | 2 | 1 day |
| PRISM migration | N/A | 3 (if needed) | 6-12 weeks |

**Overall Recommendation:** The architecture is healthy. No emergency refactoring needed.
Phase 1 improvements (panel extraction + delegate cleanup) are the highest-ROI investments and
can be done incrementally without risk.

---

*This analysis was produced as part of Issue #320. The architecture report panel (see right sidebar
→ Architecture Report) provides live access to the key metrics summarized here.*
