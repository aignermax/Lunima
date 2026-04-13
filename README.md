# Lunima

**AI-collaborative photonic design system**

<img width="1407" height="932" alt="Lunima photonic design canvas" src="https://github.com/user-attachments/assets/a784b7f3-7300-453f-a6a8-12638367ef9a" />

<img width="1534" height="475" alt="Lunima component library and simulation panel" src="https://github.com/user-attachments/assets/80dcf0e1-1cbf-4709-ab79-5292cf3e415e" />

> Lunima is a photonic design environment for fast, physically grounded circuit exploration and system-level thinking.

---

## Purpose

Lunima provides a unified environment for designing, exploring, and communicating photonic circuits across teams and abstraction levels.

Rather than replacing existing simulation tools, Lunima focuses on:

- **Fast, intuitive circuit-level exploration**
- **Shared visual representation** of photonic systems
- **Bridging the gap** between design, simulation, and system-level understanding

---

## Vision: Photonic Intermediate Representation (PIR)

### The Central Idea

👉 **Lunima is becoming the central representation layer for photonic systems**

- **Today:** GUI-based design tool
- **Tomorrow:** Central PIR with multiple views and exports
- **Future:** Integration hub for PhotonTorch, PICWave, Verilog-A, LTSpice

### PIR = `.lun` File Format

The `.lun` file format is evolving to become the PIR — a tool-independent representation that:

- Defines components and connections as a **netlist / graph**
- Accumulates physical, structural, and simulation data over time
- Enables **export to and import from** different simulation tools
- Evolves from schematic → device simulation → circuit simulation → system co-simulation

**This means:**
- GUI is just one view of the PIR
- Export is just a transformation of the PIR
- AI becomes extremely powerful with structured access to PIR

### Role in the Photonics Toolchain

| Layer | Tools | Purpose |
|-------|-------|---------|
| **Device-level simulation** | Tidy3D, FimmProp, Lumerical MODE | EM simulation, S-matrix extraction |
| **Circuit-level simulation** | PICWave, PhotonTorch | System behavior using S-matrices |
| **System / Digital Twin** | Verilog-A, LTSpice, Xyce | Photonic + electronic co-simulation |

**Lunima's position:** Between schematic design and system-level simulation.

Lunima acts as a **design and integration layer**, allowing information to flow between tools rather than duplicating their functionality.

---

## Key Features

- **Physical Coordinate System** — Components positioned in micrometers (µm), not grid tiles
- **Explicit Waveguide Routing** — Automatic routing with S-bends and Manhattan geometry
- **Dynamic Loss Calculation** — Transmission coefficients from actual path geometry
- **Hierarchical Design** — Reusable subcircuits with external pins and frozen layouts
- **PDK Integration** — JSON-based component libraries with physical pins and S-matrix data
- **Nazca Export** — Export designs to Python/Nazca for fabrication pipelines
- **AI Assistant** — Natural language circuit design with Claude integration
- **Cross-Platform UI** — Avalonia-based (Desktop, WebAssembly planned)

---

## Getting Started

### Installation

Download the latest release from [GitHub Releases](https://github.com/aignermax/Lunima/releases).

Supported platforms: Windows, macOS, Linux

### Building from Source

**Prerequisites:** .NET 8.0 SDK

```bash
# Quick start
make run      # or ./run.sh

# Build and test
dotnet build
dotnet test
```

---

## Documentation

- **[Architecture Guide](ARCHITECTURE.md)** — Code structure, DI, routing, S-matrix simulation
- **[Changelog](CHANGELOG.md)** — Completed features and milestones
- **[Agent Development Guide](CLAUDE.md)** — For AI-assisted development
- User Guide *(coming soon)*
- API Documentation *(coming soon)*

---

## Roadmap

### 🎯 High Priority: PIR Evolution

- [ ] **Expand `.lun` format** — Add S-matrix storage, simulation metadata, external tool links
- [ ] **Import S-parameters from Lumerical/Tidy3D** — Direct device simulation integration
- [ ] **Export to PhotonTorch** — Circuit-level time-domain simulation
- [ ] **Export to Verilog-A** — System-level co-simulation

### 🎯 High Priority: Professional Features

- [ ] **Connection validation** — Warn about pin angle mismatches, unconnected pins
- [ ] **Design Rule Checking** — Min bend radius, spacing violations
- [ ] **Wavelength sweep / spectral response** — Plot transmission vs wavelength
- [ ] **Parameterized models** — Components with interpolated S-matrices
- [ ] **Direct GDS export** — Without Nazca intermediate step

### 🎯 High Priority: PDK Expansion

- [ ] **Expand SiEPIC PDK** — Add remaining 31 components (43 total)
- [ ] **SiEPIC SiN PDK** — Silicon nitride platform support

### 🔮 Future Vision: Tool Integration

- [ ] **Browser version** — WebAssembly deployment
- [ ] **Python PDK extractor** — Convert Nazca PDKs to JSON
- [ ] **Component properties panel** — Edit S-matrix parameters per instance

### 🔮 Future Vision: Optical Computing

- [ ] **Nonlinear components** — S-matrix depends on input power
- [ ] **Delay lines** — Waveguide loops with propagation time
- [ ] **Pulsed laser source** — Time-domain clock for optical logic
- [ ] **Time-domain simulation** — Step-based solver for signal propagation
- [ ] **Multi-chip interconnect** — Inter-chip optical cables

See [CHANGELOG.md](CHANGELOG.md) for completed features.

---

## Contributing

We welcome contributions! Please see:

- [CLAUDE.md](CLAUDE.md) for agent development guidelines
- [ARCHITECTURE.md](ARCHITECTURE.md) for technical details

For AI-assisted development, use the provided Python tools:

```bash
python3 tools/smart_test.py              # Compact test output
python3 tools/semantic_search.py "query" # Semantic code search
```

---

## License

MIT License - see [LICENSE](LICENSE) for details.

---

## Origins

Lunima originated from [Connect-A-PIC](https://github.com/Akhetonics/Connect-A-PIC) and has evolved into a standalone photonic design system.
