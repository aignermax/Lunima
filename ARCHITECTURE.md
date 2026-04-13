# Architecture

This document describes the technical architecture of Lunima.

---

## Project Structure

```
Lunima/
├── CAP_Contracts/        # Shared interfaces
├── Connect-A-Pic-Core/   # Core simulation engine
│   ├── Components/       # Component models, pins, S-matrices, parametric
│   ├── Routing/          # Waveguide routing (A*, Manhattan, CSC)
│   ├── LightCalculation/ # S-Matrix propagation, power flow analysis
│   ├── Analysis/         # Parameter sweep, sweep configuration
│   └── Grid/             # Component placement, connection management
├── CAP-DataAccess/       # JSON persistence, PDK loading
│   └── PDKs/             # Bundled PDK JSON files (demo-pdk.json, siepic-ebeam-pdk.json)
├── CAP.Avalonia/         # Shared cross-platform UI
│   ├── ViewModels/       # MVVM ViewModels (30+ view models)
│   ├── Views/            # AXAML views and controls
│   ├── Commands/         # Undo/redo commands (IUndoableCommand, CommandManager)
│   └── Services/         # SimulationService, NazcaExporter, FileDialogService, AiService
├── CAP.Desktop/          # Desktop application entry point
├── CAP.Browser/          # WebAssembly browser entry point (planned)
└── UnitTests/            # 1600+ xUnit tests
```

---

## Dependency Injection

Services are registered in `App.axaml.cs` using `Microsoft.Extensions.DependencyInjection`:

| Service | Lifetime | Purpose |
|---------|----------|---------|
| `SimulationService` | Singleton | S-Matrix light simulation orchestrator |
| `SimpleNazcaExporter` | Singleton | Nazca Python export |
| `PdkLoader` | Singleton | PDK JSON file loading |
| `CommandManager` | Singleton | Undo/redo command history |
| `AiService` | Singleton | Claude API integration for AI assistant |
| `MainViewModel` | Singleton | Root ViewModel |
| `FileDialogService` | Property | Cross-platform file dialogs |

The container is accessible via `App.Services` for view code-behind when needed.

---

## Physical Coordinate System

Components have physical dimensions and positions:

```csharp
component.WidthMicrometers = 250.0;   // Physical width in µm
component.HeightMicrometers = 250.0;  // Physical height in µm
component.PhysicalX = 1000.0;         // X position in µm
component.PhysicalY = 500.0;          // Y position in µm
component.RotationDegrees = 45.0;     // Rotation angle
```

Physical pins define optical ports with µm offsets:

```csharp
var pin = new PhysicalPin
{
    Name = "output",
    OffsetXMicrometers = 250.0,  // Position relative to component origin
    OffsetYMicrometers = 125.0,
    AngleDegrees = 0.0           // Pin direction (0 = pointing right)
};
```

---

## Waveguide Routing

Connections are automatically routed between physical pins:

```csharp
var connection = new WaveguideConnection
{
    StartPin = componentA.PhysicalPins[0],
    EndPin = componentB.PhysicalPins[1],
    PropagationLossDbPerCm = 2.0,    // Loss parameter
    BendLossDbPer90Deg = 0.05,       // Bend loss parameter
    BendRadiusMicrometers = 10.0     // Minimum bend radius
};

// Calculate actual routed path and transmission coefficient
connection.RecalculateTransmission();

// Access results
double pathLength = connection.PathLengthMicrometers;
double bendCount = connection.BendCount;
double totalLoss = connection.TotalLossDb;
Complex transmission = connection.TransmissionCoefficient;
```

### Routing Strategies

The router supports three strategies:

1. **Straight** — Direct line when pins are aligned
2. **S-Bend** — Two opposing bends for parallel offset pins
3. **Manhattan** — Rounded corners for perpendicular pin orientations

---

## S-Matrix Simulation

The core S-Matrix light propagation simulation is physically grounded and compatible with standard photonic compact model formats:

- Components define wavelength-specific S-matrices
- Light propagates through the system based on matrix multiplication
- Waveguide connections contribute transmission coefficients to the system matrix
- Supports non-linear formulas with slider parameters

### Multi-Wavelength Support

S-matrices are stored per wavelength with automatic nearest-wavelength fallback:

```csharp
// Define S-matrix for specific wavelength
component.AddSMatrix(wavelength: 1550.0, sMatrix: matrix);

// Simulation uses nearest available wavelength if exact match not found
var result = simulation.CalculateAtWavelength(1549.5);
```

---

## Nazca Export

Export designs to Nazca Python for fabrication:

```csharp
string nazcaCode = exporter.Export(canvas);
// Generates Python code like:
// cell_0 = ebeam_gc_te1550().put(100.0, 200.0, 0.0)
// cell_1 = ebeam_y_1550().put(500.0, 200.0, 0.0)
// nd.strt(length=350.5).put(100.0, -200.0, 0.0)
```

### Coordinate Transformations

Lunima uses a Y-down viewport coordinate system, while Nazca uses Y-up. The exporter handles:

- Y-axis flip: `nazcaY = -editorY`
- Component rotation
- Pin angle calculations
- NazcaOriginOffset for PDK components

---

## PDK Integration

PDKs are defined as JSON files in `CAP-DataAccess/PDKs/`:

```json
{
  "pdk_name": "SiEPIC EBeam PDK",
  "description": "Silicon photonics 220nm SOI platform",
  "components": [
    {
      "name": "Grating Coupler TE1550",
      "nazca_function": "ebeam_gc_te1550",
      "width_um": 25.0,
      "height_um": 30.0,
      "pins": [
        {
          "name": "opt1",
          "offset_x": 12.5,
          "offset_y": 0.0,
          "angle_degrees": 0.0
        }
      ],
      "s_matrices": {
        "1550": [[0, 0.5], [0.5, 0]]
      }
    }
  ]
}
```

PDKs are automatically loaded at startup from the `PDKs/` directory.

---

## MVVM Pattern

Lunima uses the MVVM pattern with `CommunityToolkit.Mvvm`:

```csharp
public partial class MyViewModel : ObservableObject
{
    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private bool _isProcessing;

    [RelayCommand]
    private async Task RunSimulation()
    {
        IsProcessing = true;
        StatusText = "Running...";

        await _simulationService.RunAsync();

        StatusText = "Complete";
        IsProcessing = false;
    }
}
```

All ViewModels are registered in the DI container in `App.axaml.cs`.

---

## Command Pattern (Undo/Redo)

All user actions that modify the design implement `IUndoableCommand`:

```csharp
public class AddComponentCommand : IUndoableCommand
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly Component _component;
    private ComponentViewModel? _addedVm;

    public void Execute()
    {
        _addedVm = _canvas.AddComponent(_component);
    }

    public void Undo()
    {
        if (_addedVm != null)
            _canvas.RemoveComponent(_addedVm);
    }
}
```

Commands are managed by `CommandManager` which maintains undo/redo stacks.

---

## Testing

The project uses xUnit for testing with 1600+ tests:

```bash
# Run all tests
dotnet test

# Run specific test file
dotnet test --filter FullyQualifiedName~RoutingTests

# Use smart_test.py for compact output (recommended)
python3 tools/smart_test.py
python3 tools/smart_test.py RoutingTests
```

Test categories:
- **Unit tests** — Core logic (routing, S-matrix, coordinate transforms)
- **Integration tests** — GDS export, Nazca code generation
- **UI tests** — ViewModel behavior, command execution

---

## AI Assistant Architecture

The AI assistant uses Claude's tool-calling API to interact with the design canvas:

1. **User message** → AiAssistantViewModel
2. **Tool definitions** → Sent to Claude API
3. **Claude responds** with tool calls (e.g., `place_component`, `connect_pins`)
4. **Tools execute** via AiGridService
5. **Results returned** to Claude
6. **Claude summarizes** what was done

Tools are currently hardcoded in `AiGridService.cs` and `AiAssistantViewModel.cs`. See [Issue #468](https://github.com/aignermax/Lunima/issues/468) for planned plugin/registry pattern refactoring.

---

## Build System

The project uses standard .NET tooling with optional Make/bash wrappers:

```bash
# Build all projects
dotnet build

# Run desktop app
dotnet run --project CAP.Desktop/CAP.Desktop.csproj

# Run tests
dotnet test

# Quick start (Make)
make run
make build
make test
```

See [Makefile](Makefile) and [run.sh](run.sh) for details.

---

## Code Style

See [CLAUDE.md](CLAUDE.md) for detailed code style guidelines used by AI agents:

- C# naming conventions (PascalCase public, _camelCase private)
- MVVM with CommunityToolkit.Mvvm
- Maximum 250 lines per new file
- XML documentation for all public APIs
- No magic numbers (use named constants)

---

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) *(coming soon)* for contribution guidelines.
