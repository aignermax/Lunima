# Agent Instructions for Connect-A-PIC-Pro

**NOTE:** This is an example `CLAUDE.md` file configured for **Connect-A-PIC-Pro** (C# / Avalonia / MVVM project).

If you're using the Autonomous Issue Agent for a different project, adapt this file to your tech stack, architecture patterns, and coding standards.

---

This repository is maintained with the help of an autonomous AI agent.
Stability, clarity, and architectural discipline are more important than speed.

---

## Implementation Guidelines: When to Include UI

**Determine the scope based on the issue type:**

### **User-Facing Features (Full Vertical Slice Required)**
When the issue explicitly requests a UI element or user interaction:
- Keywords: "add button", "implement dialog", "user can", "add panel", "new UI"
- Complete vertical slice includes ALL layers:
  1. **Core logic** — New classes in `Connect-A-Pic-Core/`
  2. **ViewModel** — `ObservableObject` in `CAP.Avalonia/ViewModels/` with `[ObservableProperty]` and `[RelayCommand]`
  3. **View / AXAML** — UI panel in `CAP.Avalonia/Views/` or a new section in `MainWindow.axaml`
  4. **DI wiring** — Register new services in `CAP.Avalonia/App.axaml.cs` if needed
  5. **Unit tests** — xUnit tests in `UnitTests/` for core logic
  6. **Integration tests** — Core + ViewModel integration tests in `UnitTests/`

### **Core Features / Bug Fixes / Tests (NO UI Required)**
When the issue focuses on logic, testing, or investigation:
- Keywords: "investigate", "add test", "fix bug", "verify", "improve algorithm", "optimize"
- Implementation scope:
  1. **Core logic only** — New/modified classes in `Connect-A-Pic-Core/`
  2. **Unit tests** — Comprehensive xUnit tests
  3. **Integration tests** — If needed to verify behavior
  4. **NO ViewModel, NO View, NO UI** — unless explicitly requested

**Default assumption:** If the issue doesn't explicitly mention UI, don't create UI.
The human developer will ask for UI if needed.

### Exception: Debug and Testing Tools

Tools that exist purely for automated testing, CI validation, or developer debugging do **NOT** require UI panels. These should include:

- **Python scripts** in `Scripts/` folder
- **Backend service classes** for script execution (e.g., `GdsCoordinateExtractor.cs`)
- **Unit tests** demonstrating usage
- **Documentation** in CLAUDE.md section 11 (GDS Export Testing & Debugging Tools)

**Examples of debug tools**: GDS coordinate extractors, Nazca reference generators, coordinate comparison scripts.

**When creating debug tools**: Keep backend + tests + documentation. Do NOT add UI panels to MainWindow.

---

## 1. Architecture Rules

- Follow SOLID principles strictly.
- **Maximum 250 lines per NEW file.** Existing large files (MainViewModel.cs, DesignCanvas.cs) should not be refactored just for line count.
- No God classes — one responsibility per class.
- Prefer composition over inheritance.
- Avoid deep inheritance hierarchies.
- Use dependency injection where appropriate (constructor injection).
- **Only create interfaces when multiple implementations exist.** Concrete classes are fine otherwise.
- Do not introduce unnecessary abstractions.
- Do not refactor unrelated modules.
- Never modify UI or Routing unless explicitly required by the issue.

When in doubt: choose the simplest correct solution.

---

## 2. Code Structure

- Small, composable classes.
- Methods should generally not exceed ~20 lines.
- No large static utility classes.
- Avoid hidden side effects.
- Favor explicitness over cleverness.
- Keep changes minimal and localized.
- Prefer early returns over nested if/else.
- Max 2-3 levels of nesting.

### Folder Organization

**Keep folders organized and logically structured.**

- **Maximum 8-10 files per folder** before creating subfolders
- Group related classes into meaningful subfolders
- Use clear, descriptive subfolder names that reflect their purpose

**Examples:**

If `CAP.Avalonia/ViewModels/` has many files, organize by feature:
```
CAP.Avalonia/ViewModels/
  Analysis/
    ParameterSweepViewModel.cs
    OptimizationViewModel.cs
  Components/
    ComponentLibraryViewModel.cs
    PdkManagerViewModel.cs
  Layout/
    DesignCanvasViewModel.cs
    RoutingViewModel.cs
```

If `Connect-A-Pic-Core/Components/` grows large:
```
Connect-A-Pic-Core/Components/
  Waveguides/
    WaveguideConnection.cs
    WaveguideRouter.cs
  Couplers/
    GratingCoupler.cs
    DirectionalCoupler.cs
  PDK/
    PdkInfo.cs
    PdkLoader.cs
```

**When adding new features:**
- Check if the target folder already has 8+ files
- If yes, create or use an appropriate subfolder
- Follow existing subfolder patterns in the codebase
- Don't create subfolders with only 1-2 files (wait until there's 3+)

---

## 3. Code Style

- C# naming conventions:
  - PascalCase for public members
  - _camelCase for private fields
  - No abbreviations except well-known ones (VM, DI, etc.)
- Every public class and method must have XML documentation.
- No magic numbers — use named constants.
- Prefer readonly fields and immutable data where possible.
- Use clear, intention-revealing names.

---

## 4. MVVM Pattern (CommunityToolkit.Mvvm)

All ViewModels must:
- Inherit from `ObservableObject`
- Use `[ObservableProperty]` for bindable properties
- Use `[RelayCommand]` for user actions
- Be registered in DI container (`CAP.Avalonia/App.axaml.cs`)

Example:
```csharp
public partial class MyFeatureViewModel : ObservableObject
{
    [ObservableProperty]
    private string _resultText = "";

    [ObservableProperty]
    private bool _isProcessing;

    [RelayCommand]
    private async Task RunAnalysis()
    {
        IsProcessing = true;
        // ... do work
        IsProcessing = false;
    }
}
```

Reference: `CAP.Avalonia/ViewModels/ParameterSweepViewModel.cs`

---

## 5. Views (Avalonia AXAML)

- Use `x:DataType="vm:YourViewModel"` for compiled bindings
- Follow existing MainWindow layout pattern
- New feature panels go in the Right panel (properties area) as collapsible sections
- Use clear visual separators between sections
- Follow Parameter Sweep panel pattern in `MainWindow.axaml` (lines 193-229)

---

## 6. Testing

- Write unit tests for all new logic.
- Test file naming: `{ClassName}Tests.cs`
- Use xUnit with `[Fact]` and `[Theory]` attributes
- Shouldly for assertions: `result.ShouldBe(expected)`, `value.ShouldBeGreaterThan(0)`
- Moq for mocking: `new Mock<IService>()`
- Tests must be independent and deterministic.
- Cover edge cases and failure scenarios.
- Do not remove existing tests unless explicitly required.

**Integration tests** (Core + ViewModel):
```csharp
[Fact]
public void ViewModel_ReflectsCoreAnalysisResults()
{
    var coreService = new MyAnalyzer();
    var vm = new MyFeatureViewModel(coreService);
    vm.RunAnalysisCommand.Execute(null);
    vm.ResultText.ShouldNotBeNullOrEmpty();
}
```

Reference: `UnitTests/Analysis/ParameterSweeperTests.cs`

---

## 7. Implementation Recipes

### **Recipe A: User-Facing Feature (with UI)**
When the issue explicitly requests UI ("add button", "user can", "implement dialog"):

1. **Core class** in `Connect-A-Pic-Core/` (max 250 lines)
2. **ViewModel** in `CAP.Avalonia/ViewModels/MyFeatureViewModel.cs`
   - Inherit `ObservableObject`
   - Use `[ObservableProperty]` and `[RelayCommand]`
3. **Add ViewModel property** to `MainViewModel`:
   ```csharp
   public MyFeatureViewModel MyFeature { get; } = new();
   ```
4. **Add AXAML panel** in `MainWindow.axaml` (right panel section)
5. **Register in DI** if needed (`App.axaml.cs`)
6. **Unit tests** for core class
7. **Integration test** for Core→ViewModel flow

### **Recipe B: Core Feature / Bug Fix / Test (NO UI)**
When the issue focuses on logic, tests, or investigation:

1. **Core class** in `Connect-A-Pic-Core/` (max 250 lines)
2. **Unit tests** for all new/modified logic
3. **Integration tests** if needed to verify behavior
4. **Stop here** - NO ViewModel, NO View, NO AXAML

**The issue title determines which recipe to use.**

---

## 8. Build & Verification

Before finishing work:

1. Run `dotnet build`
2. **Run tests using `smart_test.py`** (preferred) or `dotnet test` (fallback)
3. Fix all build errors.
4. Fix all failing tests.
5. Ensure no new warnings are introduced unnecessarily.

**Do not stop until build AND tests pass.**

### Testing Best Practices

**Use `smart_test.py` for autonomous agents** - it filters output to prevent token overflow:

```bash
# Run all tests (concise output)
python3 tools/smart_test.py

# Run specific test pattern
python3 tools/smart_test.py FrozenPathObstacle

# Run tests in specific file
python3 tools/smart_test.py --file ParameterSweeperTests.cs

# Full output if needed
python3 tools/smart_test.py --verbose
```

**Benefits over `dotnet test`:**
- 📉 **90% less output** - only shows summary + failures
- 🎯 **Agent-friendly** - structured format, easy to parse
- ⚡ **Faster** - minimal verbosity, quick feedback
- 💾 **Token-efficient** - doesn't spam build warnings

**Fallback to `dotnet test` only when:**
- `smart_test.py` is not available
- You need raw dotnet output for debugging
- Integration with CI/CD requires it

---

## 8.1. Available Python Tools (Token Optimization)

Use these Python scripts in `tools/` to **dramatically reduce token usage** when working on issues:

### 🔍 **Semantic Code Search** (`semantic_search.py`)

**Use instead of:** Grep, file browsing, reading multiple files to find implementations

```bash
# Find ViewModel implementations for analysis features
python3 tools/semantic_search.py "ViewModel for analysis features"

# Find routing obstacle code
python3 tools/semantic_search.py "pathfinding grid obstacle detection"

# Find similar test patterns
python3 tools/semantic_search.py "integration test for component serialization"
```

**Benefits:**
- 🎯 **Intent-based search** - understands what you're looking for, not just keywords
- ⚡ **Sub-second results** - cached embeddings, nearly instant
- 💾 **90% token savings** - returns top 5 matches instead of reading 50+ files
- 🧠 **Smart matching** - finds semantically similar code, not just string matches

**When to use:**
- "Find all classes that do X"
- "Where is feature Y implemented?"
- "Show me examples of Z pattern"
- Exploring unfamiliar codebase areas

**When to rebuild index:**
```bash
# After major refactoring or adding many files
python3 tools/semantic_search.py --rebuild
```

### 🧪 **Smart Test Runner** (`smart_test.py`)

Already covered in Section 8 - use for all test runs to save tokens.

### 📊 **Tool Usage Reporting**

When you complete an issue, report which tools you used:

**Example:**
```
✅ Implementation complete!

Tools used:
- semantic_search.py: Found 3 similar ViewModel patterns (saved ~15K tokens vs reading files)
- smart_test.py: Ran 47 tests, concise output (saved ~8K tokens vs dotnet test)

Total token savings: ~85% compared to traditional file reading/testing
```

**Why this matters:**
- Helps verify tools are working
- Demonstrates efficiency gains
- Guides future optimization

**If tools aren't available or don't work** - that's also valuable feedback to report!

---

## 9. Git Discipline

- Only modify files related to the issue.
- Keep commits focused and minimal.
- Do not change formatting of unrelated files.
- Do not introduce broad refactoring unless required.
- Do not merge — only prepare changes for review.

---

## 10. Simulation Integrity

The core of this repository is photonic S-Matrix-based simulation.

- Preserve physical plausibility.
- Avoid introducing numerical instability.
- Prefer validation over silent assumptions.
- If uncertain about physics correctness, choose the conservative approach.

---

## 11. GDS Export Testing & Debugging Tools

**CRITICAL for Issue #329 and GDS coordinate bugs.**

### Python Tools in `Scripts/` Folder

These tools are **integrated with C# tests** to find GDS export bugs:

| Script | Purpose | Used By |
|--------|---------|---------|
| `extract_gds_coords.py` | Extract all polygon/path coordinates from GDS to JSON | `GdsCoordinateExtractorTests.cs` |
| `generate_reference_nazca.py` | Generate ground-truth GDS with known coordinates | `GdsGroundTruthTests.cs` |
| `compare_gds_coords.py` | Compare two GDS files numerically, report deviations | `GdsCoordinateVerificationTests.cs` |

### How to Use for Debugging

**When you see GDS coordinate bugs (waveguides not connecting to pins):**

```bash
# 1. Generate reference (ground truth)
python Scripts/generate_reference_nazca.py /tmp/ref.gds /tmp/ref_coords.json

# 2. Export your design via C#
dotnet test --filter "SimpleNazcaExporterTests"
# Creates: /tmp/test.gds

# 3. Extract coordinates
python Scripts/extract_gds_coords.py /tmp/test.gds /tmp/test_coords.json

# 4. Compare and find mismatches
python Scripts/compare_gds_coords.py /tmp/ref_coords.json /tmp/test_coords.json
# Reports: ❌ MISMATCH ΔY = 9.50 µm → Bug found!
```

### Files to Check When GDS Bugs Found

- `Connect-A-Pic-Core/Components/Core/PhysicalPin.cs` → `GetAbsoluteNazcaPosition()` method
- `CAP.Avalonia/Services/SimpleNazcaExporter.cs` → Y-flip calculations
- `Connect-A-Pic-Core/Export/NazcaReferenceGenerator.cs` → Ground truth constants

### Nazca Coordinate Convention

```
Nazca Y = -(PhysicalY + NazcaOriginOffsetY)
Pin stub local Y = ComponentHeight - PinOffsetY
```

**These tools saved us from the 9.5 µm grating coupler bug!**

---

## 12. Key File Reference

| Purpose | Path |
|---------|------|
| DI container setup | `CAP.Avalonia/App.axaml.cs` |
| Main ViewModel | `CAP.Avalonia/ViewModels/MainViewModel.cs` |
| Main Window layout | `CAP.Avalonia/Views/MainWindow.axaml` |
| Example ViewModel | `CAP.Avalonia/ViewModels/ParameterSweepViewModel.cs` |
| Example unit tests | `UnitTests/Analysis/ParameterSweeperTests.cs` |
| Test helpers | `UnitTests/Helpers/TestComponentFactory.cs` |
| GDS testing tools | `Scripts/extract_gds_coords.py`, `Scripts/compare_gds_coords.py` |

---

**The goal is a stable, modular, physically meaningful simulation tool with a complete UI — not a backend-only prototype.**
