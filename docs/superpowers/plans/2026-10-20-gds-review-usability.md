# GDS review: real-layout usability follow-up

The PHRIDGE EAM layout exposed three independent problems: the review permanently
truncated geometry, generic layer defaults missed the file's actual pin metadata,
and the inspector required too much prior knowledge.

## Design

Keep Lunima's existing surface, border, accent and typography tokens. Geometry
remains the main workspace. Replace the role dropdown with compact illustrated
choices: waveguide cross-section, slab, etched region, conductor, heater, pin
arrow, label and auxiliary geometry. Keep each explanation to one sentence.
Profiles remain optional reusable process settings.

Ports are connection points. Show detected points first; show coordinate fields
only for a selected point. Simulation remains explicitly optional and requires
a compatible model. Do not infer simulation behavior from a cell name.

## Implementation and validation

- [x] Render hierarchy with cached cell geometry and viewport/array culling;
      retain detail when zooming, without expanding large arrays in memory.
- [x] Detect pin-layer evidence from original cell metadata; separate detection
      from preview sampling. Avoid guessing process roles from layer numbers.
- [x] Fix analyzer sentinel reaching the Python preview backend.
- [x] Test actual pointer navigation with the guide visible.
- [x] Add illustrated role choices and progressive pin/model inspectors.
- [x] Explain geometry-only cells and route readiness in the review.
- [x] Review changes, run focused regressions and render the native dialog.

The user's Downloads layout is private diagnostic input. It must not be added
to the repository or uploaded. Regression fixtures are synthetic.

## Review findings addressed

Bound repeated SREF traversal as well as arrays. Cull before constructing native
geometry and retain caches across scene changes. Preflight large PATH geometry
before outlining it for detection. Snap labels to marker tips before inferring
width or signal type. Combine marker and label roles on shared layer pairs.
Retain the actual reference ancestry for clicks on reused child cells.
Preserve unknown signal types until the user confirms them.

Label-only suggestions additionally require corroboration for every eligible
label on a layer pair, including parent labels proven by transformed child
anchors. Two valid labels cannot promote unrelated annotations elsewhere.

Local verification includes 697 regression tests, a subsequent 78-test handoff
pass, and real-layout native screenshots. A real-layout review prepared in
225 ms; bitmap captures took 117–324 ms. These are local review measurements,
not simulation or arbitrary-layout performance guarantees.

The final label-evidence guard passed 21 targeted tests, including the public
gdsfactory example and the native import-dialog smoke test. Independent source
review found no remaining blocking findings.

## Follow-up from native layout testing

- Ship guided example assets with desktop builds, and explain missing assets
  instead of silently ignoring the manufacturing chapter button.
- Frame the complete selected layer and pulse briefly; stop animation when hidden
  and respect reduced motion. Retain the full-layout fit action.
- Offer a guided, explicit assembly-opening action. Interpretation is per source
  cell type; repeated placements remain separate instances after expansion.
  Flattened leaf geometry is not falsely presented as recognized MMI/MZI parts.
- Lock port positions by default and reveal coordinate corrections only on request.
  Leaving the stage or switching cells locks them again.
- Include imported frozen paths in marquee selection, with ordinary route
  selection and a single undoable delete for mixed selections.

Follow-up validation uses real headless pointer events, transformed tiny geometry,
huge array bounds without flattening, pulse lifecycle checks, repeated assembly
placements, and selection/delete/undo regression tests.

The final combined follow-up run passed 730 tests (no failures or skips).
Desktop Release build passed with existing warnings; its output includes all
29 example designs and the manifest. Independent review findings about stale
primary selection and no-op crossing deletion undo were fixed and covered by
regressions before the final run.

## Large-layout rendering follow-up

Native feedback showed that cell-level culling still submits a dense top cell's
entire layer. Repainting it for a pulse made this worse. Remove the pulse and
isolate the selected layer by default, including picking; keep context opt-in.

Use spatial subdivision inside cell layers and screen-space detail selection.
Expose automatic, fast overview, and fine-detail modes. Simplification is
display-only: preserve the source polygons, hierarchy, and exact import.
Fine detail still aggregates pathological dense hierarchy arrays. Bound retained
render geometry and raster caches; dispose raster resources on scene changes and
detach. Measure both complete overview and occupied zoomed regions locally.

Restore visible local routing hints without resurrecting unsafe legacy material
inference. Route-shaped source geometry suggests a candidate, not an optical or
electrical classification. Provide explicit, undoable choices and navigation
between candidates. No network model or API key is needed.

Local native bitmap benchmark on the private layout: complete overview warm
rendering fell from 70.18 ms to 1.00 ms; the first overview setup took 245.90 ms.
The dense selected layer rendered at 0.90 ms warm. Occupied centered zoomed
regions took 3.64 ms (10×) and 1.21 ms (100×). These are local render timings,
not application-wide frame-rate guarantees. Fine detail intentionally costs more.

The advisory geometry scan finds 19 raw candidate layer pairs in this file.
It does not resolve duplicate/helper layers or infer fabrication materials;
known port assignments suppress their corresponding route suggestions.

Verification: 741 combined regressions passed, followed by 25 native
dialog/spatial/localization checks and 16 preview checks. The final Release
build passed. Independent review findings (hidden-array pick pruning, bitmap
ownership/disposal, and snapshot marker padding) are addressed.
