# Routing System Cleanup Plan

## Current State (4,760 lines across 19 files)

### Problems
1. **Dead code**: ManhattanRouter.cs (367 lines), SectorGraph.cs (356 lines) - HPA* remnants
2. **Large files**: 6 files exceed 300-line guideline
3. **Duplicate logic**: StraightLineSolver duplicates what TwoBendSolver should do
4. **Complex PathSmoother**: 413 lines doing multiple responsibilities

---

## Phase 1: Delete Dead Code (−723 lines)

### Files to delete:
- ✅ `HierarchicalPathfinder.cs` (deleted)
- ⚠️ `ManhattanRouter.cs` (367 lines) - no longer used for photonic routing
- ⚠️ `SectorGraph.cs` (356 lines) - HPA* infrastructure, unused
- ⚠️ `DistanceTransform.cs` (154 lines) - only used by HPA*

**Impact**: Tests should still pass (these are unused)

---

## Phase 2: Consolidate Geometric Solvers (−95 lines)

### Merge StraightLineSolver into TwoBendSolver

**Current design flaw**: Straight line is a special case of two-bend (both bends have 0° angle)

**Better design**:
```
TwoBendSolver.TryTwoBendConnection(startPin, endPin)
├─ Step 1: Check if straight line (angles aligned, no turns needed)
│  └─ Return single StraightSegment if yes
├─ Step 2: Try all four bend combinations (LL, LR, RL, RR)
│  └─ Use circle-circle intersection
└─ Step 3: Return null if both fail
```

**Changes**:
- Add straight-line check to TwoBendSolver before trying circle math
- Delete StraightLineSolver.cs
- Remove straight-line call from WaveguideRouter.Route()

**Result**: TwoBendSolver (353 → 380 lines), but still under 400

---

## Phase 3: Split PathSmoother (413 → 4 files < 150 lines each)

**Current responsibilities** (too many!):
1. Grid path to waypoints conversion
2. Bend insertion (cardinal direction snapping)
3. Terminal approach geometry (start/end pin connections)
4. Path validation

**New structure**:
```
PathSmoother.cs (100 lines)
├─ ConvertToSegments() - main orchestrator
└─ Uses helper classes:

WaypointExtractor.cs (80 lines)
├─ ExtractWaypoints() - grid path → clean waypoints
└─ RemoveRedundantPoints()

TerminalConnector.cs (120 lines)
├─ ConnectToStartPin() - geometric entry
├─ ConnectToEndPin() - geometric exit
└─ ValidateAngles()

BendInserter.cs (90 lines)
├─ InsertBendsForSegments() - between waypoints
└─ SnapToCardinalDirection()
```

**Benefits**:
- Each file has single responsibility
- Easier to test individual components
- Easier to debug terminal approach issues

---

## Phase 4: Simplify WaveguideRouter (336 → 200 lines)

**Current problems**:
- Route() method does too much (80+ lines)
- TryRouteAStar() is 100+ lines with complex grid clearing

**Extract**:
```
WaveguideRouter.cs (200 lines)
├─ Route() - simple orchestrator (20 lines)
├─ TryRouteAStar() - delegate to AStarRoutingStrategy

AStarRoutingStrategy.cs (150 lines)
├─ Route() - main A* logic
├─ ClearGridCells() - pin approach area clearing
└─ ValidateAndSmoothPath() - post-processing
```

**Benefits**:
- WaveguideRouter becomes thin orchestrator
- A* complexity isolated in dedicated class
- Grid manipulation logic in one place

---

## Phase 5: Simplify TwoBendSolver (353 → 280 lines)

**Current problems**:
- TryBuildTwoBendPath() is 180 lines
- Multiple radius attempts mixed with geometric validation
- Hard to follow the arc construction logic

**Extract**:
```
TwoBendSolver.cs (150 lines)
├─ TryTwoBendConnection() - orchestrator
├─ TryStraightLine() - special case
└─ Delegates to:

BiarcSolver.cs (130 lines)
├─ SolveBiarc() - circle-circle intersection math
├─ ComputeArcCenters()
├─ FindTangentPoint()
└─ BuildArcSegments()

GeometricValidator.cs (60 lines)
├─ ValidateStartPoint()
├─ ValidateContinuity()
├─ ValidateEndPoint()
└─ CheckObstacles()
```

**Benefits**:
- Pure geometric math isolated in BiarcSolver
- Validation logic separated
- Each file < 150 lines

---

## Phase 6: Extract Grid Management

**Problem**: PathfindingGrid logic scattered across multiple files

**Solution**:
```
GridCellManager.cs (100 lines)
├─ ClearPinApproachArea() - used by multiple routers
├─ ClearCorridorArea()
├─ RestoreCells()
└─ IsLineBlocked()
```

Currently duplicated in:
- WaveguideRouter.TryRouteAStar()
- StraightLineSolver.IsLineBlocked()
- TwoBendSolver (obstacle checking)

---

## Final Structure (Clean & Testable)

```
Connect-A-Pic-Core/Routing/
│
├── WaveguideRouter.cs (200 lines)
│   └── Main orchestrator: geometric solvers → A* → invalid path
│
├── RoutedPath.cs (60 lines)
│   └── Result container with validation
│
├── PathSegment.cs (103 lines) ✅ already good
│
├── RoutingOrchestrator.cs (317 lines) ⚠️ needs review separately
│
├── GeometricSolvers/
│   ├── TwoBendSolver.cs (150 lines)
│   ├── BiarcSolver.cs (130 lines)
│   └── GeometricValidator.cs (60 lines)
│
├── AStarPathfinder/
│   ├── AStarPathfinder.cs (224 lines) ✅ OK
│   ├── AStarRoutingStrategy.cs (150 lines) - NEW
│   ├── PathfindingGrid.cs (196 lines) ✅ OK
│   ├── GridCellManager.cs (100 lines) - NEW
│   ├── RoutingCostCalculator.cs (239 lines) ✅ OK
│   │
│   ├── PathSmoothing/
│   │   ├── PathSmoother.cs (100 lines)
│   │   ├── WaypointExtractor.cs (80 lines)
│   │   ├── TerminalConnector.cs (120 lines)
│   │   └── BendInserter.cs (90 lines)
│   │
│   └── SegmentBuilders/
│       ├── BendBuilder.cs (159 lines) ✅ OK
│       ├── SBendBuilder.cs (219 lines) ✅ OK
│       └── PathBuilder.cs (257 lines) ✅ OK
│
└── Utilities/
    ├── AngleUtilities.cs (135 lines) ✅ OK
    ├── GridDirection.cs (111 lines) ✅ OK
    └── PathGeometryAnalyzer.cs (177 lines) ✅ OK
```

---

## Benefits of This Structure

### 1. **Single Responsibility**
- Each file does ONE thing
- Easy to understand at a glance
- No file > 260 lines

### 2. **Testability**
- Small, focused classes are easy to unit test
- Mock dependencies cleanly
- Test geometric math separate from routing logic

### 3. **Maintainability**
- Bug in terminal approach? → `TerminalConnector.cs`
- Bug in biarc math? → `BiarcSolver.cs`
- Bug in A* grid clearing? → `GridCellManager.cs`

### 4. **Extensibility**
- Add new geometric solver → drop into `GeometricSolvers/`
- Add new path smoothing strategy → implement in `PathSmoothing/`
- No changes needed to WaveguideRouter

---

## Implementation Order

1. ✅ Delete HierarchicalPathfinder.cs
2. **Delete dead code** (ManhattanRouter, SectorGraph, DistanceTransform)
3. **Merge StraightLineSolver into TwoBendSolver** (quick win)
4. **Extract BiarcSolver** from TwoBendSolver (geometric math isolation)
5. **Split PathSmoother** into 4 focused classes
6. **Extract AStarRoutingStrategy** from WaveguideRouter
7. **Create GridCellManager** to deduplicate grid clearing logic
8. **Run all tests** after each step - ensure nothing breaks

---

## Testing Strategy

After each refactoring step:
```bash
dotnet test UnitTests/UnitTests.csproj --filter "FullyQualifiedName~Routing"
```

Expected: **122/125 tests pass** (same as before)

---

## Notes

- **No behavior changes** - pure refactoring
- **No new features** - just cleaner structure
- **Keep git history clean** - one commit per logical step
- **All files stay under 260 lines** (well below 300 guideline)
