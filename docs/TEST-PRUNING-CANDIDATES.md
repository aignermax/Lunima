# Test pruning candidates

A review list, not a decision. Nothing listed here has been deleted. Each row names a test that
adds little coverage on top of another test, what it costs, and what to do with it if the team
agrees.

- **Durations** are CI wall-clock seconds from a full run of the sharded pipeline
  (`xUnitTests.yaml`, `workflow_dispatch` run 38701480688, commit `4ba7b7ef`).
  Single-run durations are noisy: the first test in a shard that imports nazca, torch or the
  SiEPIC PDK pays a one-time cold-start cost of 5–20 s. Treat the numbers as ±30 %.
- **Heavy** means the test carries `Category=Heavy`, so pull requests already skip it and only
  `dev`/`main` pushes, tags, nightly and manual runs pay for it.
- **Saved** is what removing or merging the test saves in a full run. For a merge it is the
  test's own duration, because the surviving twin already runs the same setup.
- **Risk** is the coverage risk of removing it: low = the twin asserts the same behaviour
  on the same path; med = the twin covers the path, but some assertion or entry point is
  only here.

Sorted by time saved. The 2-bit counter rows (1–3, 5, 6, 11–13) share one cost driver: every
`RecalculateRoutesAsync` on that example spends 5–35 s routing (see *Related finding* at the end).
Loading the example takes under a second.

## Candidates

| # | Test(s) | s | Heavy | Covers | Why it is redundant (twin) | Risk | Recommendation |
|---|---------|---|-------|--------|----------------------------|------|----------------|
| 1 | `AgentBridgeRepairExampleTests.MoveCrossingAlongAxis_MovesAValidStep_AndExplainsARefusal` | 67.9 | yes | `move_crossing_along_axis` tool: one 10 µm slide, undo, a delta-0 refusal | The slide and its undo run the same `MoveCrossingAlongAxisCommand` on the same counter as `CrossingPieceEditingTests.MovingARouterCrossingAlongItsWire_KeepsTheOtherChains_AndUndoRestoresIt` (17 s), which asserts more. Only the JSON mapping and the refusal text are unique. | med | Keep only the refusal call (no routing pass). Optionally cover the JSON mapping on a small hand-built crossing fixture. |
| 2 | `AgentBridgeExampleDesignTests.MoveGroup_ThenUndo_RestoresPositionAndConnectionCount` | 58.8 | yes | `move_component` + `undo` tools on the counter | Same tool contract as `AgentBridgeEditToolsTests.MoveComponent_ThenUndo_RestoresPosition` (<1 s, small fixture). The counter-scale move/undo is covered more strictly by `CrossingUndoTests.MoveUndoRedo_RestoresTheExactCrossingState`. Both go through `MoveComponentCommand`. | low | Delete. |
| 3 | `CleanUpCrossingsCommandTests.CleanUp_WithAdaptiveInsertionOff_LeavesNoRouterCrossingAfterRouting` | 51.9 | yes | Clean-up, one routing pass: no crossing comes back, every wire is routed | `CleanUp_RemovesRouterCrossings_KeepsUserCrossings_AndUndoRestoresThem` (59 s) runs the same clean-up and the same pass on the same scenario (adaptive off) before its undo. | low | Merge: after the twin's first pass, assert "only the user-placed crossing remains" and "every connection has a route". Then delete. |
| 4 | `FourBitAdderManufacturingJourneyTests.Step4_GdsExportAndFoundryDeck_EmptyViolationBaselineHoldsAtScale` | 39.7 | yes | nazca export + CORNERSTONE deck on the 4-bit adder | Same assertions as `FullAdderManufacturingJourneyTests.Step4_GdsExportAndFoundryDeck_EmptyViolationBaselineHolds` (4 s), 11× larger. Both tests note that the export writes no layer the deck inspects, so "0 DRC violations" holds at any scale. | med | Delete, or keep it as an explicit export-time budget (`watch.Elapsed < N`) so it guards scale instead of re-checking an empty deck. |
| 5 | `ChainCrossingMaintenanceTests.MovingAWireEnd_TakesItsChainOut` | 30.5 | yes | Full Adder, adaptive on: moving a wire end dissolves its crossing chain | The dissolution path is asserted with adaptive off by `TemporaryCrossingTests.AdaptiveOff_MovingAWireEnd_DissolvesItsStaleChains`, and the adaptive-on move by row 6's twin. | med | Add "the old crossings are gone" to the adaptive-on counter move (row 6 merge) and delete. This is the only Full Adder instance of the check. |
| 6 | `AdaptiveCrossingRerouteTests.CounterMove_ReroutesOnlyTheDissolvedWires_WithoutAddingCrossings` | 26.1 | yes | Adaptive on, counter group moved 150 µm, one pass: no ordering cascade, no new crossings | `CrossingUndoTests.MoveUndoRedo_RestoresTheExactCrossingState(adaptive: True)` (63.5 s) makes the identical load and 150 µm move (through `MoveComponentCommand` instead of `MoveComponent`). | low | Merge its three assertions into the undo test after the move. Then delete. |
| 7 | `PythonDiscoveryServiceTests.DiscoverPythonWithNazcaAsync_*` (5 tests) + `CheckPythonInstallation_WithSystemCommand_ReturnsCorrectSource` | 13.8 + 1.4 | one | Real interpreter discovery | Each test runs a full discovery. `ReturnsListOfInstallations` only checks the result type. `IncludesActiveVenvIfPresent` asserts `HasNazca`, which `OnlyReturnsInstallationsWithNazca` already guarantees for every entry. `WithSystemCommand` is a subset of `CheckPythonInstallation_WithValidPath_ReturnsInstallationInfo`. | low | Share one `Lazy` discovery result across the class (as `GdsExportServiceTests` does). Delete the two trivial discovery tests and `WithSystemCommand`. Saves ~12 s. |
| 8 | `GdsExportIntegrationTests.ViewModel_ExportScriptToGds_WithGenerateEnabled_AttemptsGdsGeneration` | 10.9 | yes | Real export of `nd.export_gds()` through the ViewModel | When the environment is ready it asserts only `ScriptPath` and a non-empty status. The not-ready branch is covered deterministically by `GdsExportServiceEnvironmentCacheTests.NotReadyProbe_ThenExport_ProbesAgain`. | low | Delete. |
| 9 | OpenEBL checker tests `OpenEblSubmissionCheckerIntegrationTests.EBeamMziExample_Checker_ReportsZeroErrorsOnBothChecks`, `OpenEblAddDropRingCheckerTests.EBeamAddDropRingExample_Checker_ReportsZeroErrorsOnBothChecks` | 5.1 + 5.3 | no | Export + both checks, zero errors, per EBeam example | `OpenEblDialogEndToEndTests` (11.9 / 12.1 s) does the same export and checks for the same examples through the DI-wired dialog. The checker tests additionally assert "no skipped connections, no export warnings". | med | Keep one full-toolchain run per example: move the skipped/warning assertions into the dialog tests and delete the checker tests. The dialog tests are Heavy, so a PR would then not run these examples at all. Alternatively make the dialog tests use a stub checker. |
| 10 | `GdsUserDesignProbeTests.Probe` | 10.3 | yes | Prints user-design import numbers; asserts only exit code 0 | Its class doc calls it a "SCRATCH probe … Will be removed". `GdsUserDesignRoundTripTests.RoundTrip_UserDesign_ExportThenReimport_ExplodesAllSevenComponents` (10.2 s) runs the same fixture with real assertions. | low | Delete. |
| 11 | `TemporaryCrossingTests.UnchangedDesign_KeepsEveryCrossing_AndRunsNoCascade` | 8.8 | no | One pass on the unchanged counter: crossings, blocked count unchanged, no cascade | Same first pass as `UnroutableWireTests.Counter_UnroutableWire_IsRememberedAndNotRetried` (11.1 s), which already asserts no cascade on its second pass. | low | Add the crossing and blocked-count checks after that test's first pass. Then delete. |
| 12 | `UnroutableWireTests.Counter_ExplicitReroute_ForgetsTheWire` | 8.6 | no | `RequestReroute` forgets a remembered unroutable wire | Its setup (load, one pass) is the first half of `Counter_UnroutableWire_IsRememberedAndNotRetried`. | low | Append `RequestReroute` and its two assertions to the end of that test. |
| 13 | `TemporaryCrossingTests.AdaptiveOff_MovingAWireEnd_DissolvesItsStaleChains` | 8.5 | no | Adaptive off: a moved wire end dissolves its stale chains | `CrossingUndoTests.MoveUndoRedo_RestoresTheExactCrossingState(adaptive: False)` (20.1 s) makes the same move and asserts fewer crossings, but not which ones. | low | Add "the stale crossings and their pieces are gone, no cascade" to the undo test after the move. Then delete. Note: rows 5 and 11–13 together leave PRs without a non-Heavy counter move test; keep one of them non-Heavy if that matters. |
| 14 | `PythonConfigurationIntegrationTests.GdsExportViewModel_SearchForPython_PopulatesAvailablePythons`, `…_AutoSelectsFirst` | 4.2 + 4.1 | no | `SearchForPythonAsync` fills the list / selects the first entry | `EndToEnd_DiscoverSelectAndCheck_WorksTogether` (7 s) calls the same `SearchForPythonAsync` and checks each step. | low | Fold their assertions into the end-to-end test. Then delete both. |
| 15 | `SaxScriptExecutionTests.ExportedScript_WavelengthSweep_PopulatesExpectedNumberOfPoints` | 7.3 | yes | `numPoints` reaches the sax script | Asserts only `Transmission.Count == 7`. Most of the time is the jax import. | low | Pass `numPoints: 7` in `GroupWithInternalAndExternalConnections_BuildsAllEdges` and assert the count there. |
| 16 | `GdsExportServiceTests.ExportToGdsAsync_WithValidScriptAndNoPython_ReturnsErrorMessage` | 6.8 | no | Misnamed: on CI it probes the environment and really runs nazca | Its assertions only fire when no environment is ready (the opposite of CI). The not-ready branch is covered by `GdsExportServiceEnvironmentCacheTests.NotReadyProbe_ThenExport_ProbesAgain`. | low | Rewrite it on `InstantEnvironmentGdsExportService(ready: false)` and assert the "GDS generation skipped" status, or delete. |
| 17 | `GdsExportIntegrationTests.Integration_CheckEnvironmentThenExport_WorksCorrectly` | 6.8 | no | Probe, then export | When ready it asserts only `result.ShouldNotBeNull()`. The ready path is covered by `DynamicGdsFilenameIntegrationTests.CompleteExportFlow_GeneratesMatchingPythonAndGdsFiles` and `PythonConfigurationIntegrationTests.EndToEnd_DiscoverSelectAndCheck_WorksTogether`. | low | Delete. |
| 18 | `DuplicatePinConnectionLoadTests.Load_EveryShippedExample_ReportsNoDrops` | 5.9 | no | No connection is dropped when an example loads | `ExampleLoadRoutingTests.Example_OpensWithEveryConnectionRouted` asserts an empty error console after load and routing for every example. `ExampleDesignFilesTests.EveryExample_LoadsExactlyWhatItDeclares` asserts connection count == declared count. | low | Delete; keep `Load_CleanRoundTrippedDesign_ReportsNoDrops` (the mutation case). |
| 19 | `ExampleFrozenBlockedPathTests.Example_ReportsPinnedBlockedPathCount` (28 rows) | 5.8 | no | Pinned BlockedPath count per example | `ExampleNestedDesignCheckTests.Example_ReportsPinnedDesignCheckFindings` runs the full `DesignValidator.Validate` chain, which includes the frozen blocked-path checker. It pins the identical BlockedPath values for all 11 non-zero examples and zero elsewhere. | low | Delete. |
| 20 | `GdsExportIntegrationTests.ViewModel_ExportScriptToGds_UpdatesLastExportStatus` | 5.5 | no | A failing real-python export updates `LastExportStatus` | `ViewModel_ExportScriptToGds_WithGenerateDisabled_SkipsGdsGeneration` checks `LastExportStatus == result.Status`. The success text is checked by `CompleteExportFlow_GeneratesMatchingPythonAndGdsFiles`. | low | Delete, or move it to the stub service. |
| 21 | `ExampleWaveguideCrossingTests.Example_ReportsPinnedWaveguideCrossingCount` (28 rows) | 5.2 | no | Pinned top-level waveguide crossings per example (all 0) | The same validator chain in `ExampleNestedDesignCheckTests` includes the crossing detector. It pins no WaveguideCrossing anywhere, which equals the all-zero pins here. | low | Delete. |
| 22 | `ComponentFootprintOverlapExampleTests.EveryShippedExample_ReportsZeroFootprintOverlaps` | 4.9 | no | No footprint overlap in any example | `ExampleNestedDesignCheckTests` runs the footprint-overlap checker over the same items and pins none. | low | Delete; keep the mutation test `LogicUnit4Bit_ShipsWithoutFootprintOverlaps`. |
| 23 | `OpenEblEBeamGroupedMziVerificationTests.GroupedMzi_SubmissionCheckAndVerification_ZeroErrors` | 4.7 | no | Grouped MZI: export + checks, zero errors | `GroupedMzi_AfterSaveLoadRoundTrip_…` runs the same grouping before its save/load, but exports only the reloaded canvas. | med | Optional merge: export and check the canvas both before and after the round trip in one test. |
| 24 | `Issue744LocalizationScreenshotTests.CaptureLocalizationWalkthrough` | 7.7 (saves ~4.6) | no | Renders 5 languages; asserts the zh and ja frames differ from en | de/es frames have no assertion of their own. This is a localization test on purpose, so only the unasserted languages are listed. | med | Render en + zh + ja by default; render de/es only with `CAP_UPDATE_PR_MEDIA=1`. |
| 25 | `OpenEblEBeamMziReadinessTests.EBeamMziExample_OpenEblSubmissionChecks_PinCurrentGaps` | 4.3 | no | EBeam MZI submission census | Same example, export and checker as `OpenEblSubmissionCheckerIntegrationTests.EBeamMziExample_Checker_ReportsZeroErrorsOnBothChecks`. | low | Assert its census lines on `report.SubmissionOutput` there (as `OpenEblEBeamFromScratchJourneyTests` does). Then delete. |
| 26 | `OpenEblEBeamMziVerificationTests.EBeamMziExample_OpenEblVerification_CompletesAndPinsRemainingGaps` | 4.2 | no | EBeam MZI functional verification | Same twin as row 25; it asserts verification error count 0, which implies the per-category zeros here. | low | Move the "Top cell" line and the 4/4 component counts into the twin via `report.VerificationOutput`. Then delete. |
| 27 | `OpenEblMziReadinessTests.MziExample_OpenEblSubmissionChecks_PinCurrentGaps` | 4.2 | yes | Demo MZI: pins 16 errors / 15 layer errors / die size | `OpenEblSubmissionCheckerIntegrationTests.DemoMziExample_Checker_ListsDieSizeAndLayerErrorsAsTypedEntries` (4.7 s) pins the same example, script and numbers. | low | Keep one. Convert the survivor's 16/15 to invariants: a die-size error is present, and every layer error names a layer outside the EBeam map. |
| 28 | `PhotonTorchScriptExecutionTests` (2 tests) | 3.7 | no | Two scripts run without error under photontorch | Different builders, but each only asserts "the script runs". | low | Optional: export the waveguides and the coupler in one design and run it once. |
| 29 | `Issue1374SpectrumLegendScreenshotTests.CaptureSpectrumWithReadableLegend` | 7.3 (saves ~3.5) | no | en + de captures, each after its own 500-step sweep | The only content assertion (unique legend titles) does not depend on language and is covered by `AddDropRingSpectrumLegendTests.SpectrumLegend_AddDropRing_TitlesDistinct_AndNameThroughAndDrop` (0.1 s). | low | Sweep once, switch the language, re-render. Or use ~100 steps. |
| 30 | `Services.DynamicGdsFilenameTests.ExportedScript_GeneratesGdsWithDifferentNames_ForDifferentScriptNames` | 3.4 | no | Two scripts, two GDS files next to them | Duplicates `DynamicGdsFilenameIntegrationTests.CompleteExportFlow_WithDifferentFilenames_GeneratesCorrectGdsNames` (8.5 s), which also covers `GdsExportService`. | low | Delete. |
| 31 | `Issue1382FsrReadoutScreenshotTests.CaptureFsrReadout_EnglishAndGerman` | 5.3 (saves ~2.6) | no | FSR readout captures in en + de | The German pass repeats the 500-step sweep and the same FSR assertions. The physics is covered by `SpectrumFsrReadoutIntegrationTests.AddDropRing_ThroughReadoutFsr_MatchesRingPhysicsWithinTwoPercent`. | low | One sweep, two renders. |
| 32 | `DynamicGdsFilenameTests.ExportedScript_HandlesPathsWithSpaces_Correctly` | 2.4 | no | Script path with spaces | It is the matching-filename test with a different file name. | low | Name the matching-filename test's script `my design.py`, then delete. |
| 33 | `DynamicGdsFilenameIntegrationTests.ExportedScript_CanBeExecutedManually_ProducesGds` | 2.2 | no | Raw `python script.py` writes `<name>.gds` | Identical to `Services.DynamicGdsFilenameTests.ExportedScript_GeneratesGdsWithMatchingFilename_WhenExecuted` (same minimal canvas, exit 0, stdout and file checks). | low | Delete one of the two. |
| 34 | `Issue1389Ram2x4ScreenshotTests.CaptureRam2x4Example` (logic-tab half) | 1.8 (saves ~0.9) | no | Write/read demo and `Q == 10` on the RAM 2×4 | `Issue1399CellGroupScreenshotTests` runs the same demo three times, and `Ram2x4UiStackJourneyTests` covers it too. The logic-tab frame predates the per-cell collapse. | low | Keep only the overview capture. |
| 35 | `AgentBridgeRepairExampleTests.InsertCrossing_ThenUndo_RestoresComponentsAndConnections` | 10.2 | no | `insert_crossing` + `undo` tools | Overlaps `InsertCrossing_WithThroughWire_DocksTheBlockedWireOnTheCrossing` (48.4 s, Heavy), which differs only by the through-wire branch. | med | **Keep for now.** It is the PR-time coverage, because the twin is Heavy. Not counted in the totals. |

## Brittle pins (convert to invariants, ~0 s each)

| Test | Pinned value | Invariant instead |
|------|--------------|-------------------|
| `GdsMziElectricalContentionRepairTests.MziElectricalFixture_ContentionRepair_AcceptsOneRepair` | `AcceptCount == 1`, `blocked == 2` | Accepts ≥ 1, fewer blocked wires after the repair than before, blocked ≤ 2. Keep the test: it is the only real-design guard of the pass. |
| `ExampleNestedDesignCheckTests.Example_ReportsPinnedDesignCheckFindings` | Full summaries such as `UnconnectedPin=1099 OverlappingPaths=227` | Exclude the gate template's known discard and bias ports, then pin the remaining categories as "≤ baseline". Risk med: re-pinning touches every example row. |
| `FourBitAdderManufacturingJourneyTests.Step1_…` (344 gates / 339 wires), `LogicUnit4BitManufacturingJourneyTests` (60 leaves / 52 frozen paths), `FullAdderManufacturingJourneyTests` (30 wires) | Gate and wire counts of the shipped file | Already implied by `ExampleDesignFilesTests.EveryExample_LoadsExactlyWhatItDeclares`. Assert the logic behaviour (they already do) and drop the counts, or derive them from the file. |
| OpenEBL Demo MZI (row 27) | 16 errors, 15 layer errors | See row 27. |

## Diagnostics that silently pass (0 s, no CI cost)

`ArcObstacleCollisionDiagnosticTests`, `FlatComponentRoutingDiagnosticTests`,
`ProcessMinRadiusRoutingDiagnosticTests` and `RoutingStyleCanvasDiagnosticTests` return early
unless `UI_SHOT_DIR` is set, so CI reports them as passed without running anything. Mark them
`Skip.If(...)` so they show up as skipped, or delete them if nobody uses them.

## Checked and kept

- `CrossingPieceEditingTests` (cut, segment shift, slide): three different editors, each with its own invariants.
- `TemporaryCrossingTests.AdaptiveOff_UserPlacedCrossings_StayWhenTheirWireEndMoves`, `…DeletingAChainPiece…`: distinct behaviour.
- `AgentBridgeRepairExampleTests.ShiftSegment…`, `…SuggestFixes…`: tool-level paths with no twin.
- `NazcaExportIntegrationTests.ExportedScript_ProducesGdsFile_WhenNazcaInstalled`: the only real-nazca run with bends.
- `ExampleGroupWireCrossingTests`: frozen-path crossings are not part of the validator's OverlappingPaths.
- `ExampleIdentifierUniqueness`, `LogicExamplesSweep`, `LogicExamplesLayout`, `ExampleLoadRouting`, `ExampleDesignFiles`: each asserts a distinct property.
- Help-flyout screenshot tests (#1333, #1335, #1344, #1352, #1362, #1391, #1421) and `GdsRoundTripVisualVerificationTests`: real assertions (flyout state, animation frames differ, pixel probes).
- `GdsBondPadOffsetProbeTests`, `GdsImportPerformanceProbeTests`: despite the name, real alignment and budget assertions.
- `Issue*ScreenshotTests` with `manifest.Count.ShouldBe(N)`: every one read also checks for blank frames or real ViewModel state.

## Totals

| Scope | Rows | Full-run CI time saved |
|-------|------|------------------------|
| All candidates (rows 1–34) | 34 | **≈ 441 s** (26 % of the 1 685 s summed test time) |
| Low risk only | 28 | **≈ 284 s** |
| Of all candidates, the part PRs run today (not Heavy) | — | ≈ 130 s |

Most of the saving sits in the `slow-agentbridge-routing-components` and `fast-routing-dies`
shards (rows 1–3, 5, 6, 11–13: ≈ 260 s), which are also the two longest shards, so pruning there
shortens the pipeline's wall-clock too.

## Related finding (not a pruning candidate)

On the 2-bit counter, a routing pass with nothing changed still takes 1.2–1.9 s locally. Two
wires that the router itself just routed fail `IsRouteStillValid` on the next pass:
`WaveguideRouter.IsPathBlocked` flags their geometry. One fails only on the pin-corridor padding
(`IsPathBlockedByComponents(segments, ownPins)` accepts it), the other fails even
`IsPathBlockedByComponentOnly`. So the incremental pass re-routes both on every pass, in the app
as well as in tests. That is roughly a quarter of the counter tests' time. The fix touches routing
validity, so it needs its own issue and review rather than a test-speed PR.
