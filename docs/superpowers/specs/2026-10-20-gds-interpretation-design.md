# GDS interpretation workflow

Approved direction: native implementation of the reviewed six-step prototype, extended with explicit layer roles. The primary user is a photonics researcher importing unfamiliar laboratory layouts.

## User experience

Open a GDS file and inspect the original geometry immediately after parsing. Review the layer/datatype pairs using plain-language roles and linked geometry highlighting. Confirm optical core, slab, etch, electrical, heater, port marker, port label and auxiliary roles. Unknown geometry is retained. A layer role does not define component boundaries.

Review the hierarchy separately: retain a cell as one component or expose its children. Correct component grouping and ports without editing the original mask geometry. Assign a compatible existing model explicitly, or retain a geometry-only component. Review the result before committing to the canvas.

Store the interpretation in the self-contained .lun design alongside the original source. Allow explicit reuse of layer profiles; never apply a profile silently to another process. Edits invalidate confirmation. Provide undo while reviewing.

Use existing Lunima theme resources, controls and the green TourAnchorOverlay cursor. First-use guidance is short, contextual, dismissible and restartable. It points to real controls and never steals clicks.

## Safety and performance

- Source geometry and units are preserved; interpretation is separate.
- Confirmed layer roles override heuristic fallbacks, including marker versus label semantics.
- Unknown/slab/auxiliary polygons are not silently reclassified as waveguides.
- Geometry-only does not invent a lossless physical model.
- Background analysis is cancellable; avoid repeated flattening on pointer movement.
- Existing projects and legacy import callers remain compatible.
- No implicit rerouting or destructive overwrite of source files.
- Repeated cells, mirrored instances and large files require bounded work.

## Validation

Exercise real synthetic GDS import through layer mapping, hierarchy interpretation, pin changes, placement, .lun capture/restore and export preservation. Headless UI tests click actual role/confirmation/tour controls in English and German. Run architecture checks and an independent code review before handoff.
