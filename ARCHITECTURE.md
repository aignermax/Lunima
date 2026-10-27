# Lunima architecture

This guide describes the architecture-hardening branch as of October 27, 2026.
Several ownership and dependency boundaries are implemented and regression-tested;
the remaining work is explicitly called out below. This is not a release-readiness
or whole-application equivalence claim. Follow
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
cases and infrastructure. Native document contracts now live outside presentation.
The target direction is presentation → application/session → domain/contracts,
with infrastructure implementing narrow boundary contracts. Feature contracts now
cover preferences, Python setup/registry, GDS execution, previews, native file I/O
and the component registry. The semantic analyzer has an explicit namespace/type
ownership inventory and runs as an error-producing analyzer in every shipping
project's normal build. It checks resolved symbols, including aliases, base types,
interfaces and implicit operations. Tests and the analyzer project are tooling.

## Composition and presentation

`CAP.Avalonia/App.axaml.cs::ConfigureServices` composes feature extensions from
`CAP.Avalonia/DI/` using `Microsoft.Extensions.DependencyInjection`. This is not
an Avalonia-provided DI container. `CoreServicesExtensions` registers simulation,
PDK loading, undo history, preferences and persistence services; other extensions
own export, AI, MCP, Python environments, canvas/panels and feature-specific wiring.
Examples, URL launching, S-parameter import, preview caching and export file writes
are supplied through configured capabilities. Feature registration preserves shared
owners; presentation constructors do not silently create replacement infrastructure.
The public parameterless `MainWindow` compatibility constructor is an explicit
composition entry point, identified by its exact Roslyn member ID. Its other
members remain subject to presentation restrictions.
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
DTOs live in `CAP-DataAccess/Persistence/NativeDesign/`. The presentation-facing
`FileOperationsViewModel` delegates native workflows to `NativeDesignIoCoordinator`,
with separate capture, component/group mapping, loading and post-load services.
`INativeDesignFileStore` is the file I/O boundary; document requests keep prompts,
pickers, accepted I/O and cleanup under one owner. The unused alternate UI
workflow (`ProjectPersistenceViewModel`/`ProjectPersistenceService`) was retired.
`GridPersistenceWithGroupsManager` remains a separately tested low-level grid
serializer; it is not the application's native-document workflow. Native capture isolation and atomic replacement are regression-tested, including
symlinks and Unix modes on the Linux/macOS CI matrix; final integration remains open.
Preserve serialized names, IDs, geometry, models and process data.

`CAP.Avalonia/Commands/CommandManager` owns undo/redo history and entry-scoped side
effects. `CAP_Core.Documents.DocumentSession` owns document identity, revision,
navigation scope and accepted operations. `DocumentOperation` guards publication
and drains cancellation callbacks asynchronously. Non-owning identity bookmarks
admit configured windows only to the document they were opened for. Canvas group
projections use canonical roots and group route publication; remaining feature
owners must adopt these boundaries rather than invent independent root copies.

## Geometry, routing and simulation

Physical positions and dimensions use micrometres. Physical pins hold local offsets
and orientations; routing must account for component translation and rotation.
`Connect-A-Pic-Core/Routing/` provides route calculation and geometry.
`LightCalculation/` contains optical propagation and time-domain calculations;
`Analysis/` contains sweeps and other analysis; `Logic/` contains photonic logic.
`CAP.Avalonia/Services/SimulationService.cs` coordinates presentation-facing
simulation. The main simulation captures immutable matrices, sources and transfers
under `LightCalculation/Snapshots/` before worker execution. Routing captures a
private hierarchy, graph, constraints and crossing physics under `Routing/Snapshots/`;
progress and final results are admitted against the document revision. Wavelength analyzer/spectrum runs also capture matrices, sources, material
models, propagation and facet coupling before scheduling. Truth-table and logic
network extraction also use captured physics. Parameter trials and circuit
optimization run against private matrices; layout compression uses captured bodies
and publishes positions through a revision lease. Their compatibility facades and
new workers share the same numerical kernels. Transient simulations capture the
circuit, excitation and output metadata. Fabrication spectrum and eye sampling
capture participants, excitation and metric inputs while preserving the legacy
random draw order and eye-noise policy. These analysis slices have numerical and
lifetime regression coverage; combined final verification remains open.
A cancellation token alone is not snapshot isolation.

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
uses `IUrlLauncher`. Solver, Python-probe, openEBL, PDK-resolution and bridge-registration sessions use
`Export/Processes/OwnedProcess` through stream drain, exit and descendant cleanup.
Final review still found incomplete deadline/cancellation/drain paths in preview,
GDS execution, uv setup and Nazca PDK parsing; these are being remediated under
A02/A15. The shared launcher alone does not provide this ownership.
Python setup, registry activation and preview caches have separate capability
contracts and effective-environment identity; shared DI aliases preserve one cache
owner. Update manifests are authenticated, while operational release-signing secret
provisioning remains outstanding. These guarantees are tested separately, rather
than inferred from the presence of a shared launcher.

## Assistant and MCP bridge

The assistant already has a tool registry. `AiAssistantFeatureExtensions` registers
`IAiTool` implementations and `IAiToolRegistry`, which `AiAssistantViewModel` uses.
`Services/AgentBridge/Tools/AgentToolCatalog.cs` combines bridge-specific tools with
adapters for assistant tools; bridge tools take precedence for matching names.
Tool calls are dispatched to the UI thread. The loopback bridge is opt-in and
its trust boundary is documented in [AGENT_BRIDGE.md](docs/AGENT_BRIDGE.md).
The operation gate tracks accepted tool work through actual cleanup. Mutations
check revocable commit permission at the mutation itself, including post-await
routing and assistant-adapter publication; responses are checked after cleanup.
Transport retirement drains accepted connections before restart. Speculative repair
trials use private graphs and separate history, including nested group scope and
all five repair families. Graph construction runs off the UI thread; derived grid
work retains its owning operation until cleanup and rechecks captured revisions.

## Feature lifetimes

`Services/Lifetime/ApplicationWorkCoordinator` drains registered feature owners
before application disposal. `ShellWorkScope` closes admission and waits for actual
jobs and asynchronous cancellation callbacks. Shell-owned spectrum/ONA, logic and
truth-table extraction, GateView/MakeGate, parameter sweep, compression, optimization,
transient/eye and Monte Carlo jobs are drained by `MainViewModel.StopAsync`.
Python, solver, editor and registry feature owners also participate in retirement;
the remaining subprocess gaps are described above. Async-only owners require
`await using` or `DisposeAsync`, including in test containers. Synchronous disposal
must not block the UI to hide unfinished work.

Analysis panels retain their document lease through worker completion and any
accepted routing cleanup. Changing the configured document immediately revokes
old publication permission. `ObservablePublicationQueue` sequences complete
display updates when a collection notification triggers a nested configuration
change: all subscribers finish receiving the old event before the replacement
collection is rebuilt. Its asynchronous entry point also keeps the operation
alive until a deferred display action has actually completed.
`PersistentAnalysisDocumentBinding` reconnects the shell-owned analysis panels after
an accepted New/Load; cancelled replacements preserve their current operation.
Independent panel configurations complete even if another notification observer
throws, and shell retirement prevents further presentation changes. The selection
truth-table panel owns extraction and publishes only to the captured, still-current
group; shell shutdown also drains that work.

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

`Directory.Build.targets` includes the architecture analyzer in ordinary desktop
and browser builds. `LUN001` reports forbidden dependencies, `LUN002` unknown
ownership and `LUN003` uncovered projects. Fix the dependency or define justified
ownership; do not downgrade these diagnostics to warnings. The architecture CI
workflow builds both hosts as well as checking source size and guard tooling.

Feature branches target `dev`; releases promote reviewed `dev` changes to `main`
as described in [RELEASE.md](docs/RELEASE.md). Use [ROADMAP.md](docs/ROADMAP.md) for
product plans and [CHANGELOG.md](CHANGELOG.md) for shipped changes.
