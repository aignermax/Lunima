# Lunima architecture

This guide describes the code inspected on October 23, 2026, at the start of the
architecture-hardening work. It is a navigation and responsibility map, not a
claim that the hardening targets have already been implemented. Follow
[CODEBASE_MAP.md](docs/CODEBASE_MAP.md) for entry points and the
[hardening design](docs/superpowers/specs/2026-10-23-architecture-hardening-design.md)
for the required end state and verification criteria.

## Projects and dependencies

| Project | Current responsibility |
|---|---|
| `CAP_Contracts` | Shared data-access and resource contracts |
| `Connect-A-Pic-Core` (`CAP_Core`) | Components, geometry, groups, routing, optical calculation, logic, analysis and export support |
| `CAP-DataAccess` (`CAP_DataAccess`) | JSON access, PDKs, persistence helpers and PIR data structures |
| `CAP.Avalonia` | Shared Avalonia UI, CommunityToolkit.Mvvm viewmodels, application workflows and platform integrations |
| `CAP.Desktop` | Desktop host and Windows/Linux/macOS packaging |
| `CAP.Browser` | Browser host project; browser delivery remains separate from desktop release support |
| `UnitTests` | xUnit 2 tests referencing production projects, including headless Avalonia tests |

Core references `CAP_Contracts` and must remain free of presentation dependencies.
Project names alone do not establish clean boundaries: Core currently includes
external-process/export support, and `CAP.Avalonia/Services` includes both use
cases and infrastructure. Current project serialization also has UI ownership.
The target direction is presentation → application/session → domain/contracts,
with infrastructure implementing narrow boundary contracts. Extracting those
responsibilities and enforcing semantic dependency rules remains hardening work.

## Composition and presentation

`CAP.Avalonia/App.axaml.cs::ConfigureServices` composes feature extensions from
`CAP.Avalonia/DI/` using `Microsoft.Extensions.DependencyInjection`. This is not
an Avalonia-provided DI container. `CoreServicesExtensions` registers simulation,
PDK loading, undo history, preferences and persistence services; other extensions
own export, AI, MCP, Python environments, canvas/panels and feature-specific wiring.
`MainViewModel` is the root presentation coordinator. Existing `App.Services`
access in UI code is implementation debt, not a pattern to extend into domain code.

Viewmodels use CommunityToolkit.Mvvm observable properties and commands. Views
bind presentation state, while `Controls/` contains canvas interaction/rendering.
The Layout, Optics, Logic and System workspaces share a design and choose different
tools, run actions, analysis surfaces and visual emphasis. Their presentation lives
in `ViewModels/Workspaces/`, with shell integration in `MainViewModel.Workspaces.cs`
and the related workspace partials. This describes existing files, not permission
to split new responsibilities into more partials.

The right sidebar contains **properties of the current selection only**. Put
whole-design analysis in the analysis dock and choose windows, dialogs, flyouts
or canvas overlays for other workflows according to [CLAUDE.md §5](CLAUDE.md).

## Design state and persistence

Components, physical pins and nested groups live under `Connect-A-Pic-Core/Components/`.
Canvas viewmodels project these into the UI. Dies/chiplets have presentation under
`ViewModels/Dies/`; fabrication and group process policies live under
`Components/Process/`. `Analysis/` includes chiplet interface checks, alignment
and optical coupling calculations. Keep the document root, active group and
per-die process context distinct when editing nested designs.

The `.lun` format carries design data and is the basis for the evolving Photonic
Intermediate Representation (PIR). `CAP-DataAccess/Persistence/PIR/` contains
metadata, S-matrix, simulation result and external-reference data. Current project
load/save orchestration is still coupled to presentation. The native document DTOs
now live in `CAP-DataAccess/Persistence/NativeDesign/`. The unused alternate UI
workflow (`ProjectPersistenceViewModel`/`ProjectPersistenceService`) was retired.
`GridPersistenceWithGroupsManager` remains a separately tested low-level grid
serializer; it is not the application's native-document workflow. Extracting the
native load/save use cases from presentation is still tracked in the hardening
design. Preserve serialized names, IDs, geometry, models and process data.

`CAP.Avalonia/Commands/CommandManager` owns undo/redo history. Command effects,
worker lifetimes and document replacement need coordinated ownership; the
session/revision/lease model in the hardening design is the target, not a description
of a completed session layer.

## Geometry, routing and simulation

Physical positions and dimensions use micrometres. Physical pins hold local offsets
and orientations; routing must account for component translation and rotation.
`Connect-A-Pic-Core/Routing/` provides route calculation and geometry.
`LightCalculation/` contains optical propagation and time-domain calculations;
`Analysis/` contains sweeps and other analysis; `Logic/` contains photonic logic.
`CAP.Avalonia/Services/SimulationService.cs` coordinates presentation-facing
simulation. Isolating worker input from mutable live state is a hardening requirement.

PDK JSON files are bundled under `CAP-DataAccess/PDKs/`; the schema is documented in
[PDK_JSON_FORMAT.md](docs/PDK_JSON_FORMAT.md). Process policies constrain compatible
components and fabrication settings. Python-backed previews and exports support
gdsfactory and Nazca, alongside circuit/netlist exporters. Export coordinates
must preserve the editor's Y-down to fabrication Y-up conversion, component
rotations and PDK origin offsets. Consult the coordinate regression tools listed
in CLAUDE.md before changing those transformations.

`SimpleNazcaExporter` retains export orchestration and its public facade. Focused
writers in `Services/NazcaExport/` own component stubs/placements, routed and frozen
paths, crossing geometry, assembly cells and the Python script envelope. Complete
script regression fixtures protect emission order and coordinate conventions.

External tools use `CAP_Core.Export.ProcessLaunchFactory`; opening URLs and files
uses `IUrlLauncher`. Solver cancellation, Python environment identity, preview
cache invalidation and update provenance are separate hardening concerns. Do not
infer their correctness merely from the presence of a shared process launcher.

## Assistant and MCP bridge

The assistant already has a tool registry. `AiAssistantFeatureExtensions` registers
`IAiTool` implementations and `IAiToolRegistry`, which `AiAssistantViewModel` uses.
`Services/AgentBridge/Tools/AgentToolCatalog.cs` combines bridge-specific tools with
adapters for assistant tools; bridge tools take precedence for matching names.
Tool calls are dispatched to the UI thread. The loopback bridge is opt-in and
its trust boundary is documented in [AGENT_BRIDGE.md](docs/AGENT_BRIDGE.md).
Tracking accepted operations through stop/cancellation remains an explicit
hardening requirement.

## Build, tests and contribution rules

`global.json` defines SDK selection. Production projects target .NET 10; the
current test project uses xUnit 2.4.2 through VSTest, not the xUnit v3 executable
runner. [TESTING.md](docs/TESTING.md) documents the checked-in test entry point,
result logs, selective runs and CI shards. Run local .NET tests serially with
Slow and Heavy categories excluded by default:

```powershell
# Windows
py scripts/run_tests.py
py scripts/run_tests.py ParameterSweeperTests
```

```bash
# Linux/macOS
python3 scripts/run_tests.py
python3 scripts/run_tests.py ParameterSweeperTests
```

Prefer new files no longer than 250 lines; authored source and documentation have a hard ceiling of
999 physical lines. Extract real responsibilities instead of adding partial files,
minifying code or dropping assertions. The source-size checker and hook setup are
documented in [CONTRIBUTING.md](CONTRIBUTING.md). Data/fixture/generated-artifact
scope remains an open hardening decision and is not a blanket source exemption.

Feature branches target `dev`; releases promote reviewed `dev` changes to `main`
as described in [RELEASE.md](docs/RELEASE.md). Use [ROADMAP.md](docs/ROADMAP.md) for
product plans and [CHANGELOG.md](CHANGELOG.md) for shipped changes.
