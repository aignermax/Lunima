# Lunima plugin for Claude Code

Lunima's photonic design engine as a Claude Code plugin. It contains:

- **MCP server `lunima`**: the full Lunima application without a window (`lunima mcp`). It has about 70 tools covering:
  - circuits, placement and routing
  - S-matrix simulation, parameter and wavelength sweeps, design checks (DRC-lite)
  - GDS analysis and import, Nazca/GDS export, netlists (gdsfactory YAML, SAX, Verilog-A)
  - floorplanning and chiplets
  - logic gates and networks, and the 4-bit ISA on photonic hardware
- **Skills:**
  - `photonic-circuit-design`
  - `gds-layout`
  - `logic-and-cpu`
  - `chiplet-assembly`

## Install

1. Install Lunima from <https://lunima.org> (Windows, macOS, Linux).
2. Put the `lunima` launcher on your PATH. The launcher forwards to your Lunima installation:

   ```bash
   # Linux
   /path/to/Lunima/CAP.Desktop agent install
   # macOS
   /Applications/Lunima.app/Contents/MacOS/CAP.Desktop agent install
   # Windows (PowerShell)
   & "C:\Program Files\Lunima\CAP.Desktop.exe" agent install
   ```

   The launcher is written to `~/.local/bin` (Linux, macOS) or `%LOCALAPPDATA%\Lunima\bin` (Windows). If that folder is not on PATH, the command says so. Alternatively, set `LUNIMA_EXE` to the full path of `CAP.Desktop`.

3. In Claude Code:

   ```
   /plugin marketplace add aignermax/Lunima
   /plugin install lunima@lunima
   ```

Without the plugin, `CAP.Desktop agent install --register` registers the same server directly with Claude Code as `lunima-headless`.

The headless engine reads a private copy of your Lunima preferences, so you get your PDKs and Python environment. It never changes the desktop app's settings. To watch an agent edit a design live, use the desktop app's own agent bridge instead (Settings → Agent bridge), as described in `docs/AGENT_BRIDGE.md`.
