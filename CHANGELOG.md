# Changelog

All completed features and milestones in Lunima's development.

---

## Core Features

### Physical Design System
- [x] Physical coordinate system (µm positioning)
- [x] Components positioned in micrometers instead of fixed grid tiles
- [x] Physical pins with µm offsets and angle definitions
- [x] Component rotation (0°, 90°, 180°, 270°)
- [x] Grid snapping with configurable grid size

### UI & UX
- [x] Avalonia-based cross-platform UI
- [x] Component placement and connection
- [x] Multi-select (box select + Shift+click)
- [x] Copy/paste (Ctrl+C/V or context menu)
- [x] Delete components and connections
- [x] Keyboard shortcuts (S/C/D/R/G/F/L/P, Ctrl+Z/Y, Ctrl+S) — global, work regardless of focus
- [x] Pin alignment guides (Figma-style visual guides during component placement)
- [x] Zoom to Fit (F key)
- [x] Element locking — Lock components/connections to prevent accidental modification

### Undo/Redo System
- [x] Command pattern implementation
- [x] Full undo/redo support for all operations
- [x] Keyboard shortcuts (Ctrl+Z, Ctrl+Y)

### File Management
- [x] Save/Load designs (.lun JSON format)
- [x] Save As support
- [x] Auto-save support

### Waveguide Routing
- [x] Explicit waveguide routing with S-bends, straight segments, and Manhattan routing
- [x] Automatic routing between physical pins
- [x] Three routing strategies: Straight, S-Bend, Manhattan
- [x] Dynamic loss calculation based on actual path geometry
- [x] Incremental routing — Preserves valid routes when circuit changes

### Component Library
- [x] PDK JSON format — Define JSON schema for PDK component libraries with physical pins
- [x] PDK Loader — Load PDK JSON files and add components to UI library
- [x] Component library search — Fulltext search by name, category, PDK source, or Nazca function
- [x] Component preview — Miniature schematics in component panel showing pin layout
- [x] PDK Management Panel — Toggle PDKs on/off with component count display

### PDK Integration
- [x] SiEPIC EBeam PDK — 12 real components with measured S-parameters, auto-loaded at startup
- [x] Demo PDK with basic components
- [x] Grating Coupler Component — Vertical fiber coupling with Nazca GDS export
- [x] Multi-wavelength S-matrices — Per-wavelength S-matrix data with nearest-wavelength fallback

### Simulation
- [x] S-Matrix light propagation simulation
- [x] Light simulation visualization — Power overlay on connections with color-coded power levels
- [x] Auto-recalculation — Simulation auto-updates when circuit changes while overlay is active
- [x] Per-source laser config — Wavelength and power settings per light source
- [x] Multi-wavelength simulation support
- [x] Parameter sweep — Systematic analysis of component parameter variations

### GDS Export
- [x] Nazca Python export
- [x] Nazca GDS Export Fixes — Chained waveguide segments (no gaps), component rotation, Y-axis transform
- [x] Nazca Integration Tests — Python syntax validation, GDS generation, property-based tests
- [x] Demofab 1:1 Matching — Built-in components match Nazca demofab dimensions exactly with correct origin offsets

### Hierarchical Design
- [x] ComponentGroups & Prefabs — Reusable component groups with external pins
- [x] Frozen waveguide paths in groups
- [x] Hierarchical editing support
- [x] Save groups as prefabs/templates in component library

### Analysis Tools
- [x] Routing Diagnostics Panel — Real-time path validation with JSON export
- [x] Component Dimension Validation — Verify component bounds match pin positions
- [x] Parameter Sweep — Systematic parameter variation analysis

### AI Assistant
- [x] AI Assistant with Claude integration
- [x] Natural language circuit design
- [x] Tool-calling API integration
- [x] AI tools:
  - [x] `get_grid_state` — Inspect current canvas state
  - [x] `place_component` — Place components by type and position
  - [x] `connect_pins` — Connect components via pins
  - [x] `remove_component` — Delete components
  - [x] `group_components` — Create component groups
  - [x] `ungroup` — Break apart groups
  - [x] `save_as_prefab` — Save groups to library
  - [x] `inspect_group` — View internal structure of groups
  - [x] `copy_component` — Duplicate components and groups

### Development Infrastructure
- [x] Dependency Injection — `Microsoft.Extensions.DependencyInjection` with constructor injection
- [x] 1600+ xUnit tests
- [x] Python-based testing tools (`smart_test.py`)
- [x] Semantic code search (`semantic_search.py`)
- [x] Agent development guide ([CLAUDE.md](CLAUDE.md))

---

## Historical Notes

### Project Evolution

Lunima originated from [Connect-A-PIC](https://github.com/Akhetonics/Connect-A-PIC) and has evolved significantly:

- **Decoupled from Godot** — No game engine dependency, pure .NET solution
- **Physical coordinate system** — Move from fixed grid tiles to µm-based positioning
- **Real PDK integration** — SiEPIC EBeam PDK with measured S-parameters
- **AI collaboration** — Built-in AI assistant for natural language design

### Recent Milestones

- **2024-12** — AI Assistant integration with Claude
- **2024-11** — ComponentGroups and hierarchical design
- **2024-10** — SiEPIC EBeam PDK integration
- **2024-09** — Nazca GDS export fixes
- **2024-08** — Physical coordinate system implementation
- **2024-07** — Avalonia UI migration from Godot

---

For upcoming features and roadmap, see [README.md](README.md#roadmap).
