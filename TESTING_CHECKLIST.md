# Testing Checklist for Agent-Generated Features

## Features to Test (6 PRs)

### ✅ Already Merged & Working:
- [x] **Copy/Paste Workflow** (PR #73, Issue #70) - Tested gestern
- [x] **PDK Management Panel** (PR #72, Issue #71) - Tested gestern
- [x] **Release Automation** (PR #80, Issue #59) - CI/CD only

---

## 🧪 To Test Today:

### 1. Pin Alignment Helper Lines (PR #79, Issue #61)

**What:** Visual guide lines when pins align on same X/Y axis

**How to test:**
- [ ] Open Connect-A-PIC-Pro
- [ ] Place 2-3 components on canvas
- [ ] Drag one component around
- [ ] **Expected:** See horizontal/vertical guide lines when pins align
- [ ] **Check:** Lines appear/disappear dynamically
- [ ] **Check:** Lines show exact X or Y alignment

**Files to check:**
- `CAP.Avalonia/Controls/DesignCanvas.cs` - Rendering logic
- `CAP.Avalonia/ViewModels/DesignCanvasViewModel.cs` - Alignment detection

---

### 2. Locked/Fixed Elements (PR #78, Issue #62)

**What:** Lock components and connections so they can't be moved/deleted

**How to test:**
- [ ] Place a component on canvas
- [ ] Right-click component → "Lock Component" (or check UI for lock button)
- [ ] Try to drag the locked component
- [ ] **Expected:** Component cannot be moved
- [ ] Try to delete locked component
- [ ] **Expected:** Component cannot be deleted
- [ ] Unlock component
- [ ] **Expected:** Can now move/delete again

**Test with connections:**
- [ ] Create a connection between two components
- [ ] Lock the connection (if UI exists)
- [ ] Try to delete connection
- [ ] **Expected:** Connection cannot be deleted

**Files to check:**
- `Connect-A-Pic-Core/Components/Component.cs` - `IsLocked` property?
- `CAP.Avalonia/Commands/*.cs` - Lock check in Execute()
- `CAP.Avalonia/Views/MainWindow.axaml` - Lock button in UI?

---

### 3. Grating Coupler TE 1550 Position Fix (PR #77, Issue #66)

**What:** Fix position offset in GDS export for Grating Coupler TE 1550

**How to test:**
- [ ] Place Grating Coupler TE 1550 from SiEPIC PDK
- [ ] Create connections to it
- [ ] **In Avalonia:** Note exact pin positions (should snap correctly)
- [ ] Export to Nazca Python (🐍 Export button)
- [ ] Run exported Python script to generate GDS
- [ ] Open GDS in KLayout
- [ ] **Expected:** Grating Coupler position matches Avalonia view (no offset)
- [ ] **Check:** Connections align perfectly with pins

**Files to check:**
- `CAP.Avalonia/Services/SimpleNazcaExporter.cs` - Component placement logic

---

### 4. Straight Waveguide 100µm Size Fix (PR #76, Issue #67)

**What:** Fix incorrect size in GDS export for Straight Waveguide 100µm

**How to test:**
- [ ] Place "Straight Waveguide 100µm" component
- [ ] **In Avalonia:** Measure bounding box (should be 100µm length)
- [ ] Export to Nazca Python
- [ ] Generate GDS
- [ ] Open in KLayout
- [ ] Measure waveguide length
- [ ] **Expected:** Length is exactly 100µm (not scaled incorrectly)

**Files to check:**
- `CAP.Avalonia/Services/SimpleNazcaExporter.cs`
- Component stub definition in PDK JSON

---

### 5. Demo PDK Grating Coupler Rotation Fix (PR #75, Issue #68)

**What:** Fix 180° rotation issue in GDS export for Demo PDK Grating Coupler

**How to test:**
- [ ] Place "Grating Coupler" from Demo PDK (demofab)
- [ ] Set rotation to 0° in Avalonia
- [ ] Note the orientation (which way arrow/taper points)
- [ ] Export to Nazca Python
- [ ] Generate GDS
- [ ] Open in KLayout
- [ ] **Expected:** Orientation matches Avalonia (NOT rotated 180°)
- [ ] Try with 90°, 180°, 270° rotations
- [ ] **Expected:** All rotations match between Avalonia and GDS

**Files to check:**
- `CAP.Avalonia/Services/SimpleNazcaExporter.cs` - Rotation angle calculation

---

### 6. MMI 2x2 Length Fix (PR #74, Issue #69)

**What:** Fix incorrect length in GDS export for MMI 2x2 (SiEPIC)

**How to test:**
- [ ] Place MMI 2x2 from SiEPIC PDK
- [ ] **In Avalonia:** Note the bounding box dimensions
- [ ] Check that pins are at correct positions (left/right edges)
- [ ] Export to Nazca Python
- [ ] Generate GDS
- [ ] Open in KLayout
- [ ] Measure MMI length
- [ ] **Expected:** Length matches Avalonia (not shorter/compressed)
- [ ] **Expected:** Pins align correctly with component edges

**Files to check:**
- `CAP.Avalonia/Services/SimpleNazcaExporter.cs`
- `Connect-A-Pic-Core/Analysis/ComponentDimensionValidator.cs` (if added)

---

## 📊 Test Results Template

```
Feature: [Name]
PR: #XX
Status: ✅ PASS / ❌ FAIL / ⚠️ PARTIAL

Issues found:
- [List any bugs]

Screenshots:
- [Attach if needed]

Notes:
- [Additional observations]
```

---

## Build & Run Instructions

```bash
cd c:/dev/Akhetonics/Connect-A-PIC-Pro

# Pull latest merged changes
git pull origin main

# Build
dotnet build

# Run tests
dotnet test ./UnitTests/UnitTests.csproj

# Run app
dotnet run --project CAP.Desktop/CAP.Desktop.csproj
```

---

## Export Testing Workflow

For all export bugs (#66, #67, #68, #69):

1. **In Connect-A-PIC-Pro:**
   - Place component(s)
   - Create design
   - Export to Nazca Python (🐍 button)

2. **In Terminal:**
   ```bash
   cd [export location]
   python exported_design.py
   # This generates a .gds file
   ```

3. **In KLayout:**
   - Open generated .gds file
   - Use ruler tool to measure dimensions
   - Compare with Avalonia view

4. **Screenshot both:**
   - Avalonia view
   - KLayout view
   - Side-by-side comparison

---

## Priority Order for Testing

1. **Pin Alignment** (#61) - Easy to test, visible immediately
2. **Locked Elements** (#62) - Easy to test, interactive
3. **Export Bugs** (#66-69) - More complex, requires Nazca + KLayout

---

## Notes

- All PRs need to pass CI before testing locally
- Check GitHub Actions status for each PR
- If tests fail, don't merge - investigate first
- Take screenshots of any visual bugs

---

Created: 2026-03-11
Last Updated: 2026-03-11 07:30
