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
225 ms; bitmap captures took 117â€“324 ms. These are local review measurements,
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
regions took 3.64 ms (10Ã—) and 1.21 ms (100Ã—). These are local render timings,
not application-wide frame-rate guarantees. Fine detail intentionally costs more.

The advisory geometry scan finds 19 raw candidate layer pairs in this file.
It does not resolve duplicate/helper layers or infer fabrication materials;
known port assignments suppress their corresponding route suggestions.

Verification: 741 combined regressions passed, followed by 25 native
dialog/spatial/localization checks and 16 preview checks. The final Release
build passed. Independent review findings (hidden-array pick pruning, bitmap
ownership/disposal, and snapshot marker padding) are addressed.


## Context, mask ambiguity, and hierarchy follow-up

Default to muted layout context with a gold selected layer. Right-click toggles
isolation; isolation focuses the chosen layer and returning restores overview.
Keep bounded overview/geometry/raster caches and LOD, with no geometry pulse.
Text anchors are hollow squares; reviewed connection ports remain circles.
The first-use guide points to the layer list before the role selector and
supports pointer and keyboard interaction. Explicit guide dismissal is respected.

Expose direct named child cells and placement counts, with a source breadcrumb
and parent navigation. Opening a child changes the import boundary in one undo
step. Source leaves cannot be subdivided through hierarchy navigation.

The private EAM/SOA assembly contains 87 direct placements across 10 cell types,
including two MZI switches, six 1x2 MMIs, and one 2x2 MMI. Several layer pairs are
nearly coincident; geometry overlap does not establish their fabrication role.
Multiple core layers, or electrical/heater layers, form planar routing networks.
Show a nonblocking warning about XY overlap and missing stack/via semantics.
Synthetic tests cover duplicate-mask deduplication and cross-layer overlap;
no process-stack inference or import algorithm change is introduced.

Independent review found an encoding roundtrip in UI symbols and missing
keyboard guide advancement. Symbols are restored as UTF-8 and keyboard
selection advances the guide. Localization covers all five supported languages.

Verification: 753 regression cases passed; the remaining localization coverage
failure was an XML-entity spelling rejected by the existing literal-string
allowlist. Restoring the original UTF-8 symbols resolves it. All 10 handoff
checks then pass, including coverage, native UI, keyboard guide and benchmark.
Release build succeeds (existing analyzer/XAML warnings remain).
With a selected layer and context visible, local native overview rendering is
0.89 ms warm / 173.15 ms initial; occupied 10x/100x views are 3.53/1.19 ms warm.
No-selection overview is 1.02 ms warm / 298.32 ms initial. These are bounded
preview-render measurements, not end-to-end loading or UI latency guarantees.


## 2026-10-21: concrete grouping and non-destructive imported editing

Keep the approved layer review UI unchanged. Components support exact placed
occurrences: click selects one placement, Ctrl/Shift-click adds/removes another.
The visible selection card creates a named component group, not a new physical
simulation model. Single exposed cells already import as separate components.
Instance keys follow the expanded import's per-cell occurrence order. Persist
explicit instance membership while keeping legacy cell-type groups readable.
Opening/collapsing source hierarchy invalidates occurrence groups with an explicit
message and undo. Review selection is bounded with a visible large-hierarchy notice.

Preserve exact imported mask polygons when only one endpoint moves. Indicate the
resulting gap separately from geometric path validity so the retained shape stays
an obstacle. Disconnected imports transmit zero light. Explicit rerouting converts
to generated paths; fitted bend/shift handles are hidden on exact polygon routes.
Joint translations and undo retain all imported layer polygons.

Main-canvas outline rendering already has bounded raster detail. Its zoom-tier
transition unnecessarily fell back to vectors (and sometimes culled everything)
before rebuilding a bitmap. Reuse a sufficiently sharp cached bitmap when zooming
out and cache immutable bounds/layer metadata. Source geometry and export stay exact.

Separate per-die export already exists. Improve discoverability, unique filenames,
shared environment/backend checks, per-file error reporting and true GDS success
counts. Scope component dependencies to the selected die while retaining the global
process-interconnect plan whose activation distinguishes heterogeneous process stacks.
Existing global metal process settings are not converted into a new per-die stack model.

Review catches: refresh group command after undo; preserve unselected membership
when regrouping; selection-limit explanation; UTF-8 preservation; consistent backend
exclusion in die export; valid empty-cell Python bodies.

Verification: 846 broad regressions passed. Subsequent review reproductions caught
legacy-group membership loss, inconsistent backend exclusion, and invalid empty-die
Python; those are corrected. All 91 final targeted tests pass, including native
Ctrl-click/create/undo, occurrence import, group migration, source polygon
preservation, export preflight/results, Python AST syntax, and localization.
Release build succeeds with existing analyzer/XAML warnings.
Synthetic main-canvas test with 10,000 polygons now draws one cached image across
four successive zoom-out tiers (0.37/0.09/0.05/0.04 ms in the local run), rather
than 100 vector calls followed by missing geometry. This is a renderer fixture,
not a full-frame measurement of the private imported design. Per-die GDS execution
in workflow tests uses a fake service; actual generated Python syntax is checked
independently, and no new foundry validation or independent metal-stack support is claimed.


## Main-canvas zoom regression follow-up (2026-10-21)

Profile the complete DesignCanvas against a locally supplied private GDS, rather
than extrapolating isolated cache draw timings. The opt-in probe accepts
GDS_MAIN_CANVAS_PROBE and retains the input outside the repository; optional
GDS_MAIN_CANVAS_PORTS enables a specific review scenario, not inferred process
semantics. Headless frame timings are CPU-render measurements, not desktop FPS.

Rendering-only detail levels simplify subpixel curves without changing imported
source polygons or exports. Reuse cached bitmap tiers before requesting geometry;
small preview images also need bounded-resolution image levels. Suppress unreadable
names and group pins before shaping/occupancy work, including selected/hovered large
groups; retain the individual highlighted connection target. Viewport culling must
include source mask bounds, not just recovered centerlines.

Resolve mutable imported route validation once per frame and share that snapshot
between bounds, styling and warning overlays. Preserve invalidation after in-place
path edits. Explain explicit conversion to editable generated routing and support
undo; group-only imports must expose their existing rerouting action. Pin-less
geometry retains the existing lossless ungroup-and-drag workflow.

Overlapping GDS layers are evidence for companion-layer suggestions, not proof of
shared physical connectivity. Do not silently mark all matching masks OpticalCore
or discard mask identity: process mapping is required for physical layer semantics.


Final verification of this follow-up: 643 rendering, preview, import, routing,
localization and ungroup/source-preservation tests passed. A subsequent adversarial
simplification test reproduced 4.2 million point reads for a 2,052-vertex sawtooth;
a linear 32N point-visit budget now falls back to exact source geometry. All 22
focused geometry/raster/LOD tests pass after that final guard.

The private main-canvas probe expanded 1,535 components and 8,909 preserved internal
paths (65 with both pins, 8,844 without pins). The original warm whole-frame samples
were approximately 25–56 ms. Final warm zoom samples were 18.55 / 17.84 / 14.54 /
11.56 / 10.45 / 9.20 / 12.32 ms; hover 14.73 ms; ungrouped 14.45 ms. Initial frame
setup remained 611 ms and creating a previously uncached finer image level peaked
at 416 ms. These cold costs remain a limitation; no universal frame-rate or geometry-
independent latency guarantee is claimed. Group image counters confirm cache reuse.

Group/canvas overview images retain original paint order and opacity. Cache owners
are weak references so bitmap residency does not retain unloaded source geometry.
Source changes, translations and layer visibility invalidate images. The cache is
bounded by pixels, entry count and image dimensions, and falls back to individually
culled rendering where necessary. Original polygon masks remain exact for export.

## Imported cell occupancy and post-import hierarchy (2026-10-21)

Treat a flat imported component with outlines as occupied polygons rather than its
outer rectangle in routing, placement, drag, multiselection and placement preview.
Keep broad-phase bounds; cache transformed polygons and raster row spans so precise
checks do not rewalk large source geometry on every pointer event. Pin escape must
not tunnel through separate solid geometry. Keep the existing rendering LOD intact.

Expose original embedded GDS child references through one cancellable preparation
and one undoable replacement. Preserve pose, reflection, source masks, layer/datatype,
parent interface metadata and own-cell geometry. Refuse unsupported source transforms
or ambiguous interface mappings before mutation. Preserve source identity and any
interface aliases in design-scoped templates so save/load and later expansion work.

Placement: a compact contextual canvas action appears on selection, with a matching
context-menu entry. This serves Peter's direct manipulation and Priya's own-GDS
workflow, while making the action discoverable for Jonas without a modal tutorial.
The preserved-route conversion uses the same surface; it remains explicit and undoable.

Verify synthetic concave/internal-port cases, transformed hierarchy round trips,
residual occupancy, stale/cancelled preparation, and the user's local EAM source.
The private source and derived screenshots stay outside the repository. Obtain an
independent code review before committing, and address its preservation/performance findings.

Verification: 1,005 focused import, placement, collision, routing, rendering, UI and
localization tests passed through smart_test.py. The local source probe expands the
large assembly into 88 source/own-geometry components and compares every polygon in
world coordinates and all eight original interface pins, then checks undo. Preparation
took 208 ms off-thread; the atomic apply took 310 ms and undo 17 ms in the headless
probe. Applying a large expansion can therefore still briefly block the UI; it is not
a constant-time interaction. New tests cover cached dense outlines, spatial edge
queries, actual external-to-inner-facet routing, persisted port aliases and unmatched
parent-owned interfaces. Existing .lun frozen-mask persistence quantizes to 0.001 µm.

Default grouped imports retain their own background masks when ungrouped. Backgrounds
are released before internal route masks to retain painter order. Missing VM source
metadata after ungroup resolves only when the embedded source template is unambiguous.
Externally connected cells, unsupported magnification and ambiguous ports refuse
expansion before changing the design. Flat source cells without child references do
not acquire invented components. Auxiliary physical layer meaning is not inferred by
the collision code: existing background flags stay nonblocking; other outlines block.

## Selection and conversion responsiveness (2026-10-21)

Keep overview detail limits active for selected and hovered content. Component names,
route warnings and group warning badges must not bypass zoom limits. Highlight the
source polygon mask of imported routes rather than an approximate fitted centerline;
reuse overview raster caches for dense selected geometry as well as ordinary drawing.

Provide Ctrl+A through the canvas and global window shortcut paths, preserving text
field shortcuts. Select the current editable scope, not children of closed groups or
root background paths while editing a group. Publish one completed selection per
collection so status, hierarchy and properties panels do not rebuild for every item.
Expose Select all in the canvas context menu and localized shortcut help.

Prepare imported-route undo snapshots from detached scalar values off the UI thread.
Yield before conversion, guard stale document/scope/targets, avoid duplicate routing
passes and record already-applied results without reapplying them. Keep final history
recording atomic; deferred redo geometry must own its snapshot and produce fresh data.
Queue group-edit grid initialization before subsequent routing without building the
grid synchronously on the UI thread.

Zero physical pins do not make an imported cell nonblocking: imported pads still
occupy their source polygons. Keep enclosing die frames as background. Preserve the
smallest positive fitted source bend radius as routing intent (without lowering a
larger existing setting), including legacy conversion and undo/redo. This does not
infer a foundry rule. Previously saved nonblocking flags are not silently migrated;
reimport applies the corrected classification.

Verification includes real Ctrl+A input versus text selection, all four workspaces,
large batched selection, source-mask hover pixels, outline selection raster work,
conversion dispatcher heartbeat, stale target removal and repeated undo/redo. Private
EAM layout measurements stay outside the repository. Independent review specifically
checks selection cache bypass and history races during asynchronous preparation.

Measured private-source selection after ungrouping: 1,535 components and 11,051
frozen paths. Selected warm frames improved from 61.52 ms to 31.97 ms, with the
first selected frame at 67.13 ms. Per-frame opacity masks were rejected after
measuring 486.78 ms; the final implementation caches tinted image variants within
the existing pixel budget. The private selected-image render was inspected: source
curves and layer contrast remain visible, and no overview name flood returns.
Cold first-import raster setup still costs hundreds of milliseconds (731 ms in this
probe); these headless measurements are not a universal desktop frame-rate guarantee.

Pre-existing conversion limitations remain: crossing-insertion history ownership is
not redesigned here, and Stop cancels the current routing pass rather than the whole
outer multi-group batch. Group cancellation now preserves imported source routes
instead of serializing null paths and silently dropping them.

Final focused verification: 822 tests passed, including real canvas/text Ctrl+A input,
hierarchy batching and both previous CI failures. The 200,000-segment conversion
probe measured 28.8 ms total and a 14.9 ms maximum dispatcher heartbeat gap.
