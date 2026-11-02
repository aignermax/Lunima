---
name: gds-layout
description: Analyse, import and export GDS layouts with Lunima — inspect cells and layers, reconstruct a GDS into placed components and connections, export a design to Nazca/GDS and netlists, and round-trip check the result. Use whenever a .gds file or a foundry layout is involved.
---

# GDS analysis, import and export with Lunima

## Analyse before importing

`analyze_gds {path}` reads a file without changing the design. It returns:
- the top-cell candidates, with the sub-cells they instantiate (name × count)
- the layer census: polygons, paths and texts per layer/datatype

Text layers usually carry **port labels**:
- gdsfactory uses `1/10`
- Nazca demofab uses `501/1`

Waveguides are usually on `1/0`. Use the census to decide the layer roles before you import.

## Import

`import_gds {path, top_cell, mode, port_layers, waveguide_layers, metal_layers}`:

- **`mode: explode`** (default): every sub-cell instance becomes a component. Pins come from port labels and waveguide ends. Abutting pins and drawn routes become connections.
- **`mode: black_box`**: imports the cell as one component.
- **Layer roles** are `"layer,datatype;..."`.
  - Defaults: ports `1,10;501,1`, waveguides `1,0;1111,0`, metal `11,0;12,0;13,0`.
- **Routing:** `reroute: false` keeps the drawn routes; `true` lets Lunima's router redraw them.
- **Report:** read `placed`, `connected`, `skipped_connections` and `warnings`. Top-cell ports are left free on purpose.

Then inspect the result with `list_components`, `get_component` and `screenshot`. Simulate it like any design; see the photonic-circuit-design skill.

## Export

- **`export_gds {path: "/abs/dir/chip1.py"}`** writes the Nazca script. If Python with Nazca is configured, it also produces `chip1.gds`.
  - Read `skipped_connections`: blocked routes are not exported. Fix them first.
  - Read `unresolved_crossings` and the placeholder `warnings`.
  - Never name the script after a Python module (`numpy.py`, `re.py`).
- **`export_netlist {format}`**:
  - `gdsfactory_yaml` (instances, placements, connections, ports)
  - `sax` (a Python circuit model)
  - `photontorch`
  - `verilog_a` (a folder with modules plus a SPICE test bench)

## Foundry sign-off DRC

`validate_design` is a fast DRC-lite check. Before a tapeout, run the foundry deck:
- `check_foundry_drc {deck: "cornerstone_sin300_drc"}` checks the open design. List the decks by omitting `deck`.
- `{deck: "/path/deck.lydrc", gds_path: ...}` checks any deck and GDS.
- KLayout must be installed.
- Every marker comes back in canvas coordinates with the component it lies on. Fix the design, then re-run the check until it reports `clean: true`.

## Layout versus schematic

After importing or editing a layout, run `compare_netlist {path: "schematic.yaml"}` against the intended netlist. It reports:
- the component counts per type,
- wires missing from the layout or extra in it,
- exact per-instance differences, when the instance names match.

Keep the gdsfactory YAML from `export_netlist` as the reference.

## Round-trip check

After `export_gds`, run `analyze_gds` on the produced GDS. Expect the same number of instances, and port texts on the expected layers. Then `import_gds` it into a `new_design` and compare `list_connections` with the source design.
