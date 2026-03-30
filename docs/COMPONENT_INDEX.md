# Component Index - Quick Reference

## ViewModels (CAP.Avalonia/ViewModels/)
- `MainViewModel.cs` - [Main window, holds all feature VMs]
- `ParameterSweepViewModel.cs` - [Analysis, sweep parameters, example of complete feature]
- `DesignCanvasViewModel.cs` - [Canvas, component placement, visual editor]

## Core Classes (Connect-A-Pic-Core/)
- `ParameterSweeper.cs` - [Analysis/ParameterSweeper.cs, parameter sweeping logic]
- `BoundingBoxCalculator.cs` - [Layout/BoundingBoxCalculator.cs, spatial calculations]
- `ComponentGroup.cs` - [Components/ComponentGroup.cs, component containers]

## UI (CAP.Avalonia/Views/)
- `MainWindow.axaml` - [Main UI, right panel for new features, lines 193-229 = ParameterSweep example]

## Tests (UnitTests/)
- `ParameterSweeperTests.cs` - [Analysis/ParameterSweeperTests.cs, example test pattern]
- `TestComponentFactory.cs` - [Helpers/TestComponentFactory.cs, test utilities]

## Search Patterns
- Analysis features: `**/Analysis/**/*`
- ViewModels: `**/*ViewModel.cs`
- Tests: `**/*Tests.cs`
- UI panels: `**/MainWindow.axaml`
