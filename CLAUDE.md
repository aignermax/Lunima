# CLAUDE.md — Instructions for Claude Code Agent

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

## Code Quality Rules

- **Max 250 lines per file** — Aim for 250 lines in newly created files. When modifying existing files that exceed 300 lines, look for opportunities to extract cohesive logic into separate classes. Files over 500 lines should be actively refactored when touched. No backwards compatibility needed — just move the code
- **SOLID principles**:
  - Single Responsibility: Each class has one reason to change
  - Open/Closed: Extend via interfaces, not modification
  - Liskov Substitution: Subtypes must be substitutable for base types
  - Interface Segregation: Prefer small, focused interfaces
  - Dependency Inversion: Use constructor injection. Concrete classes are fine — only create interfaces when there are multiple implementations
- **Clean Code**:
  - Meaningful, descriptive names (no abbreviations except well-known ones like `VM`, `DI`)
  - Small methods (max ~20 lines per method)
  - No magic numbers — use named constants
  - No deep nesting (max 2-3 levels)
  - Prefer early returns over nested if/else

## Project Structure

```
Connect-A-Pic-Core/          # Core simulation engine (no UI dependencies)
├── Components/              # Component models, pins, S-matrices, parametric
│   ├── Core/                # Core component classes (Component, Pin, PhysicalPin, Part)
│   ├── Connections/         # Waveguide connections and types
│   ├── Parametric/          # Parametric components and formulas
│   ├── FormulaReading/      # Formula parsing and evaluation
│   ├── Creation/            # Component factory
│   └── ComponentHelpers/    # Component utilities
├── Routing/                 # Waveguide routing, A* pathfinding
├── LightCalculation/        # S-Matrix propagation, power flow analysis
├── Analysis/                # Parameter sweep, sweep configuration
├── Grid/                    # Component placement, connection management
├── ExternalPorts/           # Light source configuration
└── Helpers/                 # Utilities, collections

CAP_Contracts/               # Shared interfaces (IDataAccessor, ILogger)

CAP-DataAccess/              # JSON persistence, PDK loading
├── Components/ComponentDraftMapper/  # PdkLoader, DTOs
└── PDKs/                    # PDK JSON files (demo-pdk.json)

CAP.Avalonia/                # Shared cross-platform UI
├── ViewModels/              # MVVM ViewModels
│   ├── MainViewModel.cs     # Root ViewModel (keep at root)
│   ├── Canvas/              # Canvas and layout (DesignCanvasViewModel, AlignmentGuideViewModel, GridSnapSettings)
│   ├── Analysis/            # Analysis features (ParameterSweepViewModel)
│   ├── Diagnostics/         # Diagnostics panels (DesignValidationViewModel, RoutingDiagnosticsViewModel, etc.)
│   ├── Library/             # Component library (ComponentTemplates, PdkManagerViewModel, ElementLockViewModel)
│   ├── Simulation/          # Simulation config (LaserConfig, WavelengthOption)
│   └── Converters/          # Data converters (PathSegmentConverter)
├── Views/                   # AXAML views (MainWindow.axaml, MainView.axaml)
├── Commands/                # Undo/redo commands (IUndoableCommand, CommandManager)
├── Services/                # SimulationService, SimpleNazcaExporter, FileDialogService
├── Controls/                # Custom controls (DesignCanvas)
└── Visualization/           # Power flow rendering

CAP.Desktop/                 # Desktop entry point (Program.cs)
CAP.Browser/                 # WebAssembly entry point (planned)
UnitTests/                   # xUnit test suite
```

## Architecture Patterns

### MVVM (CommunityToolkit.Mvvm)

ViewModels inherit `ObservableObject`. Use source generators:

```csharp
public partial class MyFeatureViewModel : ObservableObject
{
    [ObservableProperty]
    private string _resultText = "";

    [ObservableProperty]
    private bool _isProcessing;

    // Auto-generates OnResultTextChanged partial method
    partial void OnResultTextChanged(string value) { }

    [RelayCommand]
    private async Task RunAnalysis() { }
}
```

Reference: `CAP.Avalonia/ViewModels/Analysis/ParameterSweepViewModel.cs`

### Dependency Injection

All services registered as singletons in `CAP.Avalonia/App.axaml.cs`:

```csharp
services.AddSingleton<SimulationService>();
services.AddSingleton<MyNewService>();
services.AddSingleton<MainViewModel>();
```

Use constructor injection. Access via `App.Services` only in code-behind when needed.

### Command Pattern (Undo/Redo)

Implement `IUndoableCommand` (in `CAP.Avalonia/Commands/ICommand.cs`):

```csharp
public class MyCommand : IUndoableCommand
{
    public string Description => "My action";
    public void Execute() { }
    public void Undo() { }
}
```

Execute via `CommandManager.ExecuteCommand(cmd)`.

### Views (Avalonia AXAML)

- Use `x:DataType="vm:YourViewModel"` for compiled bindings
- MainWindow layout: DockPanel with Top=toolbar, Bottom=status, Left=library, Right=properties, Center=canvas
- New feature panels go in the Right panel (properties area) as collapsible sections
- Follow the Parameter Sweep panel pattern in `MainWindow.axaml` (lines 193-229)

### Unit Tests

- xUnit with `[Fact]` and `[Theory]` attributes
- Shouldly for assertions: `result.ShouldBe(expected)`, `value.ShouldBeGreaterThan(0)`
- Moq for mocking: `new Mock<ILightCalculator>()`
- Test helpers in `UnitTests/Helpers/TestComponentFactory.cs`
- Reference: `UnitTests/Analysis/ParameterSweeperTests.cs`

### Integration Tests (Core + ViewModel)

Test that core services produce correct data and ViewModels expose it properly:

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

Reference: `UnitTests/Simulation/SimulationIntegrationTests.cs`

## Recipe: Adding a New Feature Panel

Follow this step-by-step when implementing a new analysis/diagnostic feature:

1. **Implement core class** in `Connect-A-Pic-Core/Analysis/MyAnalyzer.cs` (max 250 lines, no interface needed unless multiple implementations exist)
2. **Create ViewModel** in `CAP.Avalonia/ViewModels/MyFeatureViewModel.cs`
   - Inherit `ObservableObject`
   - Use `[ObservableProperty]` for bindable state
   - Use `[RelayCommand]` for actions
   - Follow `ParameterSweepViewModel` pattern
3. **Add ViewModel as property** on `MainViewModel`:
   ```csharp
   public MyFeatureViewModel MyFeature { get; } = new();
   ```
4. **Add AXAML panel** in `MainWindow.axaml` right panel section:
   ```xml
   <StackPanel IsVisible="{Binding SomeCondition}">
       <Separator Margin="0,20,0,10" Background="#3d3d3d"/>
       <TextBlock Text="My Feature:" Foreground="LightBlue" FontWeight="SemiBold"/>
       <!-- UI elements bound to MyFeature.* -->
   </StackPanel>
   ```
5. **Register in DI** (`App.axaml.cs`) if the core class needs to be a service
6. **Write unit tests** in `UnitTests/` for the core analyzer class
7. **Write integration test** in `UnitTests/` (alongside related tests) testing Core→ViewModel data flow

## Build and Verify

**MANDATORY: You MUST run BOTH commands before finishing ANY task.**

```bash
dotnet build
dotnet run --project UnitTests -- -ctrf test-results.ctrf.json
```

**This is NON-NEGOTIABLE.** Even if you did not write new tests, you MUST verify existing tests still pass.

### Why CTRF JSON?

- **Structured output**: JSON format is easy to parse (vs. thousands of text lines)
- **Token efficient**: ~400 tokens vs. ~2000 tokens for console output
- **Complete information**: Test names, status, duration, error messages, stack traces

If tests fail, you MUST fix them before creating a PR. Do NOT skip tests.

### Alternative: TRX XML (fallback)

If CTRF fails for any reason, use TRX as fallback:

```bash
dotnet test --logger "trx;LogFileName=test-results.trx"
```

**Note:** Prefer CTRF over TRX (CTRF is more compact and structured).

## Branch and PR Conventions

- **Branch naming**: `feature/issue-{number}-short-description`
  - Example: `feature/issue-42-loss-budget-analyzer`
- **PR body must include**:
  - `Closes #{issue_number}`
  - Vertical slice checklist (which layers were implemented)
  - How to manually test the feature in the UI

## Key File Reference

| Purpose | Path |
|---------|------|
| DI container setup | `CAP.Avalonia/App.axaml.cs` |
| Main ViewModel | `CAP.Avalonia/ViewModels/MainViewModel.cs` |
| Main Window layout | `CAP.Avalonia/Views/MainWindow.axaml` |
| Canvas ViewModel | `CAP.Avalonia/ViewModels/Canvas/DesignCanvasViewModel.cs` |
| Canvas control | `CAP.Avalonia/Controls/DesignCanvas.cs` |
| Simulation service | `CAP.Avalonia/Services/SimulationService.cs` |
| Command interfaces | `CAP.Avalonia/Commands/ICommand.cs` |
| Command manager | `CAP.Avalonia/Commands/CommandManager.cs` |
| Example ViewModel | `CAP.Avalonia/ViewModels/Analysis/ParameterSweepViewModel.cs` |
| Test helpers | `UnitTests/Helpers/TestComponentFactory.cs` |
| Example unit tests | `UnitTests/Analysis/ParameterSweeperTests.cs` |
| Example integration tests | `UnitTests/Simulation/SimulationIntegrationTests.cs` |
| PDK data | `CAP-DataAccess/PDKs/demo-pdk.json` |

## Setup Requirements

To enable the automated agent workflow:

1. Add `ANTHROPIC_API_KEY` to repository secrets (Settings > Secrets > Actions)
2. Install Claude GitHub App: https://github.com/apps/claude
3. Create `agent-pr` label: `gh label create agent-pr --description "PR created by Claude agent" --color "7057ff"`
