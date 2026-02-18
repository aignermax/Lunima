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

### Dependency Injection

Services are registered in `App.axaml.cs` using `Microsoft.Extensions.DependencyInjection`:

| Service | Lifetime | Purpose |
|---------|----------|---------|
| `SimulationService` | Singleton | S-Matrix light simulation orchestrator |
| `SimpleNazcaExporter` | Singleton | Nazca Python export |
| `PdkLoader` | Singleton | PDK JSON file loading |
| `CommandManager` | Singleton | Undo/redo command history |
| `MainViewModel` | Singleton | Root ViewModel |

`FileDialogService` is set via property injection after window creation (requires `Window` reference). The container is accessible via `App.Services` for view code-behind when needed.

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
- [x] Save As support
- [x] Nazca Python export
- [x] Delete components and connections
- [x] Keyboard shortcuts (S/C/D/R/G/F/L/P, Ctrl+Z/Y, Ctrl+S) — global, work regardless of focus
- [x] **PDK JSON Format** - Define JSON schema for PDK component libraries with physical pins
- [x] **PDK Loader** - Load PDK JSON files and add components to UI library
- [x] **Grid Snapping** - Optional snap-to-grid with configurable grid size
- [x] **Zoom to Fit** - Auto-fit design in viewport (F key)
- [x] **Light Simulation Visualization** - Power overlay on connections with color-coded power levels
- [x] **Auto-Recalculation** - Simulation auto-updates when circuit changes while overlay is active
- [x] **Per-Source Laser Config** - Wavelength and power settings per light source (UI + multi-wavelength simulation)
- [x] **Dependency Injection** - `Microsoft.Extensions.DependencyInjection` with constructor injection for all services

### High Priority
- [ ] **Connection Validation** - Warn about pin angle mismatches, unconnected pins
- [ ] **Multi-Select** - Box select or Ctrl+click multiple components
- [ ] **Copy/Paste** - Duplicate components or selections

### Path to Professional Use: Real PDK / CML Integration

The goal is to make Connect-A-PIC Pro usable with real foundry component data so simulation results are physically meaningful.

**Background:** Professional photonic design uses Compact Model Libraries (CMLs) — parameterized S-matrices calibrated to real fabrication data. Foundries ship CMLs with their PDKs. Major vendors (Ansys Lumerical, Synopsys) use proprietary encrypted formats. However, open PDKs exist and are widely used in research/education.

**Step 1: Import SiEPIC Open PDK** (highest impact)
- [ ] **Parse SiEPIC S-parameter files** - SiEPIC EBeam PDK (UBC, GitHub, SOI 220nm silicon — the most common photonic platform) ships S-parameter data (.dat files) with wavelength-dependent S-matrices for standard components (directional couplers, ring resonators, grating couplers, Y-branches, etc.)
- [ ] **Wavelength-dependent S-matrices in core** - Currently S-matrices are fixed per component. Need to extend `Component` to hold S-matrix lookup tables indexed by wavelength (nm → Complex S-matrix). The per-source laser config UI already supports wavelength selection; this makes it functional.
- [ ] **SiEPIC SiN PDK** - Silicon nitride platform, also open. Second priority after SOI.

**Step 2: Define an open component model format**
- [ ] **JSON compact model format** - Define a simple JSON/YAML schema: port definitions + S-matrix data at wavelength points + component parameters (geometry, coupling ratios, etc.). This becomes our CML equivalent — readable, editable, version-controllable.
- [ ] **Parameterized models** - Components where S-matrix varies with user-adjustable parameters (e.g., coupler gap, ring radius, waveguide width). Could use interpolation between pre-computed S-matrices or analytical formulas.

**Step 3: Professional features**
- [ ] **Wavelength sweep / spectral response** - Run simulation across a wavelength range, plot transmission vs wavelength at output ports. This is the #1 analysis tool in photonic circuit design.
- [ ] **Design Rule Checking** - Min bend radius, spacing violations, pin angle mismatches
- [ ] **Direct GDS Export** - Export layout polygons without Nazca intermediate step
- [ ] **Hierarchical Designs** - Sub-circuits as reusable blocks

### Nice to Have
- [ ] **Better Component Graphics** - Icons/symbols instead of rectangles
- [ ] **Browser Version** - WebAssembly deployment
- [ ] **Component Properties Panel** - Edit S-matrix parameters per component instance

### Future Vision: Optical Computing
- [ ] **Nonlinear components** - S-matrix depends on input power (optical transistor / switch)
- [ ] **Delay lines** - Waveguide loops with defined propagation time (optical memory/register)
- [ ] **Pulsed laser source** - Time-domain clock signal for synchronous optical logic
- [ ] **Time-domain simulation** - Step-based solver (vs current steady-state) for signal propagation
- [ ] **Multi-chip interconnect** - Inter-chip optical cables between separate chip canvases
- [ ] **Python PDK Extractor** - Tool to convert Nazca Python PDKs to JSON format

## Original Project

Based on [Connect-A-PIC](https://github.com/Akhetonics/Connect-A-PIC) by Akhetonics.
