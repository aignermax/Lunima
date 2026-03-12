# Pull Request: Add ComponentGroup Library Catalog with Previews

**Closes #117**

## Summary

This PR implements a complete vertical slice for saving and reusing groups of components in the library. Users can now select multiple components, save them as a named group, and place the entire group back onto the canvas with a single click.

### Features Implemented

✅ **Create Component Groups**
- Select multiple components on canvas
- Save as reusable group with name and category
- Captures positions, rotations, slider values, and internal connections

✅ **Library Catalog with Visual Previews**
- Collapsible "Component Groups" panel in left sidebar
- 60×45px thumbnail previews showing group dimensions
- Displays component count and dimensions (width × height µm)
- Persistent JSON catalog (~/.config/ConnectAPicPro/component-groups.json)

✅ **Place Saved Groups**
- Click to select group from library
- Single-click placement on canvas
- Instantiates entire group with all components and connections intact

✅ **Group Management**
- Delete unwanted groups
- Refresh catalog view
- Status display showing catalog info

---

## Vertical Slice Checklist

### ✅ Core Logic
- `ComponentGroup.cs` (123 lines) - Data model for grouped components with relative positions
- `ComponentGroupManager.cs` (180 lines) - Service for creating, saving, loading groups from JSON

### ✅ ViewModel
- `ComponentGroupViewModel.cs` (205 lines) - MVVM ViewModel using CommunityToolkit.Mvvm
  - `[ObservableProperty]` for bindable state
  - `[RelayCommand]` for user actions
- Integrated into `LeftPanelViewModel`

### ✅ View (AXAML)
- `MainWindow.axaml` lines 148-224 - Component Groups panel in left sidebar
  - Create group form (name, category inputs)
  - ListBox with visual preview thumbnails using `ComponentPreview` control
  - Action buttons (Place, Delete, Refresh)
  - Dimension display in micrometers below metadata

### ✅ DI Wiring
- Integrated into `MainViewModel` with callbacks:
  - `CreateGroupFromSelection()` - Captures selected components
  - `PlaceComponentGroup()` - Instantiates entire group on canvas

### ✅ Unit Tests
- `ComponentGroupManagerTests.cs` - 8 unit tests for core logic
  - SaveGroups, LoadGroups, AddGroup, RemoveGroup, CreateFromComponents, etc.

### ✅ Integration Tests
- `ComponentGroupIntegrationTests.cs` - 7 integration tests for ViewModel workflow
  - End-to-end testing of create → save → load → place workflow
  - Tests observable property updates and command execution

**All 15 ComponentGroup tests passing ✅**

---

## Build & Test Status

```bash
✅ dotnet build - Success (with pre-existing warnings)
✅ dotnet test - 633/640 tests passing
   - 15/15 ComponentGroup tests passing
   - 7 pre-existing failures unrelated to this PR
```

---

## How to Test

1. **Launch the application**
   ```bash
   dotnet run --project CAP.Desktop
   ```

2. **Create a Component Group**
   - Place 2-3 components on the canvas (e.g., Directional Coupler, Waveguides)
   - Connect them together
   - Select all components (drag selection box or Ctrl+A)
   - Expand "Component Groups" panel in left sidebar
   - Enter a name (e.g., "Test Circuit") and category (e.g., "Test")
   - Click "Create Group" button

3. **Verify Group Appears in Library**
   - Check that the new group appears in the "Saved Groups" list
   - Verify the thumbnail preview shows the approximate dimensions
   - Verify metadata shows component count and dimensions

4. **Place Saved Group**
   - Click to select the group from the list
   - Click "Place" button
   - Move mouse to position the group
   - Click to place on canvas
   - Verify all components and connections are restored

5. **Test Delete**
   - Select a group from the list
   - Click "Delete" button
   - Verify group is removed from catalog

---

## Files Changed

### New Files
- `Connect-A-Pic-Core/Components/ComponentHelpers/ComponentGroup.cs` (123 lines)
- `Connect-A-Pic-Core/Components/ComponentHelpers/ComponentGroupManager.cs` (180 lines)
- `CAP.Avalonia/ViewModels/Library/ComponentGroupViewModel.cs` (205 lines)
- `UnitTests/Components/ComponentGroupManagerTests.cs` (215 lines)
- `UnitTests/ViewModels/ComponentGroupIntegrationTests.cs` (219 lines)

### Modified Files
- `CAP.Avalonia/ViewModels/Panels/LeftPanelViewModel.cs` - Added ComponentGroups property
- `CAP.Avalonia/ViewModels/MainViewModel.cs` - Added CreateGroupFromSelection and PlaceComponentGroup methods
- `CAP.Avalonia/Views/MainWindow.axaml` - Added Component Groups panel with visual previews

---

## Code Quality

- ✅ All new files under 250 lines
- ✅ SOLID principles followed
- ✅ MVVM pattern with CommunityToolkit.Mvvm
- ✅ XML documentation for all public members
- ✅ Clear, descriptive names
- ✅ No magic numbers

---

## Architecture Notes

**Component Group Storage Format:**
```json
{
  "Id": "guid",
  "Name": "MZI Circuit",
  "Category": "Interferometers",
  "WidthMicrometers": 500.0,
  "HeightMicrometers": 300.0,
  "Components": [
    {
      "PdkName": "demo-pdk",
      "ComponentType": "DC",
      "RelativeX": 0.0,
      "RelativeY": 0.0,
      "Rotation": 0,
      "SliderValues": {"coupling": 0.5}
    }
  ],
  "Connections": [
    {
      "SourceComponentIndex": 0,
      "SourcePinName": "out1",
      "TargetComponentIndex": 1,
      "TargetPinName": "in1"
    }
  ]
}
```

**Visual Preview:**
- Uses existing `ComponentPreview` control
- Shows bounding box representation at 60×45 pixel scale
- Dimensions displayed in micrometers below preview

---

## Screenshots

(Add screenshots showing the Component Groups panel, thumbnail previews, and placement workflow)

---

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>
