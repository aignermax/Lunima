# Codebase Map - Connect-A-PIC-Pro

## Architecture Overview
- **Pattern**: MVVM (Model-View-ViewModel)
- **UI Framework**: Avalonia (C# / XAML)
- **DI Container**: Built-in Avalonia DI

## Key Directories

### Core Logic
- `Connect-A-Pic-Core/` - Business logic, no UI dependencies
  - `Components/` - Photonic components (Waveguides, Couplers, etc.)
  - `Simulation/` - S-Matrix simulation engine
  - `Analysis/` - Analysis tools (ParameterSweeper, etc.)

### UI Layer
- `CAP.Avalonia/ViewModels/` - MVVM ViewModels (CommunityToolkit.Mvvm)
  - `MainViewModel.cs` - Main window VM (central hub)
  - `ParameterSweepViewModel.cs` - Example feature VM
- `CAP.Avalonia/Views/` - AXAML Views
  - `MainWindow.axaml` - Main UI layout
  - Right panel = Properties/Tools area (add new features here)

### Testing
- `UnitTests/` - xUnit tests
  - `Analysis/` - Analysis feature tests
  - `Helpers/TestComponentFactory.cs` - Test utilities

## Common Patterns

### Adding a New Feature (Vertical Slice)
1. Core class in `Connect-A-Pic-Core/[Category]/FeatureName.cs`
2. ViewModel in `CAP.Avalonia/ViewModels/FeatureNameViewModel.cs`
3. View in `MainWindow.axaml` (right panel section)
4. Tests in `UnitTests/[Category]/FeatureNameTests.cs`

### Key Classes to Know
- `MainViewModel` - Entry point, holds all feature VMs
- `DesignCanvas` - Main design surface
- `ComponentGroup` - Container for multiple components
- `BoundingBoxCalculator` - Spatial calculations
- `ParameterSweeper` - Example of complete vertical slice

## File Naming Conventions
- ViewModels: `*ViewModel.cs`
- Tests: `*Tests.cs`
- Core classes: Descriptive names (no suffix)

## Build & Test
```bash
dotnet build
python3 tools/smart_test.py
```

## Key Files Quick Reference

### ViewModels
- `CAP.Avalonia/ViewModels/MainViewModel.cs` — main window, holds all feature VMs
- `CAP.Avalonia/ViewModels/ParameterSweepViewModel.cs` — example feature VM
- `CAP.Avalonia/ViewModels/DesignCanvasViewModel.cs` — canvas, component placement

### Core Classes
- `Connect-A-Pic-Core/Analysis/ParameterSweeper.cs` — parameter sweeping logic
- `Connect-A-Pic-Core/Layout/BoundingBoxCalculator.cs` — spatial calculations
- `Connect-A-Pic-Core/Components/ComponentGroup.cs` — component containers

### UI
- `CAP.Avalonia/Views/MainWindow.axaml` — main UI; right panel lines 193-229 = ParameterSweep example

### Tests
- `UnitTests/Analysis/ParameterSweeperTests.cs` — example test pattern
- `UnitTests/Helpers/TestComponentFactory.cs` — test utilities

## Search Patterns
- Analysis features: `**/Analysis/**/*`
- ViewModels: `**/*ViewModel.cs`
- Tests: `**/*Tests.cs`
- UI panels: `**/MainWindow.axaml`
