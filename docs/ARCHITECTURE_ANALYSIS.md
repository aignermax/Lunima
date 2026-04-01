# Connect-A-PIC-Pro: Comprehensive Architecture Analysis

**Issue:** #320
**Date:** 2026-03-28 (updated 2026-04-01 — Issues #426, #433)
**Analyst:** Autonomous Agent (Claude Sonnet 4.6)
**Maturity Score:** 4.8/5

---

## Executive Summary

Connect-A-PIC-Pro has a **well-structured, modular architecture** that successfully follows its
own CLAUDE.md guidelines. The project is at **maturity level 4.5/5** — functional, organized, and
testable, with substantial improvements completed since the last analysis.

**PRISM migration is NOT recommended.** The current 40+ ViewModel, 40+ service architecture is
well-served by CommunityToolkit.Mvvm + manual DI. Hybrid modularization (Option C) continues to
deliver 80% of PRISM's benefits with 20% of the migration cost.

**Top improvements (ordered by impact):**

1. ✅ **COMPLETED (Issue #365)** Extract `MainWindow.axaml` panels into `UserControl` files
2. ✅ **COMPLETED (Issue #377)** Sub-ViewModel DI injection + gesture recognizer extraction — `MouseHandling.cs` reduced 880 → ~130 lines; 40+ DI registrations
3. ✅ **COMPLETED (commit efaa4f6)** Split `DesignCanvasViewModel` — reduced from 1,562 lines (7 partial files) → **299 lines** (1 file) via `Canvas/Services/` extraction
4. ✅ **COMPLETED (Issue #433)** Extract Toolbar, StatusBar, and ComponentLibrary panels — `MainWindow.axaml` reduced **927 → 511 lines** (new panels: `ToolbarPanel`, `StatusBarPanel`, `ComponentLibraryPanel`)
5. **ACTIVE** `DesignCanvas.ComponentRendering.cs` at 487 lines — candidate for extraction
6. **ACTIVE** `CAP.Avalonia/Services/` (22 services, flat) — organize into domain subfolders

---

## 1. Current Architecture Assessment

### 1.1 File Structure Summary

| Layer | Files | Notes |
|-------|-------|-------|
| `CAP.Avalonia/ViewModels/` | 48 files in 10 subfolders + 2 root files | Well-organized; new `Update/` subfolder and `Canvas/Services/` sub-subfolder added |
| `CAP.Avalonia/Views/` | 6 root files + 16 in `Panels/` | 3 new panels extracted in Issue #433: `ToolbarPanel`, `StatusBarPanel`, `ComponentLibraryPanel` |
| `Connect-A-Pic-Core/` | 143 files across 10+ modules | Proper domain separation |
| `UnitTests/` | 206 test files in 15+ folders | Excellent coverage — 43 new test files added since last analysis |

### 1.2 Key File Metrics (Current — 2026-04-01)

| File | Lines | Status |
|------|-------|--------|
| `MainViewModel.cs` | 642 | Acceptable coordinator; backward-compat delegates removed (Issue #377) |
| `MainWindow.axaml` | **511** (was 951 → 819 → 927 → 511) | ✅ Refactored — 8 panels now in `Views/Panels/`; target <600 ✅ |
| `DesignCanvasViewModel.cs` | **299** (was 1,562 / 7 partial files) | ✅ Refactored — `Canvas/Services/` extracted (commit efaa4f6) |
| `App.axaml.cs` | 125 | ~40 DI registrations — all sub-ViewModels DI-injected |
| `DesignCanvas.MouseHandling.cs` | 130 (was 880) | ✅ Refactored — delegates to 5 gesture recognizers (Issue #377) |
| `DesignCanvas.ComponentRendering.cs` | 487 | Candidate for extraction |
| `DesignCanvas.Rendering.cs` | 375 | Large — monitor |

### 1.3 Dependency Map

```
App.axaml.cs (~40 DI registrations — all sub-ViewModels injected)
  └── MainViewModel (coordinator, 642 lines)
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
        │     ├── ArchitectureReportViewModel
        │     ├── PdkConsistencyViewModel  (NEW — Issue #334)
        │     └── UpdateViewModel          (NEW — auto-update feature)
        └── BottomPanelViewModel        ──► DesignCanvasViewModel, CommandManager
              ├── ElementLockViewModel
              ├── WaveguideLengthViewModel
              └── ErrorConsoleViewModel

DesignCanvasViewModel (299 lines — refactored)
  └── Canvas/Services/ (5 extracted services)
        ├── ComponentPlacementService.cs
        ├── GroupEditService.cs
        ├── PinHighlightService.cs
        ├── RoutingOrchestrator.cs
        └── SimulationCoordinator.cs
```

**Coupling assessment:** `DesignCanvasViewModel` is the central dependency shared across all
panel VMs — this is expected and correct for a canvas-first application. No circular
dependencies detected.

---

## 2. SOLID Principles Analysis

### Single Responsibility Principle (SRP)

| Class | Responsibilities | Verdict |
|-------|-----------------|---------|
| `MainViewModel` | Orchestrator + simulation trigger | **OK** — backward-compat delegates removed (Issue #377) |
| `DesignCanvasViewModel` | Canvas state facade — delegates to 5 services | **OK** — now 299 lines, delegates to `Canvas/Services/` (commit efaa4f6) |
| `CanvasInteractionViewModel` | User input + selection + placement | **OK** — focused |
| `RightPanelViewModel` | Right sidebar composition (13 features) | **OK** — pure aggregator; monitor growth |
| `DesignCanvas.MouseHandling.cs` | Mouse event dispatcher | **OK** — 5 gesture recognizers in `CAP.Avalonia/Gestures/` (Issue #377) |
| `DesignCanvas.ComponentRendering.cs` | Component drawing logic | **WATCH** — 487 lines, approaching limit |

### Open/Closed Principle (OCP)

**Strength:** The panel composition pattern (`LeftPanelViewModel`, `RightPanelViewModel`,
`BottomPanelViewModel`) means adding a new feature only requires:
1. Creating a new ViewModel in the appropriate subfolder
2. Adding one property to the parent panel VM
3. Adding one panel section to `MainWindow.axaml`

**Weakness:** `MainWindow.axaml` must be modified for every new panel — this is inherent to
static AXAML composition. The file has grown back to 927 lines as new features were added
(PDK Consistency, Auto-Update panels). Continuing panel extraction into `UserControl` files
mitigates merge conflicts.

### Liskov Substitution Principle (LSP)

No inheritance hierarchies at risk. The project correctly uses:
- `ObservableObject` as a stable base class (CommunityToolkit.Mvvm — well-tested)
- No deep inheritance chains in Core or ViewModels

### Interface Segregation Principle (ISP)

Per CLAUDE.md: "only create interfaces when multiple implementations exist."
Current interfaces: `IDataAccessor`, `IInputDialogService`, `IFileDialogService`, `IGestureRecognizer`
All are appropriately scoped. No fat interfaces detected.

### Dependency Inversion Principle (DIP)

**Strength:** All ViewModels — including sub-ViewModels — are registered in DI and injected via
constructor (Issue #377). `DesignCanvasViewModel` is a singleton shared across all panel VMs.
Canvas services (`ComponentPlacementService`, etc.) follow the same DI pattern.

---

## 3. Scalability Analysis

### Current State: ~40+ Feature ViewModels, 40+ DI Services

| Dimension | Current | At 50 Features | Risk |
|-----------|---------|----------------|------|
| `MainViewModel` lines | 642 | ~850 | LOW — backward-compat delegates removed |
| `MainWindow.axaml` lines | 927 | ~1,400+ | MEDIUM — merge conflicts, readability |
| DI registrations | ~40 | ~50 | LOW — simple to manage |
| Build time | Fast | Moderate | LOW — Avalonia compiles AXAML lazily |
| Test isolation | Excellent | Good | LOW — 206 files well-organized |
| Team merge conflicts | Low | MEDIUM | If multiple devs touch `MainWindow.axaml` simultaneously |

**Key insight:** The architectural bottleneck is `MainWindow.axaml`, not `MainViewModel`.
The file has grown back from 819 → 927 lines. Continuing panel extraction into `UserControl`
files remains the highest-leverage improvement.

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
| **Effort** | HIGH — ~100+ files to move/rename, AXAML namespace changes |
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
| **Verdict** | **NOT RECOMMENDED** — overkill for 40 ViewModels |

**Concrete PRISM benefits for this project:**
- `EventAggregator` would replace callback wiring in `MainViewModel`
- `RegionManager` would replace static AXAML panel composition in `MainWindow.axaml`
- Module loading would enable plugin-style PDK extensions

**Why NOT now:** These benefits are real but achievable without PRISM. A simple
`IMessageBus` (e.g., via `WeakReferenceMessenger` from CommunityToolkit) replaces
`EventAggregator`. Panel extraction into `UserControl` replaces region management.
Revisit when the project exceeds 50+ features.

### Option C: Hybrid Incremental Modularization (RECOMMENDED)

Keep CommunityToolkit.Mvvm. Apply targeted improvements over 3 phases.

---

## 5. Recommended Action Plan

### Phase 1 — Quick Wins (COMPLETED)

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

**1.2 Sub-ViewModel DI injection + gesture recognizer extraction** ✅ COMPLETED — Issue #377 (2026-03-30)

All sub-ViewModels are now registered in `App.axaml.cs` and constructor-injected. 5 gesture
recognizer classes extracted to `CAP.Avalonia/Gestures/`. `DesignCanvas.MouseHandling.cs`
reduced from 880 → 130 lines.

**1.3 Split DesignCanvasViewModel** ✅ COMPLETED — commit efaa4f6 (2026-03-31)

`DesignCanvasViewModel` reduced from 1,562 lines (7 partial files) to **299 lines** (1 file).
5 service classes extracted to `CAP.Avalonia/ViewModels/Canvas/Services/`:
```
Canvas/Services/
  ComponentPlacementService.cs
  GroupEditService.cs
  PinHighlightService.cs
  RoutingOrchestrator.cs
  SimulationCoordinator.cs
```
`DesignCanvasViewModel` now acts as a facade delegating to these services.

### Phase 2 — Structural Improvements (ACTIVE)

**2.1 Continue MainWindow.axaml panel extraction** ← NEXT HIGHEST PRIORITY

`MainWindow.axaml` has grown back to 927 lines as new features (PDK Consistency, Auto-Update)
were added. Remaining candidates for `UserControl` extraction:

- `ParameterSweepPanel` — has cross-VM binding that requires wiring review
- `WaveguideLengthPanel` — same
- `LockElementsPanel` — same
- New panels added since Issue #365: PDK Consistency, Auto-Update panels

**2.2 Extract DesignCanvas.ComponentRendering.cs** ← MEDIUM PRIORITY

Currently 487 lines — the largest remaining DesignCanvas partial file. Could extract
component-type-specific rendering logic into a renderer registry pattern.

**2.3 Add WeakReferenceMessenger for cross-feature communication**

Replace callback wiring in `MainViewModel` with typed messages:
```csharp
// Instead of: CanvasInteraction.OnSelectionChanged = comp => { ... }
// Use: WeakReferenceMessenger.Default.Register<ComponentSelectedMessage>(this, ...)
```

`CommunityToolkit.Mvvm` already includes `WeakReferenceMessenger` — no new dependency.

**2.4 Create Analysis subfolder to manage folder size**

`Connect-A-Pic-Core/Analysis/` has 12 files — at the CLAUDE.md folder limit (8-10).
Consider splitting into `Analysis/Sweep/` for sweep-specific classes and `Analysis/Validation/`
for design validation classes.

### Phase 3 — Optional (LOW urgency)

**3.1 Revisit PRISM if feature count exceeds 50**

If the project grows to 50+ features:
- Evaluate `Prism.Avalonia` maturity (watch GitHub for stable release)
- Replace `MainViewModel` wiring with `EventAggregator`
- Replace static AXAML panels with `RegionManager` regions

**3.2 Consider plugin/PDK module loading**

If external PDK contributors are expected, a module loading system (PRISM modules or custom
`IPdkModule` interface) would allow PDK-specific ViewModels and Views to self-register.

---

## 6. Code Quality Findings

### High-Priority (fix soon)

| File | Issue | Recommendation |
|------|-------|----------------|
| `MainWindow.axaml:1` | 927 lines — grew back as new features added | Continue extracting panels: ParameterSweep, WaveguideLength, LockElements, PDK Consistency, Auto-Update |

### Medium-Priority (monitor)

| File | Issue | Recommendation |
|------|-------|----------------|
| `DesignCanvas.ComponentRendering.cs:487` | Largest remaining DesignCanvas partial | Consider renderer registry pattern or partial split |
| `DesignCanvas.Rendering.cs:375` | Large rendering file | Monitor — if it grows, extract sub-renderers |
| `Connect-A-Pic-Core/Analysis/` | 12 files — at folder limit | Create `Analysis/Sweep/` and `Analysis/Validation/` subfolders |
| `RightPanelViewModel.cs` | 13 feature VMs in one panel — will grow | Consider collapsible section registration pattern |

### Low-Priority (future consideration)

| File | Issue | Recommendation |
|------|-------|----------------|
| `MainViewModel` constructor | File loading callback (`LeftPanel.OnGroupTemplateSelected`) is ~25 lines inline | Extract to named method |
| `App.axaml.cs` | ~40 DI registrations — all correct | Consider sectioned comments for readability |

---

## 7. Test Architecture Quality

**Current:** 206 test files (up from 163 at last analysis — +43 new test files).

**Strengths:**
- Clean separation: unit tests vs integration tests in dedicated folders
- Commands tested independently (17 files) — enables undo/redo confidence
- Persistence tests present — save/load roundtrips protected
- New regression tests added for prefab S-Matrix, grouping/ungrouping, and light sources

A shared `MainViewModelTestHelper` factory in `UnitTests/Helpers/` simplifies test construction
across integration tests.

---

## 8. Summary Table

| Concern | Severity | Phase | Status |
|---------|----------|-------|--------|
| `MainWindow.axaml` too large | MEDIUM | 2 | Partial — grew back to 927 lines; more extraction needed |
| Sub-ViewModel DI + gesture recognizer extraction | — | 1 | ✅ DONE (Issue #377) |
| DesignCanvasViewModel too large | — | 1 | ✅ DONE (commit efaa4f6) — 299 lines |
| Canvas/Services subfolder | — | 1 | ✅ DONE (commit efaa4f6) — 5 services extracted |
| `DesignCanvas.ComponentRendering.cs` | MEDIUM | 2 | 487 lines — candidate for extraction |
| `Core/Analysis/` folder size | LOW | 2 | 12 files — create subfolders |
| Cross-feature callback wiring | LOW | 2 | Not started — WeakReferenceMessenger recommended |
| PRISM migration | N/A | 3 (if needed) | Not needed at current scale |

**Overall Recommendation:** The architecture is in excellent shape. Phase 1 is fully complete
(including the DesignCanvasViewModel split that was Phase 2 in the previous analysis).
Next highest-ROI investment is continuing `MainWindow.axaml` panel extraction (Phase 2.1)
and addressing `DesignCanvas.ComponentRendering.cs` size (Phase 2.2).

---

*This analysis was produced as part of Issue #320 and updated in Issue #426. The architecture
report panel (see right sidebar → Architecture Report) provides live access to the key metrics
summarized here.*
