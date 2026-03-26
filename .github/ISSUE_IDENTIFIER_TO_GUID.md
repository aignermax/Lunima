# Refactor: Replace string Identifier with Guid for unique component identification

## Problem

Currently, `Component.Identifier` is a `string` used for both:
1. **Unique identification** (for references, connections, groups)
2. **Human-readable display names** (in UI)

This causes **critical bugs**:
- **Bug**: Copied ComponentGroups have duplicate child component IDs after Save/Load
- Groups lose visual body, cannot be selected
- Child components share same Identifier between original and copied group

## Root Cause

When a Group is copied:
- Group's `Identifier` is renamed (e.g., "Group_1x2z2" → "Group_1x2z3")
- **BUT**: Child component `Identifiers` remain unchanged
- On Save/Load, child IDs collide → groups merge or disappear

## Proposed Solution

**Refactor**:
```csharp
// BEFORE
public class Component {
    public string Identifier { get; set; }  // Used for BOTH ID and display
}

// AFTER
public class Component {
    public Guid Id { get; set; } = Guid.NewGuid();  // TRUE unique ID
    public string Name { get; set; }  // Display name (user-editable)
}
```

**Migration**:
- Save `Identifier` → split into `Id` (new Guid) + `Name` (old Identifier value)
- Add version field to `.cappro` format for backwards compatibility
- Update all references (Connections, Groups, ExternalPins) to use `Id` instead of `Identifier`

## Impact

**Breaking Change**:
- All `.cappro` files need migration
- Serialization format changes
- Extensive refactoring across codebase (~500+ references)

## Workaround (Short-term)

Fix `RenameGroupChildren()` in `ComponentClipboard.cs` to also rename child component Identifiers when pasting groups.

## Test Case

See `FileOperationsViewModelTests.CopiedGroup_AfterSaveLoad_ShouldHaveUniqueChildIdsAndBeSelectable()` - test currently FAILS demonstrating the bug.

## Acceptance Criteria

- [ ] `Component.Id` is Guid (unique, immutable)
- [ ] `Component.Name` is string (human-readable, user-editable)
- [ ] All references use `Id` instead of `Identifier`
- [ ] Migration code for old `.cappro` files
- [ ] All 1104 tests pass
- [ ] Copied groups work correctly after Save/Load

## Current Failing Tests (9 total)

**Pre-existing (not related to this issue)**:
1. GroupEditModeIntegrationTests.CompleteEditModeWorkflow_EnterAndExitGroup
2. ComponentGroupSMatrixBuilderTests.BuildGroupSMatrix_NestedGroup_ComputesRecursively
3. ComponentGroupSMatrixBuilderTests.BuildGroupSMatrix_NearestWavelengthFallback_WorksCorrectly
4. ComponentGroupSMatrixBuilderTests.BuildGroupSMatrix_SupportsMultipleWavelengths
5. ManhattanRoutingIntegrationTests.ManhattanPath_ClearsAfterDeletion_AllowsNewRoutes
6. ComponentGroupSimulationTests.ComponentGroup_WithComputedSMatrix_SimulatesSuccessfully
7. ComponentGroupSimulationTests.ComponentGroup_NestedGroups_SimulateCorrectly
8. ManhattanRoutingIntegrationTests.ManhattanFallbackPath_RegisteredAsObstacle_BlocksSubsequentAStar
9. ManhattanRoutingIntegrationTests.SequentialRouting_AllPathsRegisteredAsObstacles

**Related to this issue** (GroupPin connection persistence also affected by ID issues):
- Connection to Group ExternalPins disappear after Save/Load
