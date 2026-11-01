# Agent bridge (MCP)

Lunima can host a small [Model Context Protocol](https://modelcontextprotocol.io) server
inside the running desktop app. An AI agent such as **Claude Code** connects to it and can
then inspect and edit the design that is currently open — list failed (red) connections with
their reasons, move a component, cut wires with crossings, slide crossings, shift waveguide
segments, re-route, take a screenshot of a region, save — while you watch the canvas. Every
edit goes through Lunima's undo history, so **Ctrl+Z** reverts whatever the agent did.

## Headless: no window at all

The same tools also run **without the desktop app**. The `CAP.Desktop` binary (`CAP.Desktop.exe`
on Windows, `Lunima.app/Contents/MacOS/CAP.Desktop` on macOS) starts the production application
on Avalonia's headless platform when its first argument is `mcp` or `agent`: the real
view-models, routing, simulation and rendering (screenshots have real pixels), but no window, no
HTTP socket and no update check.

```bash
# MCP server over stdin/stdout — Claude Code starts it on demand:
claude mcp add --scope user lunima-headless -- /path/to/CAP.Desktop mcp

# One-shot CLI: JSON on stdout, diagnostics on stderr
CAP.Desktop agent tools                                   # every tool + argument schema
CAP.Desktop agent call get_design_summary --design chip.lun
CAP.Desktop agent call move_component '{"id":"mmi1","dx":20}' --design chip.lun --save
CAP.Desktop agent run steps.json --design chip.lun --save out.lun --images shots/
```

A script for `agent run` is a JSON array of `{"tool": "...", "arguments": {...}}` steps (`-`
reads it from stdin); it stops at the first failed step unless `--keep-going` is given. Text
results that hold JSON are printed as JSON; images are written to PNG files and reported by
path. Exit codes: `0` success, `1` a tool reported an error, `2` malformed command line.

The headless session reads a private *copy* of your preferences (PDK libraries, Python
interpreter, routing settings), so nothing it does — recent files, reopen-last-project —
changes the interactive app. Every session starts with an empty design unless `--design` is
given.

### Claude Code plugin

The repository is a Claude Code plugin marketplace (`.claude-plugin/marketplace.json` →
`claude-plugin/lunima/`). The plugin bundles the headless MCP server (started as `lunima mcp`)
and four workflow skills: `photonic-circuit-design`, `gds-layout`, `logic-and-cpu` and
`chiplet-assembly`.

```bash
CAP.Desktop agent install            # writes the `lunima` launcher (~/.local/bin, or %LOCALAPPDATA%\Lunima\bin)
# in Claude Code:
/plugin marketplace add aignermax/Lunima
/plugin install lunima@lunima
```

`agent install --register` instead registers the server directly as `lunima-headless`
(`claude mcp add --scope user lunima-headless -- <path>/CAP.Desktop mcp`). The plugin also honours
`LUNIMA_EXE` (full path of `CAP.Desktop`) when no launcher is on `PATH`.

## Connect Claude Code (one click)

**Settings → Agent bridge (MCP) → Connect Claude Code → Connect.** This turns the bridge on
and runs, for your user account:

```bash
claude mcp remove --scope user lunima        # "not found" is ignored; makes re-connecting idempotent
claude mcp add --scope user --transport http lunima http://127.0.0.1:39271/mcp
```

Start a new `claude` session (or run `/mcp`) and ask, for example: *"Use the lunima tools:
which connections are blocked and why? Repair them."* If the `claude` CLI is not found, the
page says so and shows the same command to paste into a terminal.

Other ways in:

- **Windows installer (MSI):** the *Custom Setup* page has an optional **Connect Claude Code
  (register the Lunima MCP server)** feature, checked by default. After the files are copied it
  runs `CAP.Desktop.exe --register-claude-code` as the installing user: the bridge is enabled in
  that user's Lunima preferences and registered with Claude Code if the CLI is installed (it never
  fails setup if it is not). Uninstall runs `--unregister-claude-code` (best effort); upgrades keep
  the registration. Silent install without it: `msiexec /i Lunima-Setup.msi ADDLOCAL=MainFeature`.
- **macOS (.dmg) and Linux (portable)** have no install step. On start, if the Claude Code CLI
  is installed but `lunima` is not registered yet, Lunima shows a non-modal bar *"Connect Claude
  Code to Lunima?"* with **Connect Claude Code** / **Not now** ("Not now" hides it until the next
  version). It never registers without that click.
- **Manually:** copy the command under *Or run this in a terminal*.

**The bridge must be on while Claude uses it.** A registered but disabled bridge has no
listening socket, so Claude Code gets an immediate "connection refused" (it never hangs). The
connect action always turns the bridge on; the setting is remembered across restarts.

Port: change it (1024–65535) and press **Apply** if 39271 is taken, then **Connect** again so
the registration points at the new port.

Any MCP client that speaks **Streamable HTTP** can connect to `http://127.0.0.1:<port>/mcp`.

## Tools

The bridge's own tools:

| Tool | What it does |
|---|---|
| `get_design_summary` | File name, unsaved flag, component / group / connection / blocked / crossing-piece counts, routing state, undo depth |
| `list_connections` | Every connection: `id`, endpoints as `component.pin`, the top-level start/end component, length (µm), `blocked`, `failure_reason`, `is_crossing_piece`, `locked`, and `loss` (insertion loss in dB: `total_db`, `propagation_db`, `bends_db`, `junctions_db`, `bend_count`, `junction_count`, `min_bend_radius_um`, `waveguide_family` — see BEND-LOSS-MODEL.md). Filters: `blocked_only`, `component` |
| `list_components` | Top-level components and groups: `id`, name, type, position, rotation, group, child count, lock state, bounds. Filter: `name_filter` |
| `move_component` | Move by `dx`/`dy` or to absolute `x`/`y` (µm). Refuses overlapping moves unless `force=true`. Undoable; waits for re-routing and returns the blocked connections |
| `reroute_connections` | Run the router, wait for it, report blocked state (all, or the given `ids`) |
| `delete_component` | Delete a component/group with its connections. Deleting a crossing component restores the two crossed nets. Undoable |
| `delete_connection` | Delete one connection (a crossing piece dissolves its crossing). Undoable |
| `undo` / `redo` | Undo the most recent step of the shared history (the same step Ctrl+Z would undo), or redo it |
| `get_routing_timings` | Phase timings of the last routing pass (`LastRoutingPassTimings`) |
| `screenshot` | PNG of the whole design, or of a µm region (`x`, `y`, `width`, `height`), returned as MCP image content; `width_px` up to 4096 |
| `load_example` | Open a shipped example (e.g. `Logic Gate Counter 2-bit.lun`) as an untitled copy; without a name it lists the examples. Lunima still asks you about unsaved changes |
| `save_design` | Save the open design: to its current file, an absolute `path`, or a bare file name into the examples folder (how repaired examples are persisted). Replacing an existing file outside the examples folder needs `overwrite=true` |
| `get_connection_geometry` | One connection's segments (straights with `straight_index`/`shiftable`, arcs with centre/radius/sweep), pins (position, facing), blocked state, and the components and wires within `radius_um` |
| `list_cut_candidates` | Where a crossing can be inserted: `wire_intersection` (two wires cross; a blocked wire's placeholder line counts) and `pin_guide` (the Cut tool's pin guide lines), with the wire to cut, the wire that would pass through, and whether a crossing fits |
| `insert_crossing` | Cut `connection_id` at `x`,`y` with the PDK crossing (the Cut tool's own command; crossing pieces may be cut too); with `through_connection_id` that wire is re-docked through the crossing's free ports. One undo step |
| `move_crossing_along_axis` | Slide a crossing `delta_um` along its `horizontal` or `vertical` wire (the wire along the axis keeps its route, the other re-routes). Refusals carry the reason. One undo step |
| `shift_segment` | Shift straight `straight_index` of a routed wire perpendicular to itself by `offset_um` (the in-canvas segment-shift handle). Freezes that wire's route. One undo step |
| `suggest_fixes` | For one blocked wire: try repair candidates on the live design, undo each, return them ranked (see below) |
| `apply_suggestion` | Apply suggestion `s1`, `s2`, … of the last `suggest_fixes`. Refused if the design changed since, or if its trial ended with more blocked wires (unless `allow_worse=true`). One undo step (two for `reorder_neighbour`) |

Authoring — build a design from nothing (all undoable):

| Tool | What it does |
|---|---|
| `new_design` / `open_design` | Start an empty untitled design, or open a `.lun` file by path (waits for routing) |
| `list_component_types` | The loaded PDK libraries: type name, PDK, category, footprint, pins (local offset from the top-left corner, facing angle, domain), tunable parameters, and whether the active process allows it. Filters: `filter`, `pdk`, `category` |
| `place_component` | Place a type with its top-left corner exactly at `x`,`y`, rotated `rotation_deg` counter-clockwise; refuses an occupied spot unless `nudge=true`; optional display `name` (usable as id). Returns the component with absolute pin positions. Supersedes the assistant's centred variant |
| `create_connection` | Wire `from_component`[`.from_pin`] to `to_component`[`.to_pin`]; without pin names the closest free facing pair is used. Waits for routing; returns length, loss, blocked + reason. Supersedes the assistant's variant |
| `get_component` | One component: type, PDK, pose, every pin's absolute position / facing / wired-to pin, parameters, light-source settings |
| `rotate_component` | Rotate by 90/180/270° counter-clockwise; refused when locked or the rotated footprint collides |
| `set_parameter` | Set a tunable parameter by name (e.g. `splitting_ratio`) or slider number, clamped to its range |
| `set_light_source` | Laser on (input) / off (output), `wavelength_nm`, `power`; `mark_as_source` turns any optical part into a source |

Analysis — structured results instead of a canvas overlay:

| Tool | What it does |
|---|---|
| `simulate` | S-matrix simulation: enabled sources, every output (a light source with its laser off) with received power (linear, dB, `transmission_db` relative to the injected power, phase), `open_ends` where light leaves through an unwired pin, and with `include_connections` the power on every wire. Also refreshes the power overlay (visible in screenshots) |
| `sweep_parameter` | Step one parameter (`id`, `parameter` or `slider`, `from`, `to`, `steps`), simulate each value and return every output's power curve with the values of maximum and minimum power. The parameter is restored afterwards |
| `sweep_spectrum` | Wavelength sweep `start_nm`..`end_nm`: per output the transmission (dB) per sample, maximum/minimum with wavelength, extinction ratio and free spectral range |
| `validate_design` | The Checks tab: DRC-lite per PDK process (width, spacing, bend radius), overlaps, unconnected pins, chip bounds, PDK/process compatibility, chiplet interfaces, die assembly. Each finding with severity (`problem`/`hint`), kind, position and wire; `min_severity=problem` skips hints |

Exchange — GDS, layout scripts and netlists:

| Tool | What it does |
|---|---|
| `export_gds` | Write the Nazca script (`path` ending in `.py`) and, unless `generate_gds=false`, run it with the configured Python+Nazca to produce the GDS beside it. Reports skipped blocked routes, unresolved crossings and placeholder geometry |
| `export_netlist` | `gdsfactory_yaml` (inline or to `path`), `sax` and `photontorch` Python scripts, or `verilog_a` (modules + top-level netlist + SPICE test bench into a folder). Parametric components export their S-matrix at the current parameter values |
| `analyze_gds` | Read a GDS without importing: library, top-cell candidates with their sub-cell instances, and the layer census (polygons / paths / texts per layer/datatype) |
| `import_gds` | Import a cell like the GDS-import dialog: `explode` (sub-cells become components, abutments and drawn routes become connections) or `black_box`; layer roles as `"layer,datatype;..."`; optional `reroute`, `auto_connect`, `group` |

Hierarchy and logic — from optical gates to a processor:

| Tool | What it does |
|---|---|
| `create_group` / `ungroup` | Group components into a cell (internal wires move inside; open pins and leaving wires become external pins), or dissolve one level. Undoable |
| `make_gate` | Make a group a logic gate: name external pins as `inputs` (≤ 4), `outputs`, `bias`; the optical truth table is simulated and returned with output powers. Without `threshold` it is put into the widest gap between the output powers (reported with its noise margin). `register=true` for clocked registers. Undoable |
| `save_as_prefab` / `delete_prefab` | Store a group or gate in the user's component library (refuses an existing name unless `replace=true`), or remove it. `list_component_types` lists library groups under `groups`; `place_component` places them by name |
| `logic_build` | Build the logic network from the gates and the wires between them (Logic panel → Build): network inputs/outputs, gates, registers, critical-path delay, fan-out warnings |
| `logic_evaluate` / `logic_truth_table` | One input combination, or all 2ⁿ (n ≤ 12) as compact bit strings |
| `logic_step` | One clock cycle: settle, commit every register, settle again; `reset=true` clears the registers first |
| `isa_run` | Assemble and run a program for the 4-bit learning ISA (docs/ISA.md) in the ISA playground: final ACC/PC/RAM, halt state and a step trace. `photonic=true` executes the operations the built network implements (e.g. ADD on the 4-bit adder chip), with the light's propagation time |

Floorplan and chiplets:

| Tool | What it does |
|---|---|
| `get_floorplan` | Chip bounds, the box the design uses, utilisation (parts with clearance + routed waveguides), dies, and the largest free rectangles |
| `find_free_space` | Top-left spots for a `width` × `height` block, clear of parts and waveguides, nearest to `near_x`/`near_y` first |
| `set_chip_size` | Resize the chip (placement area); not for multi-die assemblies |
| `delete_die` | Delete a die with its parts and links (the die tools above create, bind, move and link dies) |

All other tools of the in-app **AI assistant** (`get_grid_state`, `run_simulation`, `get_light_values`, `inspect_group`, `copy_component`,
`create_group`, `ungroup`, `save_as_prefab`, `clear_grid`, `fit_to_view`,
`get_available_types`) are exposed as well — the tool list is generated from the same
registry, so a new assistant tool appears here automatically. Note that these assistant
tools are not all undoable (e.g. `clear_grid`).

Coordinates are micrometres; positions are a component's top-left corner; angles are degrees
with 0 = +x and 90 = +y (down).

## Repairing blocked wires

Most red wires in the dense logic examples end at a gate input whose neighbour pin is only
8 µm away (the two inputs of a 2×2 MMI), often coming from 4 µm-pitch splitter outputs:
whichever wire was routed first bent around the other pin and sealed it in
(`failure_reason: contention`). A person fixes that by letting two wires swap order through a
crossing, by sliding crossings, by shifting a waveguide segment, or by nudging a component.
The agent gets the same moves:

1. `list_connections` with `blocked_only=true` — the red wires and why.
2. `get_connection_geometry` on one — its pins, its placeholder line, the wires and components
   around it; `screenshot` of that region to see it.
3. `suggest_fixes` with its id. Lunima generates candidates, tries each on the live design —
   execute → full re-route → measure → undo → put every wire's original route back → re-route —
   and ranks them by **blocked count**, then **crossings added**, then **geometry change**
   (each with `improves`, plus a one-line `summary`):
   - `cut_through`: a crossing where a wire in the way crosses the blocked wire's straight
     placeholder line (or one of its pin guide lines), with the blocked wire routed straight
     through it — the "swap order" fix;
   - `reorder_neighbour`: take out a pin-neighbour wire, let the blocked wire route, then
     reconnect the neighbour so it routes around (two undo steps) — the manual pin-pair swap;
   - `move_crossing`: crossings within 80 µm of the blocked wire's pins slid ±10 / ±20 µm along
     each axis (only moves `MoveCrossingAlongAxisCommand.Validate` accepts);
   - `shift_segment`: the two shiftable straights of pin-neighbour wires (pins within 12 µm)
     nearest to the blocked pins, ±10 / ±20 µm;
   - `nudge_component`: the component (group) at either end moved ±10 / ±20 µm across its pin
     direction, where it does not overlap anything.

   Each trial costs two to three routing passes (≈ 40–60 s on *Logic Gate Counter 2-bit*), so
   the call is bounded by `time_budget_s` (default 240, max 1800) and lists what it did not get
   to (`not_tried`). Every suggestion carries `equivalent` — the direct tool call — so the agent
   can replay or vary it (e.g. a different offset).
4. `apply_suggestion` with `s1` (or your own edit with the direct tools); `undo` reverts it.
5. Repeat for the next blocked wire, then `save_design` with the example's file name (e.g.
   `"Logic Gate Counter 2-bit.lun"`) to persist the repaired example.

Notes:

- The sandbox runs on the open design (only the real joint routing pass knows whether a wire
  routes) and clears the redo history. After every trial it checks that the blocked and crossing
  counts are back where they started; if not, it stops and reports `sandbox_problem`.
- There is no "route this wire through waypoints" tool: the router has no waypoint API (A* goes
  pin to pin; the hierarchical pathfinder's portals are internal). Crossings, segment shifts and
  moves are the supported ways to steer a route.

## Security model

- **Loopback only.** The server binds a socket to `127.0.0.1`, never to a network interface,
  so other machines cannot reach it.
- **DNS-rebinding / browser guard.** Requests whose `Host` is not a loopback name, or that
  carry a browser `Origin` other than a loopback origin, are rejected with `403` — a web page
  in your browser cannot drive Lunima, token or not.
- **No token by default.** Any program running on this computer *as you* can call the bridge
  while it is on. A bearer token would not change much against such a program: it is stored in
  your user preferences (`user-preferences.json` in Lunima's app-data folder), which that same
  program can read. **Require access token** (Settings) turns it on — 256 random bits per
  installation, sent as `Authorization: Bearer <token>`; requests without it get `401`; the
  connect command then includes the header; **Regenerate** invalidates every connected agent.
  Useful on machines shared with other local users, or as defence in depth.
- **Opt-in.** Nothing listens until the bridge is enabled (by you, the connect button, or the
  installer feature you left checked).
- **Undoable edits.** Design edits run as the same undoable commands the UI uses, on the UI
  thread. A connected agent can still replace the open design (`load_example`, `clear_grid`)
  and write `.lun` files with `save_design` (it never replaces a file outside the examples
  folder without `overwrite=true`).

## Limits

- Requests are answered with a single JSON body; the optional server-to-client SSE stream
  (`GET /mcp`) is not offered (`405`), which the MCP spec allows. No sessions.
- Routing always routes all nets jointly; `reroute_connections` with `ids` only scopes the
  report.
- Tool calls run on the UI thread one after another; long routing passes are awaited off
  the UI thread (the UI stays responsive) and time out after 5 minutes with
  `routing_complete: false`. `suggest_fixes` can therefore occupy the bridge for minutes.

## Implementation notes

`CAP.Avalonia/Services/AgentBridge/` (vertical slice, DI via `AddAgentBridgeFeature`):

- `Transport/` — loopback `TcpListener` HTTP/1.1 server (`McpHttpServer`), request parser,
  `/mcp` endpoint with the Host / Origin gates and the optional token check.
- `Protocol/` — JSON-RPC 2.0 + MCP `initialize`, `ping`, `tools/list`, `tools/call`.
- `Tools/` — one class per tool, the `AgentToolCatalog` (bridge tools + assistant-registry
  adapters, marshalled to the UI thread), and the `AgentDesignSession`. `Tools/Repair/` holds the
  repair tools: `Cut/` (cut spots via the Cut tool's `ManualCrossingCandidateFinder`,
  `CutAtPoint` + `RouteThroughCrossingCommand`, crossing slides), `Geometry/` (geometry,
  segment shift), `Suggest/` (candidate generator, sandbox, route snapshot, ranking).
- `Registration/` — `ClaudeCliLocator` (PATH first, then the native / npm install locations;
  finds the Windows npm `claude.cmd`), `ClaudeCodeRegistrar` (runs `claude mcp …` through
  `ProcessLaunchFactory`, 30 s timeout per call), `ClaudeCodeConnector` (enable + register) and
  the installer switches `--register-claude-code` / `--unregister-claude-code`.
- `Hosting/` — `AgentBridgeHost` (start/stop per settings) and the UI dispatcher.
- `Headless/` — the windowless host: `HeadlessAgentProgram` (headless Avalonia platform +
  dispatcher loop), `HeadlessAgentSession` (production container over a preferences copy),
  `AgentCliCommand` / `AgentCliRunner` (CLI verbs) and `AgentScript`. `Transport/McpStdioServer`
  is the stdio transport over the same `McpJsonRpcDispatcher` the HTTP server uses.

The server is hand-written instead of using ASP.NET Core / Kestrel: the Windows MSI is a
framework-dependent build that only requires the .NET *Desktop* runtime, so adding the
ASP.NET Core shared framework would break existing installs, and `HttpListener` on Windows
goes through http.sys (URL ACLs, Host-header based routing). A raw loopback socket behaves
the same on Windows, macOS and Linux and needs no admin rights.
