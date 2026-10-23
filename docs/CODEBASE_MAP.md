# Lunima codebase map

Use this as a navigation index. [ARCHITECTURE.md](../ARCHITECTURE.md) describes the
current boundaries and outstanding hardening work; [CLAUDE.md](../CLAUDE.md)
defines contributor rules. The UI uses Avalonia and CommunityToolkit.Mvvm;
dependency injection uses Microsoft.Extensions.DependencyInjection.

## Projects and feature areas

| Location | Responsibility |
|---|---|
| `CAP_Contracts/` | Shared data-access contracts |
| `Connect-A-Pic-Core/Components/` | Components, pins, groups, processes and models |
| `Connect-A-Pic-Core/LightCalculation/` | Optical simulation and time-domain calculations |
| `Connect-A-Pic-Core/Analysis/` | Sweeps, checks and chiplet coupling/alignment |
| `Connect-A-Pic-Core/Routing/` | Waveguide pathfinding and route geometry |
| `Connect-A-Pic-Core/Logic/` | Photonic logic and execution models |
| `Connect-A-Pic-Core/Export/` | Export support and external process launching |
| `CAP-DataAccess/Persistence/` | Persistence helpers, group DTOs and PIR data |
| `CAP-DataAccess/Persistence/NativeDesign/` | UI-independent `.lun` document contract, process/die/import data |
| `CAP-DataAccess/PDKs/` | Bundled process design kits |
| `CAP.Avalonia/DI/` | Feature service registration |
| `CAP.Avalonia/ViewModels/` | Presentation state, grouped by feature |
| `CAP.Avalonia/Services/` | Application workflows and platform integrations; boundaries are being hardened |
| `CAP.Avalonia/Controls/` | Canvas interaction and rendering |
| `CAP.Avalonia/Views/` | AXAML windows, dialogs and panels |
| `CAP.Desktop/` | Desktop entry point and packaging |
| `CAP.Browser/` | Browser host project; not a released desktop target |
| `UnitTests/` | xUnit 2 tests, including integration and headless Avalonia coverage |

## Entry points

| Purpose | Current path |
|---|---|
| Composition root | `CAP.Avalonia/App.axaml.cs` |
| Main presentation coordinator | `CAP.Avalonia/ViewModels/MainViewModel.cs` |
| Main window | `CAP.Avalonia/Views/MainWindow.axaml` |
| Canvas viewmodel | `CAP.Avalonia/ViewModels/Canvas/DesignCanvasViewModel.cs` |
| Canvas bounds | `CAP.Avalonia/ViewModels/Canvas/BoundingBoxCalculator.cs` |
| Parameter sweep viewmodel | `CAP.Avalonia/ViewModels/Analysis/ParameterSweepViewModel.cs` |
| Parameter sweep calculation | `Connect-A-Pic-Core/Analysis/ParameterSweeper.cs` |
| Component groups | `Connect-A-Pic-Core/Components/Core/ComponentGroup.cs` |
| Nazca export orchestration | `CAP.Avalonia/Services/SimpleNazcaExporter.cs` |
| Nazca script writers | `CAP.Avalonia/Services/NazcaExport/` (components, connections, dies, geometry, script envelope) |
| Workspaces | `CAP.Avalonia/ViewModels/Workspaces/` |
| Dies and chiplets | `CAP.Avalonia/ViewModels/Dies/` |
| Process policies | `Connect-A-Pic-Core/Components/Process/` |
| Assistant tool registration | `CAP.Avalonia/DI/AiAssistantFeatureExtensions.cs` |
| MCP tool catalog | `CAP.Avalonia/Services/AgentBridge/Tools/AgentToolCatalog.cs` |
| Example analysis tests | `UnitTests/Analysis/ParameterSweeperTests.cs` |
| Test factories | `UnitTests/Helpers/TestComponentFactory.cs` |

## Adding a feature

Keep domain calculations in Core and presentation in the matching feature's
viewmodel/view folders. Register dependencies in the relevant `DI/` extension.
Choose the UI surface using CLAUDE.md §5: whole-design analyses belong in the
analysis dock; focused workflows use windows, dialogs or flyouts. **The right
sidebar is only for properties of the current selection.**

Prefer new files of 250 lines or fewer. The hard ceiling is 999 physical lines
per authored source file; separate responsibilities into collaborators rather
than adding partial-class fragments to meet the count.

## Build and verification

Use the SDK policy in `global.json` and the commands in [TESTING.md](TESTING.md).
The checked-in local test entry point is `py scripts/run_tests.py` on Windows
or `python3 scripts/run_tests.py` on Linux/macOS.
