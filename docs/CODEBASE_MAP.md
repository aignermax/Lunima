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
| Document identity, revision and operations | `Connect-A-Pic-Core/Documents/` |
| Immutable main/spectral simulation inputs | `Connect-A-Pic-Core/LightCalculation/Snapshots/` |
| Truth-table and logic network input capture | `Connect-A-Pic-Core/Analysis/LogicAnalysis/TruthTableSnapshot.cs`, `LogicNetworkSnapshot.cs` |
| Private routing input and results | `Connect-A-Pic-Core/Routing/Snapshots/` |
| Application and feature retirement | `CAP.Avalonia/Services/Lifetime/` |
| Native document workflow | `CAP.Avalonia/ViewModels/Panels/NativeDesign/` |
| Registry read capability | `Connect-A-Pic-Core/ComponentRegistry/RegistryClient/IRegistryCatalog.cs` |
| Registry adoption capability | `CAP.Avalonia/Services/ComponentRegistry/IRegistryAdoption.cs` |
| Bundled PDK inventory and write classification | `CAP-DataAccess/Components/ComponentDraftMapper/IBundledPdkCatalog.cs` |
| Measured spectrum loading | `Connect-A-Pic-Core/Analysis/MeasuredSpectrum/IMeasuredSpectrumSource.cs` |
| openEBL submission checking | `Connect-A-Pic-Core/Export/OpenEbl/` via `Export/Contracts/IOpenEblSubmissionChecker.cs` |
| GDS file reading and parsing | `CAP-DataAccess/Import/Gds/IGdsSourceReader.cs` |
| Redirected subprocess ownership | `Connect-A-Pic-Core/Export/Processes/OwnedProcess.cs` |
| Bridge CLI execution and Windows batch adaptation | `CAP.Avalonia/Services/AgentBridge/Registration/CliProcessRunner.cs`, `WindowsBatchCommand.cs` |
| Explicit shell startup | `CAP.Avalonia/ViewModels/Shell/ShellActivationCoordinator.cs` |
| Python/export capabilities | `Connect-A-Pic-Core/Export/Contracts/` |
| Example file inventory and localized projection | `CAP.Avalonia/Services/IExampleFileCatalog.cs`, `FileExampleCatalog.cs`, `ExampleDesignsService.cs` |
| S-parameter import detection | `CAP-DataAccess/Import/ISParameterImportCatalog.cs`, `SParameterImportCatalog.cs` |
| Configured preview disk cache | `CAP.Avalonia/Controls/Canvas/ComponentPreview/IGdsPreviewCache.cs` |
| Mandatory dependency policy | `Tools/Lunima.ArchitectureAnalyzers/` and `Directory.Build.targets` |
| Canvas bounds | `CAP.Avalonia/ViewModels/Canvas/BoundingBoxCalculator.cs` |
| Parameter sweep viewmodel | `CAP.Avalonia/ViewModels/Analysis/ParameterSweepViewModel.cs` |
| Parameter sweep calculation | `Connect-A-Pic-Core/Analysis/ParameterSweeper.cs` |
| Detached parameter trials | `Connect-A-Pic-Core/Analysis/ParameterSweepSnapshot.cs` |
| Detached layout compression and shared force solver | `Connect-A-Pic-Core/Analysis/LayoutCompressionSnapshot.cs`, `LayoutCompressionSolver.cs` |
| Detached circuit optimization and shared seeded search | `Connect-A-Pic-Core/Analysis/CircuitOptimization/CircuitOptimizationSnapshot.cs`, `OptimizationSearch.cs` |
| Captured transient circuit and excitation | `Connect-A-Pic-Core/LightCalculation/TimeDomainSimulation/TransientCircuitSnapshot.cs`, `TransientExcitationSnapshot.cs` |
| Captured fabrication variations and spectrum | `Connect-A-Pic-Core/Analysis/MonteCarloAnalysis/MonteCarloSpectrumSnapshot.cs`, `FabricationVariance/FabricationSnapshot.cs` |
| Analysis document lease and accepted routing cleanup | `CAP.Avalonia/ViewModels/Analysis/AnalysisSweepOperation.cs` |
| Persistent analysis rebinding after accepted New/Load | `CAP.Avalonia/ViewModels/Shell/PersistentAnalysisDocumentBinding.cs` |
| Truth-table extraction and publication ownership | `CAP.Avalonia/ViewModels/Analysis/LogicAnalysis/TruthTableWorkSession.cs` |
| Captured Monte Carlo eye metric and fabrication trials | `Connect-A-Pic-Core/Analysis/MonteCarloAnalysis/MonteCarloEyeSnapshot.cs`, `MonteCarloEyeMetric.cs` |
| Reentrant display notification sequencing | `CAP.Avalonia/ViewModels/ObservablePublicationQueue.cs` |
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
per authored source/documentation file; separate responsibilities into collaborators rather
than adding partial-class fragments to meet the count.

## Build and verification

Use the SDK policy in `global.json` and the commands in [TESTING.md](TESTING.md).
The checked-in local test entry point is `py scripts/run_tests.py` on Windows
or `python3 scripts/run_tests.py` on Linux/macOS.
