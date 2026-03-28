# GDS Testing & Debugging Tools

This folder contains Python scripts for debugging and verifying GDS coordinate export issues (primarily **Issue #329** - waveguide/pin coordinate misalignment).

These tools are **used by C# unit tests** to validate that the `SimpleNazcaExporter` produces correct GDS files.

---

## 📋 Overview

| Script | Purpose | Used By Tests? |
|--------|---------|---------------|
| `extract_gds_coords.py` | Extract all polygon/path coordinates from any GDS file to JSON | ✅ Yes - `GdsCoordinateExtractionTests` |
| `generate_reference_nazca.py` | Generate ground-truth GDS with known coordinates using Nazca | ✅ Yes - `GdsGroundTruthTests` |
| `compare_gds_coords.py` | Compare two GDS files numerically and report coordinate deviations | ✅ Yes - `GdsCoordinateVerificationTests` |
| `reference_minimal.py` | Minimal reference design for manual testing | ⚠️ Manual use only |

---

## 🔧 Installation

### Prerequisites

```bash
# Python 3.8+ required
python3 --version

# Install dependencies
pip install -r requirements.txt
```

### Requirements:
- `gdspy>=1.6.13` - For reading/writing GDS files
- `numpy>=1.20.0` - For coordinate calculations
- `nazca` (optional) - Only needed for `generate_reference_nazca.py` and `reference_minimal.py`

---

## 📖 Script Details

### 1. `extract_gds_coords.py`

**Purpose:** Extract all polygon and path coordinates from a GDS file to JSON for numerical comparison.

**Usage:**
```bash
python Scripts/extract_gds_coords.py <input.gds> [output.json]
```

**Example:**
```bash
# Extract coordinates from a design
python Scripts/extract_gds_coords.py my_design.gds coords.json

# Output: coords.json with all polygon vertices and path points
```

**Output Format:**
```json
{
  "gds_file": "my_design.gds",
  "units": {
    "user_unit_m": 1e-06,
    "db_unit_m": 1e-09
  },
  "cells": [
    {
      "name": "top_cell",
      "polygons": [
        {
          "layer": 1,
          "points": [[0.0, 0.0], [100.0, 0.0], [100.0, 50.0], [0.0, 50.0]]
        }
      ],
      "paths": [
        {
          "layer": 2,
          "points": [[100.0, 25.0], [300.0, 25.0]]
        }
      ]
    }
  ]
}
```

**Used By:** `GdsCoordinateExtractorTests.cs`, `GdsCoordinateExtractionTests.cs`

---

### 2. `generate_reference_nazca.py`

**Purpose:** Generate a **ground-truth** GDS file with **exactly known** coordinates using Nazca directly.

This replicates what `SimpleNazcaExporter` *should* produce for a simple 2-component + waveguide design.

**Usage:**
```bash
python Scripts/generate_reference_nazca.py <output.gds> <output_coords.json>
```

**Example:**
```bash
# Generate reference GDS
python Scripts/generate_reference_nazca.py reference.gds reference_coords.json

# Compare with C# export
python Scripts/extract_gds_coords.py csharp_export.gds csharp_coords.json
python Scripts/compare_gds_coords.py reference_coords.json csharp_coords.json
```

**Reference Design:**
- Component 1: 100×50 µm at physical (0, 0)
- Component 2: 100×50 µm at physical (300, 0)
- Waveguide: Straight 200 µm connecting output→input pins

**Nazca Coordinate Conventions** (matches `SimpleNazcaExporter`):
```
Nazca Y = -(PhysicalY + HeightOffset)
Pin stub Y = Height - OffsetY  (Y-flip within stub)
```

**Used By:** `GdsGroundTruthTests.cs`, `GdsTestDesigns.cs` (C# constants must match!)

---

### 3. `compare_gds_coords.py`

**Purpose:** Compare two GDS coordinate JSON files and report **numerical deviations** in micrometers.

**Usage:**
```bash
python Scripts/compare_gds_coords.py <expected.json> <actual.json> [--tolerance 0.01]
```

**Example:**
```bash
# Compare reference vs. C# export
python Scripts/compare_gds_coords.py reference_coords.json csharp_coords.json

# Custom tolerance (default: 0.01 µm)
python Scripts/compare_gds_coords.py expected.json actual.json --tolerance 0.001
```

**Output:**
```
=== GDS Coordinate Comparison ===
Expected: reference_coords.json
Actual:   csharp_coords.json
Tolerance: 0.01 µm

Cell: top_cell
  Polygon #0 (layer 1):
    Point 0: ✓ Match (0.00, 0.00)
    Point 1: ✓ Match (100.00, 0.00)
    Point 2: ❌ MISMATCH (100.00, 50.00) vs (100.00, 40.50) → ΔY = 9.50 µm
    Point 3: ✓ Match (0.00, 50.00)

  Waveguide path (layer 2):
    Start: ❌ MISMATCH (100.00, 25.00) vs (100.00, 15.50) → ΔY = 9.50 µm
    End:   ✓ Match (300.00, 25.00)

RESULT: 2 mismatches found (threshold: 0.01 µm)
```

**Used By:** `GdsCoordinateVerificationTests.cs`

---

### 4. `reference_minimal.py`

**Purpose:** Minimal standalone Nazca script for **manual testing** and learning Nazca export.

**Usage:**
```bash
python Scripts/reference_minimal.py
# Generates: reference_minimal.gds
```

**Not used by tests** - This is for human inspection and debugging.

---

## 🧪 How These Tools Help Find GDS Bugs

### Issue #329: Waveguide/Pin Coordinate Misalignment

**Problem:** Exported GDS files had waveguides that didn't connect properly to component pins (9.5 µm Y-offset for grating couplers).

**Workflow:**

1. **Generate Reference:**
   ```bash
   python Scripts/generate_reference_nazca.py reference.gds reference_coords.json
   ```
   ✅ This uses Nazca directly → guaranteed correct

2. **Export from C#:**
   ```bash
   # Run C# test that exports GDS via SimpleNazcaExporter
   dotnet test --filter "GdsExport"
   ```
   Creates: `test_output.gds`

3. **Extract C# Coordinates:**
   ```bash
   python Scripts/extract_gds_coords.py test_output.gds csharp_coords.json
   ```

4. **Compare:**
   ```bash
   python Scripts/compare_gds_coords.py reference_coords.json csharp_coords.json
   ```
   Reports: `❌ MISMATCH ΔY = 9.50 µm` → Bug found!

5. **Fix Code:**
   - Fixed `PhysicalPin.GetAbsoluteNazcaPosition()` in PR #336
   - Added proper Y-flip calculation: `nazca_y = -(physicalY + offsetY)`

6. **Verify Fix:**
   ```bash
   python Scripts/compare_gds_coords.py reference_coords.json fixed_coords.json
   # All ✓ Match!
   ```

---

## 🔗 Integration with C# Tests

### Automated Testing

The C# test suite automatically invokes these Python scripts:

```csharp
// UnitTests/Integration/GdsCoordinateVerificationTests.cs
[Fact]
public async Task ExportedGds_MatchesReferenceCoordinates()
{
    // 1. Generate reference GDS using Python
    await RunPythonScript("Scripts/generate_reference_nazca.py", "reference.gds reference_coords.json");

    // 2. Export using C# SimpleNazcaExporter
    var csharpGds = exporter.Export(testDesign);
    File.WriteAllText("csharp_export.py", csharpGds);
    await RunNazca("csharp_export.py", "csharp.gds");

    // 3. Extract coordinates from both
    await RunPythonScript("Scripts/extract_gds_coords.py", "reference.gds reference_coords.json");
    await RunPythonScript("Scripts/extract_gds_coords.py", "csharp.gds csharp_coords.json");

    // 4. Compare
    var result = await RunPythonScript("Scripts/compare_gds_coords.py", "reference_coords.json csharp_coords.json");

    result.ExitCode.ShouldBe(0, "GDS coordinates should match reference");
}
```

**Test Projects:**
- `GdsCoordinateExtractorTests.cs` - Tests `GdsCoordinateExtractor` C# wrapper
- `GdsCoordinateExtractionTests.cs` - Integration tests for `extract_gds_coords.py`
- `GdsGroundTruthTests.cs` - Tests against `generate_reference_nazca.py` ground truth
- `GdsCoordinateVerificationTests.cs` - Full workflow using `compare_gds_coords.py`

---

## 📝 For AI Agents / Debugging

When the autonomous agent needs to debug GDS export issues:

### Quick Diagnosis Script:
```bash
#!/bin/bash
# diagnose_gds_export.sh

echo "=== GDS Export Diagnosis ==="

# 1. Generate ground truth
python Scripts/generate_reference_nazca.py /tmp/ref.gds /tmp/ref_coords.json

# 2. Export via C# (assumes test outputs to /tmp/test.gds)
dotnet test --filter "SimpleNazcaExporterTests"

# 3. Extract C# coordinates
python Scripts/extract_gds_coords.py /tmp/test.gds /tmp/test_coords.json

# 4. Compare
python Scripts/compare_gds_coords.py /tmp/ref_coords.json /tmp/test_coords.json

echo ""
echo "If mismatches found above, check:"
echo "  - PhysicalPin.GetAbsoluteNazcaPosition() method"
echo "  - SimpleNazcaExporter Y-flip calculations"
echo "  - NazcaOriginOffsetY values in ComponentTemplates"
```

### Key Files to Check:
- `Connect-A-Pic-Core/Components/Core/PhysicalPin.cs:GetAbsoluteNazcaPosition()`
- `CAP.Avalonia/Services/SimpleNazcaExporter.cs`
- `Connect-A-Pic-Core/Export/NazcaReferenceGenerator.cs` (constants)

---

## 🐛 Common Issues

### Python Not Found
```bash
# Check Python installation
python3 --version

# Install if missing (Ubuntu)
sudo apt install python3 python3-pip
```

### Nazca Not Installed
```bash
# Nazca is optional (only for generate_reference_nazca.py and reference_minimal.py)
pip install nazca

# Or skip if only using extract_gds_coords.py and compare_gds_coords.py
```

### Script Not Found in Tests
```csharp
// Tests look for scripts relative to repo root
var scriptPath = Path.Combine(repoRoot, "Scripts", "extract_gds_coords.py");

// If tests fail, check:
File.Exists(scriptPath).ShouldBeTrue("Script must exist in Scripts/ folder");
```

---

## 📚 References

- **Issue #329:** GDS coordinate bug (waveguide/pin misalignment)
- **PR #336:** Fix for `GetAbsoluteNazcaPosition()`
- **PR #337:** Added `extract_gds_coords.py` tool
- **PR #341:** Added `generate_reference_nazca.py` ground truth generator
- **TESTING.md:** Main testing guide for xUnit v3 + CTRF

---

**For questions or issues with these tools, check the C# test files in `UnitTests/Integration/` for usage examples.**
