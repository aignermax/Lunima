# Release Checklist — Lunima

> Complete the list before every release. Copy it into the release issue, tick items off, and link any failing item to a follow-up issue.
>
> Verification: `auto` = covered by a named test; `manual` = needs a human with the UI. Unmarked items default to `manual`.

## Maintenance rule

Every PR that adds, removes, or materially changes a user-facing feature must update this file. Reviewers should check that the relevant checkbox is present and the expected result still matches the merged behavior.

## How to use at release time

1. Open a release issue / draft PR and paste the checklist below into it.
2. Walk through every manual item in a fresh build on each supported OS (Windows, Linux, macOS) where feasible.
3. Tick an item only after you have observed the expected result.
4. If a manual item fails, file a blocker issue and do not ship the release.
5. For `auto` items, rely on CI unless you touched the related area — then re-run the named tests locally.

---

## 1. Application startup & project lifecycle

- [ ] **auto** Adaptive crossings default to enabled for fresh/unset preferences; saved true/false values survive restart. Restoring the policy on startup does not start routing or repair (`RoutingSettingsPreferencesTests`, `RoutingSettingsPersistenceTests`, `AdaptiveCrossingToggleTests`).

- [ ] Reopen the 4-bit adder over an existing design, then switch Logic/System repeatedly: unchanged hierarchy subtrees stay intact, replaced nodes release model subscriptions, and removed die surfaces release cached parts even when old model parent links remain. `(auto: HierarchyLifecycleTests, DieSurfaceLifetimeTests, RepeatedExampleLoadTests [Slow])`
- [ ] **auto** Component previews recover after activating a working Python environment without restarting. A shared localized library hint opens Python settings and shows failure details; it clears on recovery. Old in-flight failures cannot block retries, and disk-cache reads retain no completed work (`PreviewEnvironmentRecoveryTests`, `PreviewRecoveryHintTests`).

- [ ] Windows installed-app update uses basic MSI progress (UAC only), preserves its registered install directory, waits for app exit, and relaunches only after installer success. Canceling the unsaved-change prompt launches no updater; portable copies retain manual installation. `(auto: WindowsInstallerWorkflowTests, WindowsUpdaterExecutionTests, UpdateViewModelTests)`
- [ ] Two rotated SiN Edge Couplers connected at their left-facing o2 ports route around the target's left side, entering opposite its outward normal. Bend and Auto reject wrong-way S primitives; ordinary recalculation repairs unedited cached generated arcs, while manual edits and valid quantized A* fallbacks remain preserved. `(auto: SiNEdgeCouplerDirectionTests)`

- [ ] System → New die → Create works through the actual flyout button, closes after creation, and can be undone. The Chiplets list contains real dies; a new or legacy single-chip design shows Die 1 rather than its logic groups. `(auto: NewDieButtonTests, WorkspaceContextTests)`
- [ ] Routing status identifies crossing insertion, route refinement, and conflict repair while elapsed time continues; speculative repair searches do not increment completed connections or retain the initial complex-search label. Cancelled/finished passes ignore late phase updates. `(auto: RoutingPhaseProgressTests, RoutingOrchestratorProgressTests)`

- [ ] Start Lunima without a command-line file → Home screen appears with recent projects and example tiles. `(manual)`
- [ ] Open an example project from the Home screen → design loads and canvas is usable; gate groups sit apart with their pins on their bodies. `(auto: HomeExamplesTests, LogicExamplesLayoutTests)`
- [ ] Home guided tours: first steps (place → connect → simulate), "Watch it compute" (Counter), and "Run a program on your chip" (4-bit adder → ISA playground → photonic multiply) each start from the Home card and advance only on the real user actions. `(auto: TutorialViewModelTests, WatchComputeTourViewModelTests, RunProgramTourViewModelTests)`
- [ ] Home "Learn Lunima" → "Your first gate" (second entry, before "Watch it compute") opens the gate kit (four loose, wired NAND parts) and walks 7 steps: group → Make gate… → Simulate → pick NAND (automatic threshold) → Save as gate → Open in Logic + Build; every step advances on the real action. `(auto: FirstGateTourViewModelTests, HomeLearnTourListTests, MakeGateScreenshotTests)`
- [ ] Golden-design regression gate — every example in `UnitTests/Regression/golden/manifest.json` loads, re-routes, passes DRC-lite, and matches its pinned simulation reference. `(auto: GoldenDesignTests)`
- [ ] Create a new project → an empty canvas with the chosen process / playground is shown. `(auto: FileOperationsProjectLifecycleTests)`
- [ ] Reopen the last project on startup → previous file opens automatically if preference is enabled. `(auto: HomeReopenLastProjectTests)`
- [ ] Save a design as `.lun`, close, and reopen → layout, components, and settings are restored. `(auto: FileOperationsProjectLifecycleTests)`
- [ ] **Save As** writes a new file and makes it the current document (title + later Save target switch to it, standard Save-As semantics); the typed file name arrives complete (first keystroke not lost). `(manual)`
- [ ] Dirty-state marker (`*`) appears in title bar after an edit and clears on save. `(auto: WindowTitleTests)`
- [ ] Check for update banner appears when a newer release is available. `(manual)`
- [ ] Settings window opens from toolbar and shows all settings pages. `(manual)`
- [ ] Settings → Chip Size: legacy single-chip designs can still apply root dimensions; designs with explicit dies disable the root editor and Apply, directing users to System die properties. Entering a group on a die keeps Apply disabled; returning to a legacy design restores it. Loading saved root dimensions never resizes explicit dies. `(auto: ChipSizeScopeTests; manual: disabled controls and localized explanation)`
- [ ] All main-toolbar buttons have the same height (Import / Export / Tools / AI / Settings match New / Open / Save). `(auto: ToolbarButtonHeightTests)`

## 2. Localization & accessibility

- [ ] Switch UI language in Settings → all menus, tooltips, and status text update without restart. `(auto: LocalizeExtensionLiveSwitchTests)`
- [ ] Status-bar messages re-translate after a live language switch. `(auto: MainViewModelLocalizationTests)`
- [ ] Discarded shell ViewModels and their canvases remain collectible despite process-wide localization subscriptions; live instances still update when the language changes. `(auto: LocalizationViewModelLifetimeTests, MainViewModelLocalizationTests)`
- [ ] Export outputs (scripts, GDS params) use invariant culture for numeric formatting. `(auto: NazcaExportCultureInvarianceTests)`

## 3. Design canvas

- [ ] Pan and zoom the canvas with mouse wheel / trackpad. `(auto: ZoomToFitTests)`
- [ ] Zoom to fit shows the whole design centered in the viewport. `(auto: ZoomToFitTests)`
- [ ] Select, drag, rotate, and delete selected components. `(auto: RotateComponentCommandTests, DeleteComponentCommandTests)`
- [ ] Box-select multiple components and move them as a group. `(auto: BoxSelectionSyncTests)`
- [ ] Undo/redo covers placement, move, rotate, delete, and route edits. `(auto: UndoRedoIntegrationTests)`
- [ ] Canvas context menu offers Edit, Copy, Paste, Group, Make gate…, Ungroup, Rename, Save as Prefab, Delete; "Make gate…" on loose parts groups them first, then opens the Make-gate steps. `(auto: CanvasContextMenuComponentSettingsTests; manual for Make gate…)`
- [ ] Grid snap settings (from Settings) affect placement and movement. `(auto: GridSnapSettingsTests)`
- [ ] Alignment guides appear when dragging a component near another. `(auto: AlignmentGuideViewModelTests)`
- [ ] Probe mode opens the mode-probe flyout at the clicked location. `(manual)`
- [ ] Cut mode / scissors tool splits a waveguide. `(manual)`
- [ ] Transient mode hides the CW power-flow overlay and shows laser on/off controls. `(auto: DesignCanvasSimulationModeActiveTests)`

## 4. Component library & PDK management

- [ ] Component library loads bundled components grouped by category. `(auto: ComponentLibraryViewModelTests)`
- [ ] Search text filters the component library. `(auto: ComponentLibraryViewModelTests)`
- [ ] PDK enable/disable toggles filter the visible components. `(auto: PdkManagerViewModelTests)`
- [ ] Drag a component from the library and drop it onto the canvas → instance is placed. `(manual)`
- [ ] Load a custom PDK JSON from the Tools flyout → new components appear in the library. `(auto: PdkLoaderTests)`
- [ ] Tools flyout: "Compute Modes for Waveguide…" and "Check PDKs against Python…" show a tooltip saying what they do and for whom (all five languages). `(manual)`
- [ ] Create a custom PDK via the left-panel `+` button → PDK file is written and selectable. `(auto: CreateCustomPdkFlowTests)`
- [ ] Delete a custom PDK and restore it from the trash panel. `(auto: PdkTrashViewModelTests)`
- [ ] Edit a PDK's process / layers / materials (forks bundled PDKs as custom). `(manual)`
- [ ] Process lock prevents mixing incompatible PDKs on the same design. `(auto: PdkManagerProcessLockTests)`
- [ ] Active process label in the status bar reflects the current design process. `(auto: MainViewModelProcessTests)`

## 5. Groups, hierarchy & reusable blocks

- [ ] Create a group from selected components with `Ctrl + G`. `(auto: GroupingWorkflowTests)`
- [ ] A new group's external pins read by direction — IN1, IN2… (west, top to bottom), OUT1… (east), N…/S… (north/south), E… (electrical) — while saved files keep their pin names. `(auto: GroupPinNamerTests)`
- [ ] The right panel's gate section (truth table) appears only while exactly one group is selected; nothing renders otherwise. `(auto: TruthTablePanelRenderTests)`
- [ ] "Make gate…" in the gate section: role chips (in/out/bias/–) with suggested roles and short names, Simulate shows power bars, picking NAND/AND/OR/NOR/XOR/NOT moves the threshold midway between the levels with the margin in dB (overlap warning for a wrong function, manual override + "Use suggested"), Save as gate renames the pins (undoable), Save to library adds it under Saved Groups, then "Open in Logic ›" opens the Logic tab; (?) help animates roles and threshold. `(auto: MakeGateViewModelTests, GatePinRoleSuggesterTests, GateThresholdAdvisorTests, MakeGateScreenshotTests)`
- [ ] Plain truth-table extraction shows the automatic threshold suggestion (largest power step, margin in dB) with "Use" and an "Open in Logic ›" link. `(auto: GateLinkingTests)`
- [ ] Ungroup a group with `Ctrl + Shift + G`. `(auto: GroupingWorkflowTests)`
- [ ] Rename a group via context menu and see the change in the Hierarchy panel. `(auto: HierarchyRenameTests)`
- [ ] Save a group as a prefab → it appears in the Saved Groups list. `(auto: SaveGroupAsPrefabCommandTests)`
- [ ] Enter group edit mode, edit internals, and exit without losing connections. `(auto: GroupEditModeTests, GroupEditModeIntegrationTests)`
- [ ] Nested groups render and simulate correctly. `(auto: ComponentGroupSMatrixBuilderTests)`
- [ ] Hierarchy panel shows the design tree and selection syncs with the canvas. `(auto: HierarchyPanelViewModelTests)`

### Dies (multi-die assemblies)

- [ ] In Layout, click an empty area of each of two dies with different processes: the library follows that die without selecting or moving its frame. Hover does not switch it. Arming and repeatedly placing a component retains that die's library. `(auto: DieLibraryContextTests)`
- [ ] Arm a Layout-only component, then switch to System: Select mode is active and dies can be moved. A permitted edge coupler stays armed and placeable. `(auto: WorkspaceLibraryPlacementTests)`

- [ ] A single-chip design (every older file) shows its chip as "Die 1": a die frame with its size and the design process. In System a click on the chip selects Die 1 (die properties), double-click and F frame it; the first edit makes it a real die (one Ctrl+Z restores the single chip). The model, file and exports stay single-die until then. `(auto: ChipDieTests, DieCommandTests, DiePersistenceTests, UiFlowDieWorkspaceRulesTests)`
- [ ] System workspace → Chiplets → "New die": pick a process + size preset; the chip becomes "Die 1" and the new die appears to the right with its own process-coloured frame, name, size label and edge zone; one Ctrl+Z restores the single chip. `(auto: DieCommandTests, DieViewModelTests, ChipletAssemblyScreenshotTests)`
- [ ] Selecting a die (Chiplets row or its frame) shows its properties on the right — name, size presets per process + custom, process, PDK checkboxes, edge zone, Zoom to die/Delete — every edit undoable; the library and PDK list only offer what the die's process and PDKs allow. `(auto: DieViewModelTests)`
- [ ] A die is a work surface: in Layout, parts (and saved groups) dropped on a die join it without opening anything; they are selected, moved (they stay on their die), rotated, grouped, deleted and wired right there; dropping between dies places nothing and says why; no red preview over a free spot on a die. In System, double-click on a die (or "Zoom to die") frames it, never enters it. `(auto: DieSurfaceTests, DieSurfaceCommandTests, DieCommandTests, UiFlowDieWorkSurfaceTests)`
- [ ] Outside System (Layout/Optics/Logic) a die is background: a press on its empty area draws a marquee, it is never dragged, deleted or edited there, double-click on it does nothing special, its properties show read-only only when the die itself is selected (a selected part keeps the panel). F frames the content of the current die (selection, cursor, last worked); in System F frames all dies. `(auto: InertDieTests, DieZoomToFitTests, UiFlowDieWorkspaceRulesTests)`
- [ ] Dies are never grouped (Create group disabled with the reason); a die's context menu offers no Edit component, Ungroup, Rename group or Make gate; the hierarchy lists dies with the chip icon, "Die · process" and the process colour dot. `(auto: DieGroupAndMenuRulesTests, HierarchyDieNodeTests)`
- [ ] Changing a populated die process previews incompatible component names and affected connection endpoints. Cancel changes nothing; confirm preserves compatible/unknown-source parts and nested groups, removes incompatible parts and incident live/frozen wires, and one Undo restores all (including legacy chip promotion). Check English/German confirmation buttons. `(auto: DieProcessChangeTests, DieProcessConfirmationTests)`
- [ ] Copy/paste of a die lands it in a free spot next to the others with its parts and wires (one undo step); a die stuck in an overlap can always be moved out. Releasing a moved die is instant (the carrier routing grid rebuilds in the background). Right-click opens the canvas menu on the first click, also with a pixel of jitter or after a middle-button pan. `(auto: DieCopyPasteTests, DieDropResponsivenessTests, UiFlowCanvasContextMenuTests)`
- [ ] Moving a die moves everything on it rigidly: the routes on it are identical afterwards (nothing re-routes); only die-to-die links re-evaluate. Routes stay inside their die. `(auto: DieSurfaceTests, DieWiringPolicyTests, UiFlowDieWorkSurfaceTests)`
- [ ] Wiring across dies: two facing edge couplers become a die-to-die link (the far die aligns, one undo step); any other wire from one die to another is refused with the hint "Chips connect only through edge couplers"; Design Checks flag a legacy cross-die waveguide. `(auto: CrossDieWiringTests, DieWiringPolicyTests, UiFlowDieWorkSurfaceTests)`
- [ ] System workspace: die content is dimmed and locked (clicks select the die; parts, pins and wires cannot be picked); dies are moved, linked and aligned, Run system shows the links with loss. Back in Layout the parts are editable again. `(auto: SystemWorkspaceDieLockTests, UiFlowDieWorkSurfaceTests)`
- [ ] Wire ownership: a wire between K1 and K2 inside group M — drawn outside M — is stored in M (visible when M is entered, moves with M); undo/redo is one step; older files with misplaced wires are re-homed on load. `(auto: WireHomingCommandTests, GroupWireHomingTests, UiFlowDieWorkSurfaceTests)`
- [ ] An edge coupler dropped near a die edge snaps onto it, facet facing out (Alt-drag places freely); one whose facet is not on the edge is a Design Check finding. `(auto: DieEdgeSnapperTests, DieAssemblyCheckerTests)`
- [ ] Dragging a die toward another snaps facing couplers together at 2 µm with a dashed guide and live loss label; on release the link is created (undoable). Selecting the link edits gap (moves the die) and gap medium index. `(auto: DieLinkSnapperTests, DieViewModelTests, UiFlowChipletAssemblyTests)`
- [ ] Old multi-chiplet files open as dies sized to their content; the shipped chiplet examples and "Two Dies - SOI and SiN Edge-Coupler Link" run in System with the link loss shown. `(auto: DiePersistenceTests, TwoDieExampleTests)`

## 6. Routing & connections

- [ ] Unchanged automatic routes tolerate neighbouring padding only inside their own pin corridors, avoiding redundant searches; component bodies and frozen group paths still force rerouting. `(auto: PinCorridorOwnershipTests)`
- [ ] Enabling adaptive crossings in Settings starts one routing pass on the open design without another edit; restoring the preference, opening Settings, or enabling it on an empty canvas does not start a pass. `(auto: AdaptiveCrossingToggleTests)`
- [ ] While a complex route is computing, the status keeps the connection count and advances elapsed seconds; cancellation and completion stop periodic updates and ignore late phase callbacks. `(auto: RoutingOrchestratorProgressTests)`

- [ ] Connect mode draws a waveguide between two pins. `(auto: ViewModels/Canvas/ConnectModeAutoSwitchTests)`
- [ ] A* routing finds a path around obstacles and respects grid ownership. `(manual)`
- [ ] Manual bend handles adjust waveguide shape. `(auto: SegmentShiftCommandTests, BendRadiusCommandTests)`
- [ ] Bend radius cannot be reduced below the active process minimum. `(auto: BendRadiusCommandTests)`
- [ ] Crossing-insertion routes around existing components automatically. `(auto: CrossingInsertionCanvasBinderTests, InsertManualCrossingCommandTests)`
- [ ] Router-placed crossings are temporary: moving a wire end takes its chain out even with adaptive insertion off; user-placed crossings stay. `(auto: TemporaryCrossingTests, AdaptiveCrossingRerouteTests)`
- [ ] Tools → Clean Up Crossings removes every router-placed crossing, re-routes the wires and is undoable. `(auto: CleanUpCrossingsCommandTests)`
- [ ] Cut mode also cuts a wire coming out of a crossing (router- or user-placed): the halves keep their geometry and the neighbouring router chain stays; undoable. Segment handles work on such pieces too. `(auto: CrossingPieceEditingTests, CutToolCandidateComputerTests)`
- [ ] Dragging a crossing in Select mode slides it along one of its two wires (dominant drag direction, dashed guide + ghost); that wire's pieces only stretch/shrink, the other wire re-routes; a moved router crossing becomes user-owned; one undo step. `(auto: UiFlowCrossingAxisDragTests, MoveCrossingAlongAxisCommandTests, CrossingAxisMovePlannerTests)`
- [ ] Crossing axis drag snaps along the free axis: a port lands exactly on a facing pin's X (horizontal drag) / Y (vertical drag) within 5 µm and the dashed pin guide shows; otherwise the centre follows grid snap (G). `(auto: CrossingAxisSnapperTests, UiFlowCrossingFreeDragTests)`
- [ ] Shift frees a crossing drag — held at the press or pressed mid-drag: the crossing moves diagonally like any component (full pin/grid snapping), all four wires re-route on release; one undo step. While dragging a crossing the status bar shows "Shift: move freely"; the (?) shortcut flyout lists it. `(auto: UiFlowCrossingFreeDragTests)`
- [ ] Deleting a crossing (router- or user-placed) keeps the wires through it, merged; undo restores the crossing and its pieces. `(auto: ChainCrossingMaintenanceTests, DeleteCrossingKeepsWiresTests)`
- [ ] A wire that cannot be routed with its pins stays red and is not retried on every recalculation. `(auto: UnroutableWireTests)`
- [ ] Connection style panel changes curve type, width, radius, and freeze state. `(auto: ConnectionRoutingViewModelShapeTests, ConnectionRoutingStyleEffectTests)`
- [ ] Selecting a waveguide shows its insertion-loss breakdown (propagation, bends with radii, junctions, total) in the Properties panel; a tight bend shows more bend loss than a gentle one; the (?) shows light leaking out of a tight bend. `(auto: ConnectionLossViewModelTests, BendLossBreakdownScreenshotTests)`
- [ ] Frozen routes persist through save/load. `(auto: Serialization/LockStatePersistenceTests)`
- [ ] Metal routing (electrical traces) exports with process-defined layers and widths. `(auto: MetalRouting/ElectricalConnectionExportTests)`
- [ ] Design Checks see inside groups at any depth: open "Logic Gate RAM 2x4", run checks → blocked paths inside cells are listed as "Blocked path in group 'CELLx'…" and Next zooms onto them; the groups holding them show a red dashed outline with a "!" badge and the blocked path draws red without entering the group. `(auto: Analysis/Hierarchy/*, ExampleNestedDesignCheckTests, GroupBrokenPathMarkerTests)`
- [ ] Unconnected pins are hints, not issues: hidden by default, the "Show unconnected pins" toggle in the Checks tab shows them (count in brackets) after the real problems. `(auto: DesignChecksPanelNestedTests, DesignValidationMenuCommandTests)`

## 7. Simulation & S-matrix

- [ ] Run CW simulation → optical power reaches the expected output ports. `(auto: SimulationModeTests, LightCalculation/TransitiveSMatrixCalculatorTests)`
- [ ] Run Transient simulation → time-domain output appears in the analysis dock. `(auto: TimeDomainSimulation/TimeDomainSimulatorTests)`
- [ ] Toggle simulation mode (CW / Transient) from the toolbar combo box. `(auto: SimulationModeTests)`
- [ ] Simulation converges for looped circuits or emits a readable non-convergence message. `(auto: NonConvergentCircuitMessageFormatterTests)`
- [ ] Passivity warnings surface when components produce unphysical gain. `(auto: LightCalculation/SingleHopPassivityCheckerTests)`
- [ ] Run is the simulation on/off toggle in Optics (CW) and System: first press (or L / P) simulates and shows the overlay, Run then looks pressed with "Simulation on" + stop icon and tooltip "Turn simulation off (L, P)"; the next press turns it off. No separate eye button. Optics in Transient starts a transient run on every press; Layout (Run checks) and Logic (Build logic / Logic step) never toggle. `(auto: MainViewModelWorkspaceTests, WorkspaceViewModelTests)`
- [ ] Live simulation: with the simulation on, every design change (connect in Connect mode, delete, move, rotate, parameter edit, group/ungroup, undo/redo, crossing edits) re-runs it once routing has finished — a new wire lights up instead of "no signal", the simulation stays on, the canvas stays responsive (debounced, off the UI thread, a stale run is cancelled). Removing the last light source turns it off with "No light sources found". Opening another design / example / recent project keeps it on and re-simulates the new design; File → New turns it off. `(auto: MainViewModelLiveSimulationTests, LiveSimulationSchedulerTests, UiFlowLiveSimulationConnectTests)`
- [ ] Workspace switch (Layout / Optics / Logic / System) next to Run: one click or Ctrl+1–4 switches; Run's label names the action ("Run checks" / "Simulate" / "Build logic", then "Logic step" once a network with registers is built / "Run system"); the first three presses of L per action show a short "L — <action>" hint under Run that hides itself; the CW/Transient choice appears only in Optics. `(auto: WorkspaceViewModelTests, UiFlowWorkspaceSwitchTests)`
- [ ] Run per workspace: Layout opens Design Checks with results; Optics simulates (CW overlay or Transient tab); Logic builds the network, then each Run steps the clock; System runs the chiplet checks + whole-system CW and shows the chiplet/link strip (process per chiplet, link loss in dB). `(auto: MainViewModelWorkspaceTests)`
- [ ] Dock tabs follow the workspace; "⋯ → Show all tabs" in the dock header brings every tab back; a tab opened by a tour/menu is shown even outside its workspace. `(auto: WorkspaceViewModelTests)`
- [ ] Workspace is saved in the .lun and restored on open; files without one open by content: chiplet links → System, gate groups → Logic, light source → Optics, otherwise Layout (check the Two Chiplets, Logic Gate and MZI examples). `(auto: WorkspaceDefaultsTests, MainViewModelWorkspaceTests)`
- [ ] (?) beside the workspace switch explains the four workspaces in a few sentences each, plus which tools move with the workspace. `(manual)`
- [ ] The active workspace is unmistakable but calm: its hue (Layout steel, Optics cyan, Logic violet, System copper) marks the switch segment, a 2 px line under the toolbar, a faint canvas tint + edge, the status-bar chip and Run; waveguides and power-flow labels stay readable; switching cross-fades (instant with `LUNIMA_REDUCE_MOTION=1`). `(auto: WorkspaceAccentTokenTests, WorkspaceShellRenderTests)`
- [ ] Left rail follows the workspace tool set: Layout shows every section and the full library; Optics lists **instruments only** (couplers / sources, detectors, ONA analyzer) under "Instruments" with a "Place circuit parts in Layout ›" link; Logic shows Gates + Hierarchy only (saved gates, starter gate examples, "Make gate from selection", same link); System shows Chiplets, Hierarchy and PDKs (link at the bottom). The link (and any "show all components" button) switches to Layout with the library open; each workspace remembers the last picked section. `(auto: WorkspaceLeftRailTests, WorkspaceContextTests, GatesPanelViewModelTests, UiFlowWorkspaceToolSetsTests)`
- [ ] No placement backdoor: in Optics an MMI / saved group cannot be armed or dropped, in Logic only gates place, in System nothing — each refusal says "… is placed in Layout … (Ctrl+1)" in the status bar; a template armed in Layout is disarmed on switching. "Place a light source" from Logic/System switches to Optics first. `(auto: WorkspaceLibraryPlacementTests, WorkspaceToolSetTests)`
- [ ] Canvas context menu per workspace: "Edit Component…" only on a real component in Layout and Optics (never on groups / gates); Logic offers "Show in Layout" and "Edit gate…" on an existing gate ("Make gate…" otherwise). `(auto: CanvasContextMenuViewModelTests)`
- [ ] Truth Table / Make gate shows in the properties panel only in Logic and System (exactly one group selected); "Make gate…" from Layout/Optics switches to Logic. Select / Connect / Delete are in the toolbar everywhere; Probe is in the toolbar in Layout and Optics, Cut in Layout / Logic / System — elsewhere each sits under "More tools" (⋯), and X keeps reaching Cut in every workspace. `(auto: WorkspaceContextTests, WorkspacePresentationTests, UiFlowWorkspaceLayoutOpticsTests)`
- [ ] Layout vs Optics on the Mach-Zehnder example: Optics (Run on) shows the light, recedes component bodies (sources/detectors keep full strength and teal names) and opens the dock on Transient; switching to Layout turns the light off, draws bodies at full strength, marks the design-check findings after Run (L) (warning diamonds, info rings on open pins) and opens Design Checks; back in Optics the light returns. The layers icon (Overlays) toggles light / check markers, and each workspace remembers the choice. `(auto: WorkspacePresentationTests, WorkspaceOverlayMemoryTests, UiFlowWorkspaceLayoutOpticsTests)`
- [ ] Logic opens on the gate view: gate symbols (NAND/AND/OR/XOR/NOT, flip-flops for registers, triangles for copies, boxes for anything else) at their physical gate positions, nets as clean wires labelled with signal names, input/output flags, legend top-left. After Run (L) the nets colour by live level (green 1, grey 0, dashed = no level yet) and follow input toggles. Check Half Adder, Counter 2-bit and ALU + RAM + ACC (overview stays calm; zoom in for symbols and labels). `(auto: GateViewGraphExtractorTests, GateViewLayoutTests, GateViewScreenshotTests)`
- [ ] Gates | Physical switch next to Run (and V) flips gate view ↔ physical MMIs/waveguides; picking Connect / Cut / Probe in the gate view switches to Physical. Clicking a gate selects its group (Properties follow, Layout shows the same selection); dragging moves the whole gate; double-click jumps to Layout centred on it. Entering Logic on the CPU example stays responsive. `(auto: GateViewViewModelTests, UiFlowGateViewTests, KeyboardHandlerTests)`
- [ ] Truth Table / Make gate shows in the properties panel only in Logic and System (exactly one group selected); "Make gate…" from Layout/Optics switches to Logic. Probe shows only in Layout and Optics; Select / Connect / Delete / Cut stay everywhere. `(auto: WorkspaceContextTests)`

## 8. Parameter sweep

- [ ] Select a component with a slider, set start/end/steps, and run a parameter sweep. `(auto: ParameterSweeperTests)`
- [ ] Sweep results update and can be exported to CSV. `(auto: SweepCsvExporterTests)`
- [ ] Sweep respects slider min/max bounds from the selected template. `(auto: ParameterSweeperTests)`

## 9. Analysis panels

- [ ] Transient trace visibility toggles preserve chart zoom and existing sample buffers. Tracking long transient or spectrum traces remains responsive; exact samples, interpolated values, duplicate coordinates, and unsorted data retain their tracker behavior. `(auto: TimeTraceVisibilityTests, XTrackingLineSeriesTests; manual: large transient result)`

- [ ] ONA wavelength sweep panel measures transmission across a wavelength range. `(auto: OnaAnalysis/OnaAnalyzerSimulationTests)`
- [ ] Wavelength spectrum panel plots transmission curves. `(auto: WavelengthSpectrum/WavelengthSpectrumViewModelTests)`
- [ ] Spectrum tab "Coherent interference" toggle: EBeam MZI sweep shows fringes (FSR = λ²/(n_g·ΔL)) when on, none when off; the flag round-trips through .lun save/load; (?) help shows the ΔL→fringes animation. `(auto: Integration/EBeamMziSpectrumToggleTests, Persistence/CoherentPropagationPhasePersistenceTests)`
- [ ] Time-domain / transient analysis panel plots signals over time. `(auto: TimeDomainSimulation/TimeDomainSimulatorTests)`
- [ ] Transient initialization shares one peak scan per trace between legend and table, preserving signal filtering, stable peak ordering, and first-peak time; plot buffers are allocated to their final sample count. `(auto: TimeTracePeakTests, TimeTracePlotBuilderTests; manual: large transient result)`
- [ ] Eye diagram panel opens and renders eye metrics / BER. `(auto: EyeDiagram/EyeDiagramBuilderTests, BerEstimatorTests)`
- [ ] Monte Carlo panel runs fabrication-variance sweeps. `(auto: MonteCarloAnalysis/MonteCarloRunnerTests)`
- [ ] Circuit optimization panel suggests and applies component variants. `(auto: CircuitOptimization/CircuitOptimizerTests)`
- [ ] Design validation panel lists and navigates to rule violations. `(auto: DesignValidatorTests, DesignValidationIntegrationTests)`
- [ ] Component dimension diagnostics detect out-of-bounds geometry. `(auto: ComponentDimensionValidatorTests)`
- [ ] Layout compression panel shrinks the design within constraints. `(auto: LayoutCompressorTests)`
- [ ] Analysis output panel shows designated outputs and persists them. `(auto: AnalysisOutput/AnalysisOutputPanelViewModelTests)`
- [ ] Empty optical tabs explain themselves: with no light source, Spectrum / Monte Carlo / Transient / Eye show one sentence + "Place a light source" (arms placement of a grating/edge coupler); with a source but no picked output, Transient / Eye offer "Pick on canvas". `(auto: AnalysisReadinessViewModelTests, MainViewModelWorkspaceTests)`
- [ ] Logic panel lives in the analysis dock's Logic tab: builds the logic network of the loaded design, toggles inputs, steps/runs the clock, and shows timeline + waveform lanes. `(auto: WatchComputeTourViewModelTests, WatchTourCounterExampleTests)`
- [ ] Logic tab empty state lists the groups that are not gates yet, each with "Make gate…" that selects the group and opens the Make-gate steps. `(auto: GateLinkingTests)`
- [ ] Logic tab is laid out for the wide dock: one toolbar row (Build · Step clock / Run / cadence / Reset · Play / Prev / Next / Back to live · status · critical path (?)) above side-by-side columns (inputs | outputs + registers | timeline | waveforms | fan-out warnings), each column scrolling on its own; at a 900 px dock the toolbar wraps and the columns still fit, narrower docks scroll the columns sideways instead of clipping. `(auto: LogicTabHorizontalLayoutScreenshotTests)`

## 10. Component-specific tools & properties

- [ ] Selected-component properties panel shows name, position, size, and type-specific editors. `(auto: Properties/SelectionPropertiesPanelIntegrationTests)`
- [ ] Light-source editor toggles input power and wavelength. `(manual)`
- [ ] ONA analyzer editor configures sweep range. `(auto: Properties/OnaAnalyzerEditor)`
- [ ] Slider editor appears for components with tuning parameters. `(auto: Properties/SliderEditor)`
- [ ] Parametric formula editors accept and evaluate user equations. `(auto: Properties/ParametricParametersEditorTests)`
- [ ] Component settings dialog opens, shows S-matrix / display / import / FDTD tabs, and saves changes. `(auto: ComponentSettingsDialogViewModelTests)`
- [ ] Port mapping dialog remaps imported S-parameter ports to internal pins. `(auto: PortMappingDialogIntegrationTests)`
- [ ] S-matrix override applies to all instances or a single instance. `(auto: SMatrixOverrideApplicatorTests)`
- [ ] Unlock all elements from the Tools flyout re-enables editing on locked items. `(manual)`

## 11. Mode solver & FDTD

- [ ] Mode solver dialog opens from Tools and runs against the configured Python environment. `(auto: Solvers/ModeSolver/ModeSolverViewModelTests)`
- [ ] Mode-probe flyout shows effective index and mode profile for a clicked waveguide; the header (?) help explains n_eff, n_g, MFD and fiber coupling with the guided-crest animation. `(auto: UI/Issue1486ModeProbeHelpFlyoutScreenshotTests)`
- [ ] FDTD backend selection dialog lists available environments and toggles Tidy3D settings. `(manual)`
- [ ] Docker setup dialog detects or installs a Python/Nazca environment. `(manual)`
- [ ] Python environment manager installs/updates interpreters and the Nazca package. `(auto: PythonEnvironmentManager/PythonEnvironmentManagerViewModelTests)`

## 12. Import

- [ ] Import a GDS layout via the library panel `GDS` button → pins appear on the imported cell. `(auto: Services/GdsImport/GdsImportServiceTests)`
- [ ] GDS import dialog previews the cell hierarchy and lets the user pick placement. `(auto: Services/GdsImport/GdsImportDialogViewModelTests)`
- [ ] Importing a finished layout with the dialog defaults keeps every route frozen and exactly as drawn (core + cladding polygons, mirrored cells mirrored), flat on the canvas; save/reopen keeps it frozen. `(auto: Integration/GdsLosslessFrozenImportTests)`
- [ ] Selecting one imported waveguide → "Re-route selected" re-routes only that waveguide; Ctrl+Z brings the drawn route back. `(auto: Integration/GdsLosslessFrozenImportTests)`
- [ ] Exporting an untouched import writes the drawn polygons verbatim and mirrored cells with `flip=True`. `(auto: Integration/GdsLosslessFrozenImportTests)`
- [ ] Imported components are linked to the design scope (saved in `.lun`). `(auto: Services/GdsImport/DesignScopedGdsComponentServiceTests)`
- [ ] Import a PDK JSON and register its components. `(auto: ViewModels/PdkImport/PdkImportWizardViewModelTests)`
- [ ] Round-trip: export a layout, import it back, and compare geometry. `(auto: Services/GdsImport/GdsRoundTripImportTests)`

## 13. Export

- [ ] Nazca Python export produces a runnable script with correct coordinates. `(auto: CodeExporter/SimpleNazcaExporterTests)`
- [ ] A die assembly exports one cell per die (die-local coordinates, outline on layer 999) under an assembly top cell; Export → "Nazca → one file per die" writes one script/GDS per die; single-chip exports are unchanged. `(auto: DieAssemblyExportTests)`
- [ ] GDS export writes a valid GDS file with layers, pins, and cells. `(auto: Export/GdsExportIntegrationTests)`
- [ ] SAX export produces a SAX-compatible Python netlist/script. `(auto: Export/SaxScriptExecutionTests)`
- [ ] gdsfactory export generates a YAML netlist and Python module. `(auto: Export/GdsFactoryExport/GdsFactoryExporterTests, Export/Netlist/GdsFactoryYamlNetlistWriterTests)`
- [ ] PhotonTorch export dialog configures and writes an export script. `(auto: Export/PhotonTorchExporterTests)`
- [ ] Verilog-A export produces a SPICE-compatible model. `(auto: Export/VerilogAExporterTests, Export/VerilogASimulationTests)`
- [ ] Export menu → "Netlist (YAML)" saves the gdsfactory YAML netlist to a .yml file; the status bar confirms. There is no Netlist tab in the analysis dock any more (the Export menu is the single entry point). `(auto: Export/ExportMenuViewModelTests, Export/Netlist/NetlistViewModelTests)`
- [ ] Export menu → "Netlist (YAML) → Clipboard" copies the netlist without a file dialog; the menu stays open while copying, closes only after success, and the status bar confirms. Clipboard failures keep the menu open and show an error. `(auto: Export/ExportMenuViewModelTests, NetlistClipboardMenuTests)`
- [ ] Export guards warn when the design contains broken routes or missing PDKs. `(auto: Export/GdsExportGuardTests, Export/NazcaExportSkipsBrokenConnectionsTests)`
- [ ] Foundry environment selection resolves a working Python/Nazca interpreter. `(auto: Export/GdsExportEnvironmentSelectionTests, Export/ProcessLaunchFactoryTests)`
- [ ] PDK resolution check validates all Nazca functions resolve for export. `(auto: Export/PdkResolution/PdkFunctionResolutionServiceTests)`
- [ ] Export → "Check for openEBL…" exports the design to GDS and reports pass/fail per check (submission + verification) with typed errors; a missing Python toolchain shows the pip hint with a copy button. `(auto: Export/OpenEbl/OpenEblCheckViewModelTests, Export/OpenEbl/OpenEblSubmissionCheckerTests)`
- [ ] openEBL check against a real toolchain passes on a valid EBeam design and fails on an oversized die. `(manual)`

## 14. Layers & geometry

- [ ] Layer list is shown and configurable per PDK process. `(manual)`
- [ ] GDS layer mapping in exported files matches the active process. `(auto: Export/SimpleNazcaExporterConnectionSourceLayerTests)`
- [ ] PDK offset editor calibrates component pin offsets against a reference GDS. `(auto: PdkOffsetEditorViewModelTests)`
- [ ] Component outline polygons render and export correctly. `(auto: Controls/Canvas/ComponentPreview/GdsPolygonRendererTests)`
- [ ] Waveguide companion layers: a demo-PDK design shows a translucent 8 µm trench band (2/0) around every routed waveguide, the band layer can be hidden in the Imported Layers panel, the exported GDS carries the band polygons along straights and arcs, and the Process Management window lists/edits the bands. `(auto: SimpleNazcaExporterCompanionLayerTests, CompanionLayerGdsExportTests, WaveguideCompanionBandScreenshotTests, ProcessManagementCompanionLayerTests; manual: canvas look)`
- [ ] A process `KeepOut` band makes Design Checks flag two wires whose keep-out bands overlap (not at their crossing); shipped examples gain no findings. `(auto: WaveguideSpacingKeepOutTests, ExampleCompanionKeepOutDrcTests)`
- [ ] GDS coordinate comparison tool reports differences between two exports. `(manual)`

## 15. AI design assistant

- [ ] AI assistant panel accepts natural-language requests. `(manual)`
- [ ] AI tools registry exposes fit-to-view and other canvas operations. `(auto: AI/FitToViewToolTests, AI/AiToolRegistryTests)`
- [ ] AI grid service applies tool results to the design model. `(auto: AI/AiGridServiceTests)`
- [ ] AI settings page configures the API key / endpoint. `(manual)`
- [ ] Agent bridge (MCP): off by default; Settings → Agent bridge (MCP) → **Connect Claude Code → Connect** turns it on ("Listening on http://127.0.0.1:39271/mcp") and registers `lunima` in Claude Code (`claude mcp get lunima`); without the CLI the page says so and shows the command. No token by default; with "Require access token" on, requests without it get 401 and the command carries the header. `list_connections` lists the blocked routes, an agent `move_component` is reverted by Ctrl+Z. `(auto: AgentBridge/*Tests; manual: real Claude Code connect on Windows, Linux, macOS)`
- [ ] Claude Code at install/first start: Windows MSI Custom Setup shows "Connect Claude Code" (checked) and after install `claude mcp get lunima` succeeds (when Claude Code is installed); uninstall removes it. macOS .dmg / Linux portable: first start with the CLI installed shows the "Connect Claude Code to Lunima?" bar; "Not now" hides it until the next version. `(manual)`
- [ ] Agent repair tools on *Logic Gate Counter 2-bit*: `list_cut_candidates`, `insert_crossing` (+ undo), `move_crossing_along_axis`, `shift_segment`, `suggest_fixes` → `apply_suggestion`, `save_design` round trip. `(auto: AgentBridgeRepairExampleTests, Category=Slow)`

## 16. Component registry (online)

- [ ] Open Component Registry browser from toolbar or library header. `(manual)`
- [ ] Registry browser fetches the public index and displays components. `(auto: ComponentRegistry/RegistryBrowser/RegistryActiveProcessWiringTests)`
- [ ] Download and register a registry component into the local library. `(manual)`

## 17. Error console, diagnostics & help

- [ ] Error console opens with `F12`, lists errors/warnings, and copies to clipboard. `(auto: ErrorConsoleViewModelTests)`
- [ ] Error badge counts update after validation runs. `(manual)`
- [ ] Routing diagnostics panel highlights blocked paths and detours. `(manual)`
- [ ] GDS coordinate comparison view loads two coordinate JSON exports side by side. `(manual)`
- [ ] Help flyouts for transient and eye workflows appear on first use. `(manual)`
- [ ] Monte Carlo tab (?) help explains fabrication variance and yield; the scatter/histogram animation loops. `(manual)`
- [ ] PDK JSON help page opens from the Tools flyout. `(manual)`
- [ ] ISA playground opens from the Tools flyout; Assemble + Step runs count-to-5 to ACC = 5; Reset restores PC = 0. `(manual)`
- [ ] ISA playground hint offers Open suitable example, preserves unsaved-design Save/Discard/Cancel, then offers Build logic network as an explicit next step. Missing examples report a readable message; English/German buttons fit the default window. `(auto: IsaExampleActionTests, IsaPlaygroundExampleActionTests, LocalizationCompletenessTests)`
- [ ] ISA playground editor shows line numbers and highlights the current step in place (green bar follows Step); no second program listing beside the machine state. `(auto: IsaProgramEditorTests)`
- [ ] ISA playground Run auto-steps count-to-5 to "halted after N steps" with ACC = 5; Stop mid-run freezes PC and re-enables Step. `(manual)`
- [ ] ISA playground "Compute ADD on the photonic chip" toggle: disabled with a hint before a build; after building the 4-bit adder example in the Logic tab it runs count-to-5 to ACC = 5 and the status names the photonic adder. `(manual)`
- [ ] ISA playground photonic ADD: each ADD step shows operands and result in binary with the light-travel time in ps (e.g. `0001 + 0001 = 0010`); the header names the photonic adder while the toggle is on, the golden model otherwise. `(manual)`

## 18. Cross-platform sanity

- [ ] All export scripts run on the named interpreter across Windows, Linux, and macOS. `(auto: Export/PythonConfigurationIntegrationTests)`
- [ ] No direct `ProcessStartInfo` / `Process.Start` calls outside sanctioned launchers. `(auto: Architecture/CrossPlatformProcessLaunchTests)`
- [ ] File paths use `Path.Combine` and OS-appropriate separators. `(manual / code review)`

## 19. Performance & stability

- [ ] No `[RelayCommand]` on `MainViewModel` blocks the UI thread longer than 100 ms against the shipped 4-bit-adder example. `(auto: ResponsivenessBudgetTests)`
- [ ] Large design (> 500 components) opens without UI hangs. `(manual)`
- [ ] Memory usage remains stable after repeated open/save cycles. `(manual)`
- [ ] Crash during simulation shows a readable error in the console rather than a hard exit. `(manual)`

---

## Sign-off

- **Tester:** ___________________
- **Build / version:** ___________________
- **Date:** ___________________
- **Blockers:** ___________________
- **Approved for release:** ☐ Yes  ☐ No
