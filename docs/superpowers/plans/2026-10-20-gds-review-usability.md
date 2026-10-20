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
