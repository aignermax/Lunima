# Connect-A-PIC Pro

> **An architecture-level photonic design tool for fast concept validation and system exploration.**

Professional fork of [Connect-A-PIC](https://github.com/Akhetonics/Connect-A-PIC) - optimized for **thinking**, not tape-out. See [PHILOSOPHY.md](PHILOSOPHY.md) for positioning.

## Key Differences from Connect-A-PIC

Connect-A-PIC Pro extends the educational version with professional features:

- **Physical Coordinate System**: Components positioned in micrometers (µm) instead of fixed grid tiles
- **Explicit Waveguide Routing**: Automatic routing with S-bends, straight segments, and manhattan routing
- **Dynamic Loss Calculation**: Transmission coefficients calculated from actual path geometry (propagation loss + bend loss)
- **Cross-Platform UI**: Avalonia-based frontend supporting Desktop and WebAssembly (browser)
- **Decoupled from Godot**: No game engine dependency - pure .NET solution

## Architecture

```
Connect-A-PIC-Pro/
├── CAP_Contracts/        # Shared interfaces
├── Connect-A-Pic-Core/   # Core simulation engine
│   ├── Components/       # Component models, pins, S-Matrix
│   ├── Routing/          # Waveguide routing (WaveguideRouter, PathSegments)
│   └── LightCalculation/ # S-Matrix light propagation
├── CAP-DataAccess/       # JSON loading, component draft conversion
├── CAP.Avalonia/         # Shared Avalonia UI (views, viewmodels, controls)
├── CAP.Desktop/          # Desktop application entry point
├── CAP.Browser/          # WebAssembly browser entry point (planned)
└── UnitTests/            # xUnit tests
```

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

The router supports three strategies:
1. **Straight**: Direct line when pins are aligned
2. **S-Bend**: Two opposing bends for parallel offset pins
3. **Manhattan**: Rounded corners for perpendicular pin orientations

## Building

### Prerequisites
- .NET 8.0 SDK
- (Optional) Nazca Python for GDS export

### Build Commands

```bash
# Quick start (recommended)
make run
# or
./run.sh

# Build all projects
dotnet build
# or
make build

# Run desktop app (explicit)
dotnet run --project CAP.Desktop/CAP.Desktop.csproj

# Run tests
dotnet test UnitTests/UnitTests.csproj
# or
make test
```

## S-Matrix Simulation

The core S-Matrix light propagation simulation remains compatible with the original Connect-A-PIC:

- Components define wavelength-specific S-matrices
- Light propagates through the system based on matrix multiplication
- Waveguide connections contribute transmission coefficients to the system matrix
- Supports non-linear formulas with slider parameters

## Nazca Export

Export designs to Nazca Python for fabrication:

```csharp
string nazcaCode = connection.ExportToNazca();
// Generates: ic.cobra_p2p(pin1=cell_0_0.pin['output'], pin2=cell_1_0.pin['input']).put()
```

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Backlog / Roadmap

### Done
- [x] Physical coordinate system (µm positioning)
- [x] Avalonia UI with component placement, connections, rotation
- [x] Undo/Redo system (Command pattern)
- [x] Save/Load designs (.cappro JSON format)
- [x] Nazca Python export
- [x] Delete components and connections
- [x] Keyboard shortcuts (S/C/D/R, Ctrl+Z/Y, Ctrl+S)

### High Priority
- [x] **PDK JSON Format** - Define JSON schema for PDK component libraries with physical pins
- [x] **PDK Loader** - Load PDK JSON files and add components to UI library
- [ ] **Auto-Routing** - Manhattan routing with bend segments around obstacles
- [ ] **Grid Snapping** - Optional snap-to-grid for cleaner layouts
- [ ] **Connection Validation** - Warn about pin angle mismatches, unconnected pins

### Medium Priority
- [ ] **Multi-Select** - Box select or Ctrl+click multiple components
- [ ] **Copy/Paste** - Duplicate components or selections
- [ ] **Component Properties Panel** - Edit parameters (coupling ratio, phase, etc.)
- [ ] **Zoom to Fit** - Auto-fit design in viewport
- [ ] **Light Simulation Visualization** - Show power levels at pins/connections

### Nice to Have
- [ ] **Hierarchical Designs** - Sub-circuits as reusable blocks
- [ ] **Direct GDS Export** - Without Nazca intermediate step
- [ ] **Wavelength Sweep** - Plot response vs wavelength
- [ ] **Design Rule Checking** - Min bend radius, spacing violations
- [ ] **Better Component Graphics** - Icons/symbols instead of rectangles
- [ ] **Browser Version** - WebAssembly deployment

### Future / Separate Projects
- [ ] **Python PDK Extractor** - Tool to convert Nazca Python PDKs to JSON format
- [ ] **Embedded Python** - Run exported Nazca code directly in app

## Original Project

Based on [Connect-A-PIC](https://github.com/Akhetonics/Connect-A-PIC) by Akhetonics.
