# Agent Instructions for Connect-A-PIC-Pro

C# / Avalonia / MVVM photonic simulation tool. Stability, clarity, and architectural discipline are more important than speed.

---

## Implementation Guidelines: When to Include UI

**User-Facing Features (with UI):** Keywords: "add button", "implement dialog", "user can", "add panel"
- Full stack: Core class → ViewModel (`[ObservableProperty]`, `[RelayCommand]`) → AXAML panel → DI wiring → Tests

**Core Features / Bug Fixes (NO UI):** Keywords: "investigate", "add test", "fix bug", "verify", "optimize"
- Core class → Tests → **STOP** (no ViewModel/View unless explicitly requested)

**Debug Tools (NO UI):** Python scripts in `Scripts/`, backend service classes, tests, documentation only

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

- **Max 8-10 files per folder** → create subfolders
- Group by feature (e.g., `ViewModels/Analysis/`, `ViewModels/Components/`)
- Don't create subfolders with <3 files

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

- Write unit tests for all new logic (`{ClassName}Tests.cs`)
- xUnit: `[Fact]`, `[Theory]` | Shouldly: `result.ShouldBe(expected)` | Moq: `new Mock<IService>()`
- Tests must be independent and deterministic
- Cover edge cases, don't remove existing tests

Reference: `UnitTests/Analysis/ParameterSweeperTests.cs`

---

## 7. Implementation Recipes

**Recipe A (User-Facing Feature with UI):** Core class → ViewModel (`[ObservableProperty]`, `[RelayCommand]`) → AXAML panel → Tests
**Recipe B (Core/Bug Fix - NO UI):** Core class → Tests → **STOP** (no ViewModel/View)

Issue title determines which recipe to use.

---

## 8. Build & Verification

Before finishing work:

1. Run `dotnet build`
2. **REQUIRED: Run tests using `python3 tools/smart_test.py`** (NOT `dotnet test`!)
3. Fix all build errors.
4. Fix all failing tests.
5. Ensure no new warnings are introduced unnecessarily.

**Do not stop until build AND tests pass.**

### Testing: Use `smart_test.py` (MANDATORY)

**⚠️ avoid using `dotnet test` directly - it outputs 100K+ chars!**

```bash
python3 tools/smart_test.py                     # All tests
python3 tools/smart_test.py FrozenPathObstacle  # Pattern match
python3 tools/smart_test.py --file MyTests.cs   # Specific file
```

**Why:** 90% less output, agent-friendly format, prevents context overflow.

---

## 8.1. REQUIRED Python Tools (Token Optimization)

**⚠️ MANDATORY: Use Python tools in `tools/` - NOT MCP (doesn't work in headless mode)!**

### 🔍 Semantic Search (`semantic_search.py`)

Intent-based code discovery - use instead of grep/reading multiple files:

```bash
python3 tools/semantic_search.py "ViewModel for analysis features"
python3 tools/semantic_search.py "pathfinding grid obstacle"
python3 tools/semantic_search.py --rebuild  # After major refactoring
```

**Benefits:** Sub-second results, 90% token savings (top 5 matches vs 50+ file reads), smart semantic matching

**Use when:** Finding classes/implementations, exploring unfamiliar areas, looking for examples

### 📊 Tool Usage Reporting (REQUIRED)

Report at end of each issue:
```
✅ Complete! Tools: semantic_search.py (3 searches, ~15K saved), smart_test.py (~100K saved)
```

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

## 11. GDS Export Debugging (Issue #329)

**Python tools in `Scripts/` for GDS coordinate bugs:**

| Script | Purpose |
|--------|---------|
| `extract_gds_coords.py` | Extract polygon/path coordinates to JSON |
| `generate_reference_nazca.py` | Generate ground-truth GDS |
| `compare_gds_coords.py` | Compare two GDS, report deviations |

**Debugging workflow:**
```bash
python Scripts/generate_reference_nazca.py /tmp/ref.gds /tmp/ref_coords.json
python Scripts/extract_gds_coords.py /tmp/test.gds /tmp/test_coords.json
python Scripts/compare_gds_coords.py /tmp/ref_coords.json /tmp/test_coords.json
```

**Files to check:** `PhysicalPin.cs::GetAbsoluteNazcaPosition()`, `SimpleNazcaExporter.cs`, `NazcaReferenceGenerator.cs`

**Convention:** `Nazca Y = -(PhysicalY + NazcaOriginOffsetY)` | `Pin stub local Y = ComponentHeight - PinOffsetY`

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
