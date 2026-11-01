---
name: photonic-circuit-design
description: Design, route, simulate and verify a photonic integrated circuit with the Lunima MCP tools — from an empty chip or an existing .lun file to a checked, simulated, saved design. Use for MZIs, ring filters, splitter trees, switch fabrics and any component-level PIC work.
---

# Photonic circuit design with Lunima

Lunima runs headless behind the `lunima` MCP server: the same routing, S-matrix simulation and design
checks as the desktop app. Units are **µm** (positions are a component's **top-left corner**), angles
are degrees counter-clockwise, powers are linear relative to a unit laser (dB = 10·log10).

## Workflow

1. **Start**: `new_design`, `open_design {path}`, or `load_example {name}` (omit the name to list examples).
2. **Pick parts**: `list_component_types {filter, pdk}`. It returns the footprint, pins (offset from the top-left corner and facing), parameters, and whether the active process allows the part. Prefer one PDK per chip.
3. **Floorplan**:
   - Use `get_floorplan` to see free areas.
   - Use `find_free_space {width, height, near_x, near_y}` to get spots where a block fits.
   - Leave about 100–200 µm between parts that will be wired, so the router can bend.
4. **Place**: `place_component {type, x, y, rotation_deg, name}`.
   - Give every part a short `name`. You can use it as an id afterwards.
   - An occupied spot is refused with a hint; `nudge=true` accepts the hint.
5. **Wire**: `create_connection {from_component, from_pin, to_component, to_pin}`.
   - Every call waits for the router and returns `length_um`, `loss` and `blocked`.
   - Without pin names, the closest free facing pins are used.
6. **Light**: `set_light_source {id, enabled, wavelength_nm, power}`. A source with `enabled=false` is an **output**. Mark the input coupler on and each output coupler off.
7. **Simulate**:
   - `simulate` returns per-output `power_db` and `transmission_db`. `open_ends` shows light escaping through unwired pins, which usually means a missing wire.
   - Then `sweep_parameter {id, parameter}` for phase/coupling transfer curves, and `sweep_spectrum {start_nm, end_nm}` for spectra (extinction ratio, FSR).
8. **Check**: `validate_design {min_severity: "problem"}` runs DRC-lite (width, spacing, bend radius per PDK process), overlaps and process checks. Fix every problem. Hints such as open fiber pins are often intended.
9. **Look**: `screenshot` (whole design or a µm region) shows the routed layout with the power overlay.
10. **Save**: `save_design {path}`. Export with `export_gds` (Nazca script plus GDS) and `export_netlist` (gdsfactory YAML, SAX, Verilog-A).

## Fixing problems

- **Blocked wire** (`blocked: true`, `failure_reason`): move a part with `move_component {id, dx, dy}`, or call `suggest_fixes {connection_id}` and then `apply_suggestion {id: "s1"}`. Crossing wires can be cut with `list_cut_candidates` and `insert_crossing`.
- **High loss**: look at `loss.bends_db` and `min_bend_radius_um` in `list_connections`. Give the router more room.
- **Mistakes**: every edit is undoable with `undo`.

## Verify, do not assume

- After each design step, re-read the state (`get_component`, `list_connections {blocked_only: true}`) instead of trusting the plan.
- A passive circuit can never output more power than it receives. A sum of outputs above the injected power means a modelling error, so report it.
