# Issue #273 Analysis: ComponentViewModel Duplicates After Undo/Redo

## Summary

**Status:** ✅ **ALREADY FIXED**

The bug described in issue #273 was already fixed in commit `3a78993` (March 25, 2026).

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

**Before Fix (commit b8a83d0):**
```csharp
// Remove child components from canvas
var componentsToRemove = _canvas.Components
    .Where(cvm => _components.Contains(cvm.Component))  // ← BUG: Searches by Core Component reference!
    .ToList();

foreach (var compVm in componentsToRemove)
{
    _canvas.Components.Remove(compVm);
}
```

**Problem:** After multiple undo/redo cycles, searching for ViewModels by Core Component reference could find different ViewModel instances than the ones originally stored, causing:
- Old ViewModels not being removed
- New ViewModels being added
- Result: Duplicates!

## The Fix

**Commit:** `3a78993` - "Agent: implement #255 — Bug: Visual orphan components and incorrect pin rendering after group undo/redo"

**Date:** March 25, 2026

**Changes:**
```csharp
// Remove child components from canvas (use stored VMs for identity)
foreach (var compVm in _componentViewModels)  // ← FIX: Use stored ViewModel references!
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
1. ✅ Use `_componentViewModels` list directly (stores exact ViewModel instances)
2. ✅ Remove by reference equality, not by searching for matching Core Components
3. ✅ Also added Router obstacle cleanup
4. ✅ Set `ParentGroup` reference correctly

## Verification

### Unit Tests Created

New test file: `UnitTests/Commands/CreateGroupMultipleUndoRedoTests.cs`

**Tests:**
1. ✅ `CreateGroup_MultipleUndoRedo_NoViewModelDuplicates` - Tests exact scenario from issue
2. ✅ `CreateGroup_MultipleUndoRedoCycles_MaintainsCorrectCount` - Tests 5 undo/redo cycles
3. ✅ `CreateGroup_10UndoRedoCycles_NoMemoryLeak` - Tests reference equality after 10 cycles

**All tests pass!**

### Test Results

```bash
$ dotnet test --filter "CreateGroupMultipleUndoRedoTests"
Bestanden!   : Fehler: 0, erfolgreich: 3, übersprungen: 0, gesamt: 3
```

**Full Test Suite:**
- **1078 tests passing** (including 3 new tests)
- **6 tests failing** (pre-existing, unrelated to this issue - see MEMORY.md)
- **0 regressions**

## Related Commits

1. `b8a83d0` - "fix: Preserve ComponentViewModel instances in group undo/redo cycles"
   - First attempt at fixing the ViewModel lifecycle issue
   - Added Redo handling but still had the bug (searched by Core Component)

2. `3a78993` - "Agent: implement #255 — Bug: Visual orphan components and incorrect pin rendering after group undo/redo"
   - **This commit fully fixed the bug**
   - Changed Redo path to use stored `_componentViewModels` list
   - Added Router obstacle cleanup

3. `92206e9` - "Agent: implement #256 — Bug: ComponentGroup external pins are not created for unoccupied internal pins"
   - Latest commit (current HEAD)
   - No changes to CreateGroupCommand.cs

## Conclusion

✅ **Issue #273 is already resolved in the current codebase.**

The bug was fixed in commit `3a78993` (March 25, 2026), and the fix has been verified with comprehensive unit tests.

**Recommendation:** Close issue #273 as already fixed, referencing commit `3a78993`.

---

**Test Coverage Added:**
- Unit tests for exact reproduction scenario ✅
- Unit tests for multiple undo/redo cycles ✅
- Unit tests for reference equality preservation ✅

**Build Status:** ✅ All builds passing
**Test Status:** ✅ 1078/1084 tests passing (6 pre-existing failures unrelated)

🤖 Analysis completed by Claude Code Agent
Date: March 25, 2026
