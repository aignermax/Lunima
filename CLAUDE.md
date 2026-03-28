# Agent Instructions for Connect-A-PIC-Pro

**NOTE:** This is an example `CLAUDE.md` file configured for **Connect-A-PIC-Pro** (C# / Avalonia / MVVM project).

If you're using the Autonomous Issue Agent for a different project, adapt this file to your tech stack, architecture patterns, and coding standards.

---

This repository is maintained with the help of an autonomous AI agent.
Stability, clarity, and architectural discipline are more important than speed.

---

## CRITICAL: Vertical Slice Requirement

**Every feature implementation MUST be a complete vertical slice.**
Do NOT submit backend-only or core-only code. Every PR must include user-testable UI.

A complete vertical slice includes ALL of these layers:

1. **Core logic** — New classes in `Connect-A-Pic-Core/` (concrete classes are fine, use interfaces only when multiple implementations are needed)
2. **ViewModel** — `ObservableObject` in `CAP.Avalonia/ViewModels/` with `[ObservableProperty]` and `[RelayCommand]`
3. **View / AXAML** — UI panel in `CAP.Avalonia/Views/` or a new section in `MainWindow.axaml`
4. **DI wiring** — Register new services in `CAP.Avalonia/App.axaml.cs` if needed
5. **Unit tests** — xUnit tests in `UnitTests/` for core logic
6. **Integration tests** — Core + ViewModel integration tests in `UnitTests/` (place alongside related unit tests)

**This is NON-NEGOTIABLE.** The human developer must be able to test the feature in the UI immediately after PR merge.

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

## 7. Recipe: Adding a New Feature

Follow this checklist for EVERY new feature:

1. **Core class** in `Connect-A-Pic-Core/Analysis/` or appropriate folder (max 250 lines)
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

**Do not skip ANY of these steps.**

---

## 8. Build & Verification

Before finishing work:

1. Run `dotnet build`
2. Run `dotnet test`
3. Fix all build errors.
4. Fix all failing tests.
5. Ensure no new warnings are introduced unnecessarily.

**Do not stop until build AND tests pass.**

---

## 8.1. MCP Tool Usage Reporting

When you complete an issue, include a brief summary in your final message about which MCP tools you used:

**Example:**
```
✅ Implementation complete!

MCP Tools used:
- OpenViking: Used semantic search to find similar routing implementations (3 searches)
- NetContextServer: Analyzed project dependencies for Grid module
- dotnet-test-mcp: Ran 47 unit tests, all passing

Token savings: Estimated ~85% reduction using semantic search vs full file reads.
```

**Why this matters:**
- Helps verify MCP servers are working correctly
- Shows token optimization effectiveness
- Provides feedback for improving the agent setup

If you didn't use any MCP tools, that's also valuable information to report.

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
