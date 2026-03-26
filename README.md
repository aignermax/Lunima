# Connect-A-PIC Pro

<img width="1407" height="932" alt="image" src="https://github.com/user-attachments/assets/a784b7f3-7300-453f-a6a8-12638367ef9a" />

<img width="1534" height="475" alt="image" src="https://github.com/user-attachments/assets/80dcf0e1-1cbf-4709-ab79-5292cf3e415e" />

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
│   └── Services/         # SimulationService, NazcaExporter, FileDialogService
├── CAP.Desktop/          # Desktop application entry point
├── CAP.Browser/          # WebAssembly browser entry point (planned)
└── UnitTests/            # 544 xUnit tests (539 passing)
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
| `FileDialogService` | Property | Cross-platform file dialogs |

The container is accessible via `App.Services` for view code-behind when needed.

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
- (Optional) **OpenViking** for AI agent context management — see [docs/OPENVIKING_QUICKSTART.md](docs/OPENVIKING_QUICKSTART.md)

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

## OpenViking Setup (Optional - For Contributors)

**Why OpenViking?** When working with Claude Code or other AI agents, reading 152 files (76,000+ lines) every time is slow and expensive. OpenViking creates a semantic index that reduces context by 93%, making AI assistance faster and more effective.

### Quick Setup - One Command! (Linux)

```bash
bash scripts/setup-openviking-linux.sh YOUR_OPENAI_API_KEY
```

That's it! The script:
- ✅ Installs OpenViking
- ✅ Creates config with your API key
- ✅ Indexes the entire codebase (~30 seconds)
- ✅ Shows you how to start the server

**Cost:** ~€0.003 for initial indexing (less than 1 cent), ~€0.0001 per git pull update.

### Start the Server

```bash
~/.local/bin/openviking-server
```

Or run in background:
```bash
nohup ~/.local/bin/openviking-server > ~/.openviking/server.log 2>&1 &
```

### Documentation

- **Home Setup (Linux):** [docs/OPENVIKING_HOME_SETUP.md](docs/OPENVIKING_HOME_SETUP.md) ← **Start here!**
- **Detailed Guide:** [docs/OPENVIKING_QUICKSTART.md](docs/OPENVIKING_QUICKSTART.md)
- **MCP Integration:** [mcp-servers/openviking/README.md](mcp-servers/openviking/README.md)

**Important:** OpenViking uses OpenAI only for embeddings (creating search vectors), NOT as the LLM. Claude Sonnet remains your AI assistant. This is 100% optional for contributors but highly recommended when working with AI agents.

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

### Done (50+ features implemented)
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
- [x] **SiEPIC EBeam PDK** - 12 real components with measured S-parameters, auto-loaded at startup
- [x] **Multi-Wavelength S-Matrices** - Per-wavelength S-matrix data with nearest-wavelength fallback
- [x] **Component Library Search** - Fulltext search by name, category, PDK source, or Nazca function
- [x] **Component Preview** - Miniature schematics in component panel showing pin layout
- [x] **Nazca GDS Export Fixes** - Chained waveguide segments (no gaps), component rotation, Y-axis transform, demofab 1:1 matching
- [x] **Nazca Integration Tests** - Python syntax validation, GDS generation, property-based tests
- [x] **Multi-Select** - Box select + Shift+click for multiple components
- [x] **Copy/Paste** - Duplicate components with Ctrl+C/V or context menu
- [x] **Grating Coupler Component** - Vertical fiber coupling with Nazca GDS export
- [x] **PDK Management Panel** - Toggle PDKs on/off with component count display
- [x] **Pin Alignment Guides** - Figma-style visual guides during component placement
- [x] **Routing Diagnostics Panel** - Real-time path validation with JSON export
- [x] **Component Dimension Validation** - Verify component bounds match pin positions
- [x] **Parameter Sweep** - Systematic analysis of component parameter variations
- [x] **Element Locking** - Lock components/connections to prevent accidental modification
- [x] **Incremental Routing** - Preserve valid routes when circuit changes

### High Priority
- [ ] **Connection Validation** - Warn about pin angle mismatches, unconnected pins

### Path to Professional Use: Real PDK / CML Integration

The goal is to make Connect-A-PIC Pro usable with real foundry component data so simulation results are physically meaningful.

**Background:** Professional photonic design uses Compact Model Libraries (CMLs) — parameterized S-matrices calibrated to real fabrication data. Foundries ship CMLs with their PDKs. Major vendors (Ansys Lumerical, Synopsys) use proprietary encrypted formats. However, open PDKs exist and are widely used in research/education.

**Step 1: Import SiEPIC Open PDK** ✅
- [x] **Parse SiEPIC S-parameter files** - 12 SiEPIC EBeam PDK components with real Lumerical-simulated S-parameters, bundled as `siepic-ebeam-pdk.json`
- [x] **Wavelength-dependent S-matrices in core** - Multi-wavelength support with nearest-wavelength fallback in `SystemMatrixBuilder`
- [ ] **Expand SiEPIC PDK** - Add remaining 31 components (43 total in full PDK) 🚧 IN PROGRESS
- [ ] **SiEPIC SiN PDK** - Silicon nitride platform, also open. Second priority after SOI.

**Step 2: Define an open component model format** ✅
- [x] **JSON compact model format** - PDK JSON schema with physical pins, multi-wavelength S-matrices, Nazca function names. Auto-loaded at startup from `PDKs/` directory.
- [ ] **Parameterized models** - Components where S-matrix varies with user-adjustable parameters (e.g., coupler gap, ring radius, waveguide width). Could use interpolation between pre-computed S-matrices or analytical formulas.

**Step 3: Professional features**
- [x] **Nazca GDS Export** - Chained waveguide segments, component rotation, Y-axis coordinate transform, real PDK function names
- [x] **Component Library Search** - Fulltext search across name, category, PDK source
- [x] **Component Preview** - Miniature schematic with pin layout in the component panel
- [x] **Demofab 1:1 Matching** - Built-in components match Nazca demofab dimensions exactly with correct origin offsets
- [ ] **Wavelength sweep / spectral response** - Run simulation across a wavelength range, plot transmission vs wavelength at output ports
- [ ] **Design Rule Checking** - Min bend radius, spacing violations, pin angle mismatches
- [ ] **Direct GDS Export** - Export layout polygons without Nazca intermediate step
- [ ] **Hierarchical Designs** - Sub-circuits as reusable blocks

### Nice to Have
- [x] **Component Preview Graphics** - Miniature schematics with pin positions in the component library
- [ ] **Browser Version** - WebAssembly deployment
- [ ] **Component Properties Panel** - Edit S-matrix parameters per component instance

### Future Vision: Optical Computing
- [ ] **Nonlinear components** - S-matrix depends on input power (optical transistor / switch)
- [ ] **Delay lines** - Waveguide loops with defined propagation time (optical memory/register)
- [ ] **Pulsed laser source** - Time-domain clock signal for synchronous optical logic
- [ ] **Time-domain simulation** - Step-based solver (vs current steady-state) for signal propagation
- [ ] **Multi-chip interconnect** - Inter-chip optical cables between separate chip canvases
- [ ] **Python PDK Extractor** - Tool to convert Nazca Python PDKs to JSON format

## AI Code Assistant Integration

This codebase uses [NetContextServer](https://github.com/willibrandon/NetContextServer) for deep .NET semantic understanding:
- Roslyn-based C# code analysis (classes, methods, types, references)
- NuGet dependency analysis
- Test coverage parsing (Coverlet, LCOV, Cobertura)
- Model Context Protocol (MCP) integration with Claude Code

See [docs/NETCONTEXTSERVER_SETUP.md](docs/NETCONTEXTSERVER_SETUP.md) for setup instructions.

## Original Project

Based on [Connect-A-PIC](https://github.com/Akhetonics/Connect-A-PIC) by Akhetonics.
