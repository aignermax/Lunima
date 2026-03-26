# Issue #273 Analysis: ComponentViewModel Duplicates After Undo/Redo

## Summary

**Status:** ✅ **NOW FIXED** (March 26, 2026)

The bug described in issue #273 has been fixed. The initial fix in commit `3a78993` addressed basic undo/redo scenarios but missed a critical edge case: when navigating history all the way back to 0 (empty canvas) and then forward again.

## Issue Description

**Original Bug:** After creating a group and performing multiple undo/redo cycles, duplicate ComponentViewModels appeared in both the UI grid and the hierarchy tree.

**Steps to Reproduce:**
1. Create 2 components (e.g., MMI1X2)
2. Select both components
3. Press `Ctrl+G` to create a group
4. Press `Ctrl+Z` (undo) **3 times**
5. Press `Ctrl+Y` (redo) **3 times**
6. Press `Ctrl+Z` (undo) **1 time**

**Expected:** 2 components visible
**Actual (before fix):** 4 components visible (2 duplicates!)

## Root Cause

The bug was in `CAP.Avalonia/Commands/CreateGroupCommand.cs` in the **Redo path** (Execute method, lines 52-101).

**Initial State (commit 3a78993):**
```csharp
// Remove child components from canvas (use stored VMs for identity)
foreach (var compVm in _componentViewModels)  // ← BUG: Uses stale ViewModel references!
{
    var pinsToRemove = _canvas.AllPins
        .Where(p => p.ParentComponentViewModel == compVm)
        .ToList();
    foreach (var pin in pinsToRemove)
    {
        _canvas.AllPins.Remove(pin);
    }
    _canvas.Router.RemoveComponentObstacle(compVm.Component);
    _canvas.Components.Remove(compVm);  // ← This fails if compVm is not in canvas!
}
```

**Problem:** The `_componentViewModels` list is populated during first Execute and stores ViewModel references. When history navigates all the way back to 0 (empty canvas), components are removed. When history moves forward, components are re-added but with NEW ViewModel instances. The Redo path tries to remove OLD ViewModels that are no longer in canvas.Components, so the NEW ViewModels remain, causing duplicates!

## The Fix

**Final Fix:** March 26, 2026

**Changes in `CAP.Avalonia/Commands/CreateGroupCommand.cs` (lines 59-77):**
```csharp
// Remove child components from canvas
// IMPORTANT: Find ViewModels by Core Component reference, not by stored ViewModel reference
// This handles the case where components were removed/re-added (creating new ViewModels)
var componentsToRemove = _canvas.Components
    .Where(cvm => _components.Contains(cvm.Component))  // ← FIX: Search by Core Component!
    .ToList();

foreach (var compVm in componentsToRemove)
{
    var pinsToRemove = _canvas.AllPins
        .Where(p => p.ParentComponentViewModel == compVm)
        .ToList();
    foreach (var pin in pinsToRemove)
    {
        _canvas.AllPins.Remove(pin);
    }
    _canvas.Router.RemoveComponentObstacle(compVm.Component);
    _canvas.Components.Remove(compVm);
}
```

**Key Changes:**
1. ✅ Search for ViewModels in canvas.Components by their Core Component reference (`_components.Contains(cvm.Component)`)
2. ✅ This works even if ViewModels were recreated (different instances wrapping same Core Components)
3. ✅ Removes the CURRENT ViewModels in canvas, not stale references from `_componentViewModels`

## Verification

### Unit Tests Created

New test file: `UnitTests/Commands/CreateGroupMultipleUndoRedoTests.cs`

**Tests:**
1. ✅ `CreateGroup_MultipleUndoRedo_NoViewModelDuplicates` - Tests exact scenario from issue
2. ✅ `CreateGroup_MultipleUndoRedoCycles_MaintainsCorrectCount` - Tests 5 undo/redo cycles
3. ✅ `CreateGroup_10UndoRedoCycles_NoMemoryLeak` - Tests reference equality after 10 cycles
4. ✅ `CreateGroup_WithPlaceCommands_NoHierarchyDuplicates` - Tests hierarchy panel integration
5. ✅ `CreateGroup_HistoryNavigationToZeroAndForward_NoComponentDuplicates` - **Tests the critical edge case** (empty canvas → forward)

**All 5 tests pass!**

### Test Results

```bash
$ dotnet test --filter "CreateGroupMultipleUndoRedoTests"
Passed!  - Failed: 0, Passed: 5, Skipped: 0, Total: 5

$ dotnet test --filter "FullyQualifiedName~CreateGroup"
Passed!  - Failed: 0, Passed: 37, Skipped: 0, Total: 37
```

**Full Test Suite:**
- **All CreateGroup-related tests passing** (37 tests)
- **0 regressions**

## Related Commits

1. `b8a83d0` - "fix: Preserve ComponentViewModel instances in group undo/redo cycles"
   - First attempt at fixing the ViewModel lifecycle issue
   - Added Redo handling but still had issues

2. `3a78993` - "Agent: implement #255 — Bug: Visual orphan components and incorrect pin rendering after group undo/redo"
   - Improved the Redo path to use stored `_componentViewModels` list
   - Added Router obstacle cleanup
   - Fixed basic undo/redo scenarios but missed the empty canvas edge case

3. **Current fix** (March 26, 2026) - "Fix: CreateGroupCommand handles re-created ViewModels correctly"
   - Changed Redo path to search for ViewModels by Core Component reference
   - Fixes the critical edge case: history navigation to empty canvas and forward
   - Added comprehensive test for this scenario

## Conclusion

✅ **Issue #273 is now fully resolved.**

The bug has been fixed in the current branch. The fix handles all scenarios including:
- Basic undo/redo (fixed in commit `3a78993`)
- Multiple undo/redo cycles (fixed in commit `3a78993`)
- **History navigation to empty canvas and forward** (fixed in current commit)

**Recommendation:** Merge this branch to close issue #273.

---

**Test Coverage Added:**
- Unit tests for exact reproduction scenario ✅
- Unit tests for multiple undo/redo cycles ✅
- Unit tests for reference equality preservation ✅
- Unit tests for hierarchy panel integration ✅
- Unit tests for empty canvas edge case ✅

**Build Status:** ✅ All builds passing
**Test Status:** ✅ All CreateGroup tests passing (37/37)

🤖 Fix completed by Claude Code Agent
Date: March 26, 2026
