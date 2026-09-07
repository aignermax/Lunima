# Agent Instructions for Connect-A-PIC-Pro

C# / Avalonia / MVVM photonic simulation tool. Stability, clarity, and architectural discipline are more important than speed.

---

## User Personas (UX North Star)

**Before any UX/UI design, redesign, or feature-prioritization work, read [`docs/PERSONAS.md`](docs/PERSONAS.md).**

It defines who Lunima is built for — Peter (Figma-precision GDS layout engineer), Priya (academic lab with own fab, GDSFactory + Tidy3D, measurement feedback), Mirko (full-stack system simulation, open formats, netlist export, photonic IR), and Ingrid (investor, demo path) — including per-persona design implications and the priority order when they conflict. Justify UX decisions against these personas in issues and PRs.

---

## Implementation Guidelines: When to Include UI

**User-Facing Features (with UI):** Keywords: "add button", "implement dialog", "user can", "add panel"
- Full stack: Core class → ViewModel (`[ObservableProperty]`, `[RelayCommand]`) → AXAML view **on the right surface (window / dialog / flyout / canvas overlay — see §5; NOT the right sidebar)** → DI wiring → Tests → **screenshots of the new UI in the PR**

**Core Features / Bug Fixes (NO UI):** Keywords: "investigate", "add test", "fix bug", "verify", "optimize"
- Core class → Tests → **STOP** (no ViewModel/View unless explicitly requested)

**Debug Tools (NO UI):** Python scripts in `scripts/`, backend service classes, tests, documentation only

---

## 1.1 Vertical Slice Convention

Lunima uses a **vertical-slice** layout: each feature ships with mirrored subfolders across all layers, named identically.

### Folder pattern

```
Connect-A-Pic-Core/<DomainArea>/<FeatureName>/
CAP-DataAccess/<RelevantArea>/<FeatureName>/   (only if persistence is touched)
CAP.Avalonia/ViewModels/<RelevantArea>/<FeatureName>/
CAP.Avalonia/Views/Panels/<FeatureName>Panel.axaml   (only for panel features)
UnitTests/<RelevantArea>/<FeatureName>/
```

**Concrete examples:**
- GDS preview (Issue #525): `CAP.Avalonia/Controls/Canvas/ComponentPreview/` + tests
- ONA sweep (Issue #526): `Connect-A-Pic-Core/Analysis/OnaAnalysis/` + `CAP.Avalonia/ViewModels/Analysis/OnaAnalysis/` + tests
- Time-domain (Issue #527): `Connect-A-Pic-Core/LightCalculation/TimeDomainSimulation/` + tests
- Nazca override (Issue #528): `CAP.Avalonia/Services/ComponentInstanceOverride/` + tests

### Cross-feature dependency rule

A feature's code **must only** import from:
- Its own namespace
- The shared kernel (see allow-list below)
- Platform / framework namespaces (`System.*`, `Avalonia.*`, `Microsoft.*`, `CommunityToolkit.*`)

**Shared kernel allow-list:**
`CAP_Core.Components`, `CAP_Core.Helpers`, `CAP_Core.Grid`, `CAP_Core.Tiles`,
`CAP_Core.ExternalPorts`, `CAP_Core.Routing`, `CAP_Core.LightCalculation`,
`CAP_Core.Resources`, `CAP_Contracts`, `CAP.Avalonia.Services`,
`CAP.Avalonia.ViewModels.Canvas`, `CAP.Avalonia.ViewModels.Panels`, `CAP_DataAccess`

Architecture tests in `UnitTests/Architecture/VerticalSliceConventionTests.cs` enforce this rule for enumerated features and will fail CI if violated.

### DI registration

Each feature group has a dedicated extension method in `CAP.Avalonia/DI/`:

```csharp
public static IServiceCollection AddExportFeature(this IServiceCollection s);
public static IServiceCollection AddAiAssistantFeature(this IServiceCollection s);
```

`App.axaml.cs` calls these methods instead of raw `services.AddSingleton<X>()` lines.
To add a service: add the registration inside the relevant extension method, not directly in `App.axaml.cs`.

### What "vertical slice" is NOT

- Prism, MediatR, or any modular plug-in framework — **not adopted**.
- Splitting the solution into multiple `.csproj` files — **out of scope**.
- Mandatory for every single file — **only for new cross-cutting features**.

---

## 1.2 Cross-Platform Parity (macOS / Linux / Windows)

Lunima ships for **macOS (arm64 + x64), Linux (x64), and Windows (x64) together**. A change that only works on one OS is a bug, not a partial win — never let the Mac build fall behind Windows/Linux or vice versa. Most changes are made by agents, so treat this as a hard rule.

**Never call `Process.Start` / `new ProcessStartInfo` directly.** PATH and executable discovery differ per OS (a Finder/Dock launch on macOS has no shell PATH; Windows uses `python`/`py`, Linux `python3`). Route every launch through the shared abstractions:

- **External tools (python, docker, …):** take a `CAP_Core.Export.ProcessLaunchFactory` constructor param defaulting to `ProcessLaunchFactory.CreateDefault()` (the DI singleton is registered in `CAP.Avalonia/DI/CoreServicesExtensions.cs`). Build the `ProcessStartInfo` with `factory.TryBuild(...)`. It honours an explicit/rooted path verbatim and only probes well-known locations for **bare** names **on macOS** — do not reintroduce per-OS probing that overrides a working PATH on Linux/Windows (that breaks the Linux CI runner).
- **Opening a URL / file / reveal-in-folder:** take a `CAP.Avalonia.Services.IUrlLauncher` (`Open` / `OpenFileOrDirectory` / `RevealInFileManager`); `PlatformShellLauncher` already maps these to `open` / `xdg-open` / `explorer`. Never hardcode `explorer.exe /select` or `open -R`.

**Other parity rules:**
- **Paths:** `Path.Combine`, never a literal `\` or `/`; no drive-letter or `%USERPROFILE%` assumptions. Branch with `OperatingSystem.IsMacOS()/IsLinux()/IsWindows()` and `Environment.SpecialFolder`.
- **Machine-facing strings** (export scripts, serialization, GDS/Nazca params): format with `CultureInfo.InvariantCulture` so a locale-`de` runner doesn't emit `1,5`.
- **`.csproj`:** Windows-only properties (`WinExe`, `ApplicationManifest`, `BuiltInComInteropSupport`, `ApplicationIcon`) must carry `Condition="'$(RuntimeIdentifier)' == '' Or $(RuntimeIdentifier.StartsWith('win'))"` (see `CAP.Desktop/CAP.Desktop.csproj`). Any new RID goes in `<RuntimeIdentifiers>win-x64;linux-x64;osx-arm64;osx-x64</RuntimeIdentifiers>`.
- **CI / releases (`.github/workflows/Build_Exe.yaml`):** a release must produce a downloadable artifact for **all three** OSes. It has parallel `build` (win/linux portable), `build-msi` (Windows installer), and `build-macos` (`.dmg`) jobs, and the `release` job attaches all of them. If you add/change packaging for one OS, add the equivalent for the others (or state explicitly in the PR why not). The macOS `.app` is assembled by `scripts/build_macos_bundle.sh`.
- **Tests:** guard platform-specific assertions with `OperatingSystem.IsX()` and use the abstractions, so the suite is green on the **Linux CI runner** (`🔍 xUnit Tests`), not just locally. A path/executable substitution that overrides the runner's PATH passes locally and fails CI — don't.

**Before opening a PR that touches launching, paths, formatting, build, or packaging:** would this behave identically on macOS, Linux, and Windows? If you can't run all three, reason about each and note it in the PR.

*Enforced:* `UnitTests/Architecture/CrossPlatformProcessLaunchTests.cs` fails the `🔍 xUnit Tests` check if a `new ProcessStartInfo` or `Process.Start("…")` appears in production code outside the sanctioned launchers. New launching code must use the abstractions, not extend that allowlist.

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
- Comments are rare: they explain only *why* something exists or behaves unusually, never *what* the code already says. PR/issue references ("#776", "field finding", "PR review") do not belong in code comments — they belong in the PR description.
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

## 5. Views (Avalonia AXAML) — UI placement is a design decision

- Use `x:DataType="vm:YourViewModel"` for compiled bindings.
- **Do NOT add new features to the right sidebar.** The right panel is for *properties of the
  current selection* only. Stacking features there as collapsible sections has produced a
  cluttered, hard-to-find UI — this rule reverses the old "new panels go in the Right panel"
  guidance on purpose.
- **Decide placement like a UX designer, and write the decision into the PR body** ("Placement:
  … because …"). Pick the surface that fits the interaction:

  | Feature type | Surface |
  |---|---|
  | Acts on the selected component/connection | Properties panel (right) — the *only* case for the sidebar |
  | Acts on the whole design / has its own workflow (analysis, sweep, import, export, run mode) | **Own window or dockable tool window** (`Views/*Window.axaml`, e.g. `ProcessManagementWindow`) |
  | Short, focused task with a few inputs | **Dialog** (`Views/Dialogs/`, e.g. `GdsImportDialog`) |
  | Contextual, transient information or a small choice | **Flyout / popup** anchored to the element (e.g. `HelpFlyoutButton`, probe flyout) |
  | Spatial feedback about the design | **Canvas overlay** (guides, badges, power-flow, DRC markers) |
  | Frequent action | **Toolbar button / context menu / shortcut** — never a buried panel |

- Features must be **discoverable**: a visible entry point (toolbar, menu, canvas), not only a
  keyboard shortcut or a nested expander.
- **Help `(?)` content: animate, don't lecture.** Use the shared `HelpFlyoutButton` control. Text is
  capped at ~3 short sentences per section; anything physically non-obvious gets an **illustrative
  animation** (Avalonia `Animation`/`Transitions`, see `TransientHelpFlyout` / `EyeHelpFlyout`) —
  light moving through the structure, a curve reacting to a parameter — not a wall of text.
- **Performance budget:** no UI interaction may block the UI thread for more than ~100 ms. Heavy
  work (routing, simulation, import) runs off-thread with a visible busy state and stays
  cancellable. Where a feature adds computation, add a test asserting the command completes
  asynchronously (UI thread free).
- **Physical plausibility on screen:** components and routes must not overlap unintentionally;
  run the DRC-lite checks (`DesignValidator`) on any design your feature creates or modifies.
- Justify placement and UX against `docs/PERSONAS.md` (Jonas: learnability; Peter: no modal
  interruptions; Ingrid: obvious demo path).

---

## 6. Testing

- Write unit tests for all new logic (`{ClassName}Tests.cs`)
- xUnit: `[Fact]`, `[Theory]` | Shouldly: `result.ShouldBe(expected)` | Moq: `new Mock<IService>()`
- Tests must be independent and deterministic
- Cover edge cases, don't remove existing tests

Reference: `UnitTests/Analysis/ParameterSweeperTests.cs`

---

## 7. Implementation Recipes

**Recipe A (User-Facing Feature with UI):** Core class → ViewModel (`[ObservableProperty]`, `[RelayCommand]`) → AXAML view on the surface chosen per §5 (window / dialog / flyout / overlay; **not** a sidebar section) → Tests → headless screenshots of the new UI attached to the PR (`UnitTests/UI/*ScreenshotTests` pattern) → update `docs/RELEASE-CHECKLIST.md`
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

**Slow tests are CI-only locally:** heavyweight tests (full-MainWindow flows, all-component
renders, Skia screenshot walkthroughs) carry `[Trait("Category", "Slow")]`. Never run the
unfiltered suite on a local desktop session — it can starve the display manager and crash
the machine. `make test` and `.agent.toml` already exclude `Category!=Slow`; with
`smart_test.py` set `SMART_TEST_EXCLUDE_CATEGORY=Slow` (the `safe_test.sh` wrapper does
this + CPU throttling). CI runs everything unfiltered, so nothing loses coverage.

---

## 8.1. REQUIRED Python Tools (Token Optimization)

**⚠️ MANDATORY: Use Python tools - NOT MCP (doesn't work in headless mode)!**

### Tool Installation & Location

**Tools can be in two locations:**
1. `~/.cap-tools/` (recommended for persistent installation)
2. `tools/` (inside repository, for agent sessions)

**If tools are missing, install them:**
```bash
# Clone tools repository
git clone https://github.com/aignermax/python-dev-tools.git /tmp/python-dev-tools

# Option 1: Install to ~/.cap-tools/ (persistent)
mkdir -p ~/.cap-tools
cp /tmp/python-dev-tools/smart_test.py ~/.cap-tools/
cp /tmp/python-dev-tools/semantic_search.py ~/.cap-tools/

# Option 2: Install to tools/ (repository-local)
mkdir -p tools
cp /tmp/python-dev-tools/smart_test.py tools/
cp /tmp/python-dev-tools/semantic_search.py tools/

# Clean up
rm -rf /tmp/python-dev-tools
```

**Usage (try both locations):**
```bash
# Preferred (persistent installation):
python3 ~/.cap-tools/smart_test.py
python3 ~/.cap-tools/semantic_search.py

# Alternative (repository-local):
python3 tools/smart_test.py
python3 tools/semantic_search.py
```

### 🔍 Semantic Search (`semantic_search.py`)

Intent-based code discovery - use instead of grep/reading multiple files:

```bash
python3 ~/.cap-tools/semantic_search.py "ViewModel for analysis features"
python3 ~/.cap-tools/semantic_search.py "pathfinding grid obstacle"
python3 ~/.cap-tools/semantic_search.py --rebuild  # After major refactoring
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

**Python tools in `scripts/` for GDS coordinate bugs:**

| Script | Purpose |
|--------|---------|
| `extract_gds_coords.py` | Extract polygon/path coordinates to JSON |
| `generate_reference_nazca.py` | Generate ground-truth GDS |
| `compare_gds_coords.py` | Compare two GDS, report deviations |

**Debugging workflow:**
```bash
python scripts/generate_reference_nazca.py /tmp/ref.gds /tmp/ref_coords.json
python scripts/extract_gds_coords.py /tmp/test.gds /tmp/test_coords.json
python scripts/compare_gds_coords.py /tmp/ref_coords.json /tmp/test_coords.json
```

**Files to check:** `PhysicalPin.cs::GetAbsoluteNazcaPosition()`, `SimpleNazcaExporter.cs`, `NazcaReferenceGenerator.cs`

**Convention:** `Nazca Y = -(PhysicalY + NazcaOriginOffsetY)` | `Pin stub local Y = ComponentHeight - PinOffsetY`

---

## 12. Key File Reference

| Purpose | Path |
|---------|------|
| User personas (UX reference) | `docs/PERSONAS.md` |
| DI container setup | `CAP.Avalonia/App.axaml.cs` |
| Main ViewModel | `CAP.Avalonia/ViewModels/MainViewModel.cs` |
| Main Window layout | `CAP.Avalonia/Views/MainWindow.axaml` |
| Example ViewModel | `CAP.Avalonia/ViewModels/ParameterSweepViewModel.cs` |
| Example unit tests | `UnitTests/Analysis/ParameterSweeperTests.cs` |
| Test helpers | `UnitTests/Helpers/TestComponentFactory.cs` |
| GDS testing tools | `scripts/extract_gds_coords.py`, `scripts/compare_gds_coords.py` |

---

**The goal is a stable, modular, physically meaningful simulation tool with a complete UI — not a backend-only prototype.**
