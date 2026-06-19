# Per-instance editable Nazca code with live preview + size recompute

**Issue:** #556  ·  **Builds on:** #541 (merged)  ·  **Scope:** geometry-only

## Problem

#541 added a per-instance Nazca *parameter* override (function name + params). Power
users want to **see, edit, and paste a complete Nazca cell function** for a single
placed instance, validate that it runs, see the resulting geometry live, and have the
component's size recomputed — without touching the PDK template or other instances.

## Scope

- **In:** editable raw Nazca code per instance → executed to produce geometry; live
  preview; recomputed bounding-box size; overlap warning; `.lun` persistence.
- **Out:** S-matrix recompute (mode-solver work, #533/#544). Optical pins + S-matrix
  stay as the template — only geometry + size change. A mismatch between the new
  geometry's port count and the component's pins surfaces a hint, nothing more.

## Architecture / data flow

```
[ Component Settings Dialog · "Nazca Code" tab ]
  Code editor (left)  ──Run──▶  InstanceNazcaCodeEditorViewModel
  Live preview (right) ◀──────  RelayCommands: Run, Apply, Reset
                                       │
                                       ▼
                 NazcaComponentPreviewService.RenderRawCodeAsync(code, ct)
                                       │  (subprocess · timeout · kill · JSON)
                                       ▼
                 scripts/render_component_preview.py  (NEW raw-code mode)
                   writes code → temp .py → importlib → component() → Cell
                   → { success, bbox, polygons, pins } | { success:false, error }
```

Reused: preview subprocess + timeout/kill (no hang/crash on bad or infinite code),
the JSON error contract, `GdsPolygonRenderer` for preview rasterisation, and #541's
`NazcaCodeOverride` persistence. New: raw-code mode in the python script, a service
method, an editor VM, the dialog tab, size recompute + overlap marking.

**Execution contract:** the entered code must define `def component():` returning a
Nazca cell (fallback: a module-level `cell` variable). Imports and helper defs in the
same code are allowed (it becomes a temp module). The convention is shown as a comment
in the pre-filled editor.

## Components

### 1. Python — `render_component_preview.py`
- New invocation mode `--code-file <path>` (mutually exclusive with module/function).
- Writes is unnecessary — the C# side writes the temp file; the script `importlib`-loads
  it, resolves `component` (callable) or `cell` (variable), builds the cell, and reuses
  the existing `_extract_bbox` / polygon / `_extract_pins` path → same JSON.
- Syntax/runtime errors → `{ "success": false, "error": "<message incl. line>" }`.

### 2. Service — `NazcaComponentPreviewService`
- `Task<NazcaPreviewResult> RenderRawCodeAsync(string code, CancellationToken)`:
  writes `code` to a temp `.py`, invokes the script in raw-code mode, parses JSON into
  the existing `NazcaPreviewResult` (Success/Error/Bbox/Polygons/Pins), deletes the temp
  file. Reuses the existing timeout/kill; cache keyed by code hash.

### 3. ViewModel — `InstanceNazcaCodeEditorViewModel`
- `[ObservableProperty] string Code` — pre-filled with a runnable template that
  reproduces the current PDK function call, so "see → edit → paste".
- `[RelayCommand] RunPreviewAsync` — async, non-blocking: calls `RenderRawCodeAsync`,
  sets `PreviewData` / `ErrorText` / `IsValid` / `IsRunning`. Never throws.
- `[RelayCommand] ApplyOverride` — enabled only after a successful run: persists `Code`
  into `NazcaCodeOverride`, recomputes size from bbox, runs the overlap check, sets the
  badge, fires `onChanged`.
- `[RelayCommand] ResetToTemplate` — clears the override (reuses #541 mechanics).

### 4. Live preview — reuse `GdsPolygonRenderer.RasterizeToBitmap(previewData, w, h)`
bound to an `Image` next to the editor.

### 5. Size recompute + overlap
- New size = bbox width/height (µm) → `Component.WidthMicrometers/HeightMicrometers`.
- Overlap: after resize, check other components via the existing
  `DesignCanvasViewModel.CanPlaceComponent`; collect overlapping ones → visual highlight
  + status warning. **Non-blocking** (deliberate override).
- Pins/S-matrix unchanged. If the preview's port count differs from the component's
  pins, show a hint.

### 6. Persistence — `NazcaCodeOverride` (PIR DTO)
- Add `RawCode: string?` and the recomputed `OverrideWidthMicrometers` /
  `OverrideHeightMicrometers`.
- `.lun` round-trip preserves code + size. The canvas thumbnail re-renders the raw code
  on demand (same async/cache path as today's param-based preview). Old `.lun` files
  without the field load cleanly (no override).

## Error handling

- Bad/infinite code → subprocess timeout + kill → `Fail(...)` result → red error text in
  the dialog, Apply disabled. The app never hangs or crashes.
- Missing python/nazca → existing discovery + clear "could not start python" message.
- Overlap after resize → warning, not a hard failure.

## Testing

- **Python:** raw-code mode — valid code → JSON + bbox; invalid code → `success:false`
  + error (no crash); infinite loop → timeout.
- **Service:** `RenderRawCodeAsync` success / failure / timeout.
- **VM:** Run sets Valid/Error; Apply persists `RawCode` + recomputes size; overlap flag
  set when neighbours collide; Reset clears.
- **Persistence:** `.lun` round-trip of `RawCode` + size; legacy file loads as no-override.
- **UI rendering:** manual.

## Out of scope / follow-ups

- S-matrix recompute from the edited geometry → depends on mode-solver (#533/#544).
- Auto-repositioning of overlapping neighbours (we only warn).
- Syntax highlighting in the editor (plain multiline text box for v1).
