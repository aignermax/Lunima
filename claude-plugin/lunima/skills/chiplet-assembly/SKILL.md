---
name: chiplet-assembly
description: Design multi-chiplet photonic systems with Lunima — create dies with their own fabrication process, place parts per die, link dies through edge couplers, check alignment and per-die design rules, and size the floorplan. Use for chiplets, multi-process assemblies (e.g. SiN next to SOI or InP) and system-level layouts.
---

# Chiplet assemblies with Lunima

A **die** is a chiplet with its own fabrication process, and therefore its own PDK and design rules. Waveguides never cross a die edge. Dies connect only through **edge couplers** on facing edges, and the coupling loss depends on the facet gap and lateral offset.

## Workflow

1. **Plan the floorplan:**
   - `get_floorplan` gives the chip bounds, utilisation and free rectangles.
   - `set_chip_size` sets the chip size for a single-die design.
2. **Create dies:**
   - `create_die {name, process, width, height}`, then position it with `move_die`. `list_dies` shows the available processes and the dies.
   - `set_die_process` rebinds a die. Each die only accepts parts of its process (`list_component_types` → `allowed`).
3. **Fill each die:**
   - Place parts inside the die outline with `place_component`, using `find_free_space` near the die.
   - Wire them inside the die with `create_connection`.
4. **Link dies:**
   - Put an edge coupler at each facing edge.
   - Link them with `link_dies`, which aligns the couplers across the gap.
   - `list_dies` reports each link's loss (dB, at 1550 nm).
5. **Check:**
   - `validate_design` checks the per-die DRC, chiplet interfaces (misaligned or too-distant couplers) and die assembly.
   - `simulate` checks the end-to-end transmission across the link.
6. **Rework:**
   - `move_die` moves a die rigidly with its parts and wires.
   - `delete_die` removes a die with its contents.
7. **Export:** `export_gds`. Every routed waveguide is exported on its own die's process cross-section.

## Rules of thumb

- Keep the facet gap small and the couplers co-linear. The lateral offset dominates the coupling loss.
- Put the more demanding process (smallest bend radius, gain, modulators) on its own die and keep passive routing on the cheapest process.
- Re-run `validate_design` after every die move. Interfaces are re-checked, not assumed.
