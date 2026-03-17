# ComponentGroup Architecture — Template-Only System

**Status:** Active (since #214 implementation)
**Pattern:** Unity Prefab / Unreal Blueprint
**Last Updated:** 2026-03-17

---

## Executive Summary

ComponentGroups in Connect-A-PIC Pro are **reusable templates stored in the library**, not live containers on the canvas. This document explains the design rationale, implementation details, and migration path from the previous nested-group architecture.

**Key Principle:** The design canvas is always flat. Templates are instantiated as ungrouped, top-level components.

---

## Table of Contents

1. [Design Rationale](#design-rationale)
2. [Architecture Overview](#architecture-overview)
3. [Template Lifecycle](#template-lifecycle)
4. [Core Components](#core-components)
5. [JSON Storage Format](#json-storage-format)
6. [Comparison to Previous Architecture](#comparison-to-previous-architecture)
7. [Migration Guide](#migration-guide)
8. [FAQ](#faq)

---

## Design Rationale

### Why Template-Only?

The previous architecture allowed **live nested groups on canvas** with an edit mode to access child components. While conceptually powerful, this created fundamental issues:

#### Problems with Live Nested Groups

1. **Phantom Connections (#215)**: Deleting a component inside a group left dangling `WaveguideConnection` references pointing to destroyed components. The connection manager couldn't find these connections because they were stored in the parent group's internal list.

2. **Invisible Pins (#216)**: Group pins were only accessible in edit mode. Outside edit mode, users couldn't see or connect to group pins, making them effectively invisible on the canvas.

3. **Route Recalculation Bugs (#217)**: Moving a group didn't trigger waveguide recalculation for internal connections, causing visual/physical desync.

4. **Complex State Management**:
   - Edit mode state tracking (which group is being edited)
   - Nested navigation breadcrumbs
   - Coordinate system transformations (group-local vs canvas-global)
   - Recursive selection and hit testing

5. **Serialization Complexity**: Saving/loading nested groups required careful handling of parent references, relative coordinates, and internal connection IDs.

### Why the Unity Prefab Pattern?

The Unity Prefab pattern solves these issues by:

- **Eliminating nested state**: Canvas is always flat, no edit mode needed
- **Preventing phantom connections**: All connections are top-level, managed by a single `ConnectionManager`
- **Simplifying routing**: All waveguides are canvas-level, standard routing applies
- **Clear template ownership**: Templates live in library, instances are independent components
- **Easy copy/paste**: Placed components are just regular components with optional metadata

**Trade-off:** You can't modify a template and have all instances update automatically. This is acceptable for the current use case (manual photonic circuit design, not procedural generation).

---

## Architecture Overview

### Three-Layer System

```
┌─────────────────────────────────────────┐
│  LIBRARY (Template Storage)             │
│  ┌─────────────────────────────────┐   │
│  │ ComponentGroup (IsPrefab=true)  │   │
│  │ - ChildComponents (template)    │   │
│  │ - InternalPaths (frozen)        │   │
│  │ - ExternalPins (optional)       │   │
│  └─────────────────────────────────┘   │
└─────────────────────────────────────────┘
               ↓ (PlaceTemplateCommand)
┌─────────────────────────────────────────┐
│  CANVAS (Flat Component List)           │
│  ┌─────┐  ┌─────┐  ┌─────┐  ┌─────┐   │
│  │Comp1│  │Comp2│  │Comp3│  │Comp4│   │ ← All top-level
│  └─────┘  └─────┘  └─────┘  └─────┘   │
│       ↘      ↓       ↓      ↙          │
│        WaveguideConnections            │
└─────────────────────────────────────────┘
               ↓ (Metadata only)
┌─────────────────────────────────────────┐
│  METADATA (Optional Tracking)            │
│  - Component.SourceTemplate              │
│  - Component.TemplateInstanceId          │
└─────────────────────────────────────────┘
```

### Key Classes

| Class | Location | Purpose |
|-------|----------|---------|
| `ComponentGroup` | `Connect-A-Pic-Core/Components/Core/` | Template container (library only) |
| `GroupLibraryManager` | `Connect-A-Pic-Core/Components/Creation/` | Template persistence & instantiation |
| `PlaceTemplateCommand` | `CAP.Avalonia/Commands/` | Places templates as ungrouped components |
| `FrozenWaveguidePath` | `Connect-A-Pic-Core/Components/Core/` | Stores connection geometry in templates |
| `GroupTemplate` | `Connect-A-Pic-Core/Components/Creation/` | Template metadata (name, thumbnail, etc.) |

---

## Template Lifecycle

### 1. Template Creation

**User Action:** Select multiple components → Right-click → "Save Selection as Template"

**Implementation:**
```csharp
// CAP.Avalonia/ViewModels/Canvas/DesignCanvasViewModel.cs
public void SaveSelectionAsTemplate(string name, string description)
{
    var selectedComponents = Selection.SelectedComponents.Select(vm => vm.Model).ToList();
    var connections = ConnectionManager.GetConnectionsBetween(selectedComponents);

    // Create ComponentGroup with frozen paths
    var group = new ComponentGroup(name)
    {
        Description = description,
        IsPrefab = true
    };

    foreach (var comp in selectedComponents)
    {
        group.AddChild(comp.Clone() as Component);
    }

    foreach (var conn in connections)
    {
        var frozenPath = new FrozenWaveguidePath
        {
            Path = conn.RoutedPath.Clone(),
            StartPin = conn.StartPin,
            EndPin = conn.EndPin
        };
        group.AddInternalPath(frozenPath);
    }

    // Save to library
    GroupLibraryManager.SaveTemplate(group, name, description);
}
```

**Result:** JSON file in `GroupLibrary/UserGroups/` folder.

### 2. Template Storage

**File Structure:**
```
%LocalAppData%/ConnectAPICPro/GroupLibrary/
├── UserGroups/
│   ├── ring_resonator_20260317_143052.json
│   └── mzi_switch_20260317_150234.json
└── PdkGroups/
    └── balanced_detector_20260315_120000.json
```

**JSON Format:** See [JSON Storage Format](#json-storage-format) section.

### 3. Template Instantiation

**User Action:** Drag template from library panel onto canvas

**Implementation:**
```csharp
// CAP.Avalonia/Commands/PlaceTemplateCommand.cs
public void Execute()
{
    // 1. Deep copy template (new GUIDs for all components)
    var templateCopy = _template.DeepCopy();

    // 2. Calculate placement offset
    double offsetX = _placeX - templateCopy.PhysicalX;
    double offsetY = _placeY - templateCopy.PhysicalY;

    // 3. Extract child components and place as top-level
    var instanceId = Guid.NewGuid();
    foreach (var child in templateCopy.ChildComponents)
    {
        child.PhysicalX += offsetX;
        child.PhysicalY += offsetY;
        child.ParentGroup = null; // ← Key step: ungroup
        child.SourceTemplate = _template.GroupName;
        child.TemplateInstanceId = instanceId;

        _canvas.AddComponent(child);
    }

    // 4. Create waveguide connections from frozen paths
    foreach (var frozenPath in templateCopy.InternalPaths)
    {
        var startPin = FindPinInPlacedComponents(frozenPath.StartPin);
        var endPin = FindPinInPlacedComponents(frozenPath.EndPin);
        _canvas.ConnectionManager.AddConnection(startPin, endPin);
    }

    // 5. Auto-route connections
    await _canvas.RecalculateRoutesAsync();
}
```

**Result:** Individual components on canvas with waveguide connections. No `ComponentGroup` instance exists on canvas.

---

## Core Components

### ComponentGroup Class

**Purpose:** Container for template data. Only used in library, never instantiated on canvas.

**Key Properties:**
- `ChildComponents: List<Component>` — Components in this template
- `InternalPaths: List<FrozenWaveguidePath>` — Stored connection geometry
- `ExternalPins: List<GroupPin>` — Optional external interface (future: hierarchical designs)
- `IsPrefab: bool` — Marks this as a library template (always `true` for saved templates)

**Key Methods:**
- `DeepCopy(): ComponentGroup` — Creates independent clone with new GUIDs
- `AddChild(Component)` — Adds component to template
- `AddInternalPath(FrozenWaveguidePath)` — Stores connection geometry

**Important Notes:**
- Despite containing methods like `MoveGroup()` and `RotateGroupBy90CounterClockwise()`, these are only used during template creation/modification in the library, NOT during runtime on canvas.
- The class still inherits from `Component` for serialization compatibility, but it's never added to the canvas component list.

### GroupLibraryManager Class

**Purpose:** Manages template persistence and instantiation.

**Key Methods:**

```csharp
public GroupTemplate SaveTemplate(
    ComponentGroup group,
    string name,
    string? description = null,
    string source = "User")
```
Saves a ComponentGroup as a JSON template file.

```csharp
public void LoadTemplates()
```
Loads all templates from `UserGroups/` and `PdkGroups/` folders.

```csharp
public ComponentGroup InstantiateTemplate(
    GroupTemplate template,
    double x,
    double y)
```
Creates a deep copy of a template at the specified position. (Note: This is rarely used directly; `PlaceTemplateCommand` does the same work.)

**Storage Locations:**
- **User templates**: `%LocalAppData%/ConnectAPICPro/GroupLibrary/UserGroups/`
- **PDK templates**: `%LocalAppData%/ConnectAPICPro/GroupLibrary/PdkGroups/`

### PlaceTemplateCommand Class

**Purpose:** Implements undoable template placement.

**Workflow:**
1. Deep copy template → `_template.DeepCopy()`
2. Offset children to target position
3. Clear `ParentGroup` references (ungroup)
4. Add metadata (`SourceTemplate`, `TemplateInstanceId`)
5. Add components to canvas
6. Create connections from frozen paths
7. Trigger route recalculation

**Undo:** Removes all placed components and their connections.

### FrozenWaveguidePath Class

**Purpose:** Stores waveguide geometry in templates. Converted to live `WaveguideConnection` on instantiation.

**Key Properties:**
- `Path: RoutedPath` — Segment geometry (straights, bends)
- `StartPin: PhysicalPin` — Start pin reference
- `EndPin: PhysicalPin` — End pin reference
- `PathId: Guid` — Unique identifier

**Important Notes:**
- **Not used for live canvas connections**: Only for template storage
- Frozen paths are **not auto-routed**: They store the exact geometry from template creation
- When instantiated, `PlaceTemplateCommand` creates new `WaveguideConnection`s and triggers auto-routing

---

## JSON Storage Format

Templates are stored as JSON files with metadata and serialized ComponentGroup data.

### Example Structure

```json
{
  "Name": "Ring Resonator",
  "Description": "Add-drop ring with directional couplers",
  "Source": "User",
  "CreatedAt": "2026-03-17T14:30:52Z",
  "ComponentCount": 4,
  "WidthMicrometers": 250.0,
  "HeightMicrometers": 150.0,
  "PreviewThumbnailBase64": null
}
```

**Note:** The actual ComponentGroup data (child components, frozen paths, etc.) is NOT stored in this file. Currently, the system only stores metadata. Full serialization would require extending `ComponentGroupSerializer` to include:

- Component details (type, position, parameters)
- Frozen path segments
- External pin definitions

**Future Enhancement:** Implement full ComponentGroup serialization using `CAP-DataAccess/Persistence/ComponentGroupSerializer.cs`.

---

## Comparison to Previous Architecture

### Old Architecture (Before #214)

```
Canvas with Live Groups:
┌─────────────────────────────────────┐
│ ComponentGroup (on canvas)          │
│ ┌─────────────────────────────────┐ │
│ │ Edit Mode View                  │ │
│ │ ┌─────┐  ┌─────┐  ┌─────┐      │ │
│ │ │Comp1│─→│Comp2│─→│Comp3│      │ │
│ │ └─────┘  └─────┘  └─────┘      │ │
│ │ (Internal WaveguideConnections) │ │
│ └─────────────────────────────────┘ │
│ External Pins: [o] [o] [o]          │
└─────────────────────────────────────┘
                ↕
     Outside edit mode: Black box
```

**Features:**
- Live nested groups on canvas
- Edit mode to access/modify internal components
- External pins for group-to-group connections
- Hierarchical navigation

**Problems:**
- Phantom connections when deleting grouped components (#215)
- Pins invisible outside edit mode (#216)
- Route recalculation bugs (#217)
- Complex coordinate transformations
- Nested state management

### New Architecture (After #214)

```
Library Templates:
┌─────────────────────────────────────┐
│ GroupLibraryManager                 │
│ ┌─────────────────┐                 │
│ │ Ring Resonator  │ (JSON file)    │
│ └─────────────────┘                 │
└─────────────────────────────────────┘
                ↓ Drag & Drop
Canvas (Always Flat):
┌─────────────────────────────────────┐
│ ┌─────┐  ┌─────┐  ┌─────┐  ┌─────┐ │
│ │Comp1│─→│Comp2│─→│Comp3│─→│Comp4│ │
│ └─────┘  └─────┘  └─────┘  └─────┘ │
│ All top-level, no nesting           │
└─────────────────────────────────────┘
```

**Features:**
- Templates in library, instances are ungrouped
- No edit mode needed
- All connections are top-level
- Optional metadata tracks template origin

**Benefits:**
- No phantom connections (all connections managed centrally)
- No invisible pins (all components/pins are top-level)
- No route recalculation bugs (standard routing applies)
- Simple coordinate system (no transformations)
- Easy serialization (flat component list)

**Trade-offs:**
- Can't update all instances when template changes
- No live hierarchical designs (planned for future with external pins)

---

## Migration Guide

### For Users with Old .cappro Files

**Old files with live nested groups will still load, but:**

1. **Compatibility Layer**: The deserializer should detect nested groups and flatten them on load.
2. **Warning Message**: Display notification: "This file contains nested groups (old format). Converting to template-only format. Save to update."
3. **Automatic Conversion**:
   - Extract all nested components to top-level
   - Convert internal FrozenWaveguideConnections to regular connections
   - Preserve component positions and identifiers

**Implementation Status:** ⚠️ Not yet implemented. Old files may fail to load correctly.

**Workaround:** Re-create designs in the new version, or manually flatten groups before saving.

### For Developers Adding Features

**If you need to work with ComponentGroups:**

1. ✅ **DO** use `GroupLibraryManager` to save/load templates
2. ✅ **DO** use `PlaceTemplateCommand` to instantiate templates
3. ✅ **DO** keep canvas components flat (no `ParentGroup` references)
4. ❌ **DON'T** add ComponentGroup instances to the canvas component list
5. ❌ **DON'T** implement edit mode or nested navigation
6. ❌ **DON'T** create live FrozenWaveguidePath connections (use WaveguideConnection)

**Example: Adding a "Duplicate Template" Feature**

```csharp
public void DuplicateTemplate(GroupTemplate template)
{
    // Load template
    var group = template.TemplateGroup;

    // Create copy
    var copy = group.DeepCopy();
    copy.GroupName = $"{template.Name} (Copy)";

    // Save as new template
    GroupLibraryManager.SaveTemplate(copy, copy.GroupName, template.Description);
}
```

---

## FAQ

### Q: Can I create hierarchical designs with sub-circuits?

**A:** Not in the current template-only system. However, the `ExternalPins` feature in `ComponentGroup` is designed for future hierarchical support. The idea:

1. Save a complex circuit as a template with external pins
2. Place multiple instances on canvas
3. Connect instance pins to other components
4. System computes S-matrix from internal structure

This would be **live hierarchical groups**, but with explicit external interfaces (no edit mode needed).

**Status:** Planned, not implemented. See issue #XXX (future).

### Q: What happens to FrozenWaveguidePath when a template is placed?

**A:** `PlaceTemplateCommand` converts frozen paths to regular `WaveguideConnection`s:

```csharp
foreach (var frozenPath in templateCopy.InternalPaths)
{
    var startPin = FindPinInPlacedComponents(frozenPath.StartPin);
    var endPin = FindPinInPlacedComponents(frozenPath.EndPin);
    _canvas.ConnectionManager.AddConnection(startPin, endPin);
}
```

The frozen paths are **discarded** after instantiation. Connections are auto-routed based on current routing settings.

### Q: Can I modify a template after placing instances?

**A:** No. Templates and instances are independent. If you modify a template in the library, existing instances on canvas are unchanged.

**Workaround:** Delete old instances and place new ones from the updated template.

### Q: Why does ComponentGroup still have MoveGroup() and RotateGroupBy90CounterClockwise()?

**A:** These methods are used during **template creation** to adjust component positions before saving. They're never called on live canvas groups (because there are no live canvas groups).

**Future Refactoring:** Consider extracting these methods into a `GroupTemplateEditor` class to clarify intent.

### Q: How do I debug template placement issues?

**A:** Set breakpoints in `PlaceTemplateCommand.Execute()` and check:

1. `_template.ChildComponents.Count` — Are children loaded?
2. `_template.InternalPaths.Count` — Are frozen paths present?
3. `_placedComponents` after loop — Were components added to canvas?
4. `ConnectionManager.Connections` after routing — Were connections created?

Enable **Routing Diagnostics Panel** (right panel) to see real-time path status.

### Q: Can templates span multiple PDKs?

**A:** Yes. Templates store component data (type, parameters, S-matrices), not just references. You can create a template with components from multiple PDKs, and it will work even if PDKs are toggled off (as long as component data is serialized).

---

## Summary

The template-only ComponentGroup architecture provides a **simple, robust system** for reusable component groups without the complexity and bugs of live nested groups. By keeping the canvas flat and using templates as blueprints (not live containers), we eliminate phantom connections, invisible pins, and coordinate transformation issues.

**Key Takeaways:**
- Templates in library, instances are ungrouped
- Canvas is always flat, no edit mode
- FrozenWaveguidePath for template storage only
- PlaceTemplateCommand handles instantiation
- Future: Hierarchical designs with external pins (not live nesting)

For implementation details, see source code documentation in:
- `Connect-A-Pic-Core/Components/Core/ComponentGroup.cs`
- `CAP.Avalonia/Commands/PlaceTemplateCommand.cs`
- `Connect-A-Pic-Core/Components/Creation/GroupLibraryManager.cs`
