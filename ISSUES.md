# GitHub Issues for Connect-A-PIC Pro

All issues tagged with `agent-task` for automatic agent processing.

## Summary

- **UX Efficiency**: 2 issues
- **Architecture Insight**: 5 issues (core focus)
- **Diagnostic Tools**: 4 issues
- **Core Engine**: 4 issues

**Total**: 15 issues

---

## 1. UX Efficiency

### Issue #1: Implement optional grid snapping for component placement
**Labels**: `agent-task`, `ux-efficiency`, `enhancement`

**Description**: Add optional snap-to-grid to speed up clean layout creation during exploration.

**Acceptance Criteria**:
- [ ] GridSnapEnabled setting (default off)
- [ ] GridSnapSizeMicrometers (default 50µm)
- [ ] Snap during drag operations
- [ ] Keyboard toggle (G key)
- [ ] Visual grid overlay when enabled

**Technical Notes**: Modify `DesignCanvas.cs` drag logic, store in user preferences, minimal overhead when disabled.

**Strategic Fit**: Speeds up layout iteration without compromising exploration focus.

---

### Issue #2: Add zoom-to-fit functionality for quick overview
**Labels**: `agent-task`, `ux-efficiency`, `enhancement`

**Description**: Auto-fit entire design in viewport for rapid context switching between detail and overview.

**Acceptance Criteria**:
- [ ] Calculate bounding box of all components
- [ ] 10% padding around design
- [ ] Keyboard shortcut (F key)
- [ ] Handle empty canvas gracefully

**Technical Notes**: Add `ZoomToFitCommand`, modify viewport transform in `DesignCanvas`.

**Strategic Fit**: Faster navigation when exploring large system architectures.

---

## 2. Architecture Insight (Core Focus)

### Issue #3: Add system complexity metrics for architecture assessment
**Labels**: `agent-task`, `architecture-insight`, `core`

**Description**: Calculate and display metrics that characterize architectural complexity, helping users reason about scalability and stability.

**Acceptance Criteria**:
- [ ] Component count by type
- [ ] Network depth (longest path from input to output)
- [ ] Fan-out distribution (connections per component)
- [ ] Feedback loop detection and count
- [ ] Average path length distribution
- [ ] Display in side panel or overlay

**Technical Notes**: Add `ArchitectureMetricsCalculator` in `Connect-A-Pic-Core/Analysis/`, graph traversal for depth/loops, update on topology change.

**Strategic Fit**: Core architecture reasoning - helps answer "how complex is this system?"

---

### Issue #4: Implement system-level loss budget estimation
**Labels**: `agent-task`, `architecture-insight`, `diagnostics`

**Description**: Calculate and visualize total loss budget from inputs to outputs, helping identify bottlenecks and feasibility issues early.

**Acceptance Criteria**:
- [ ] Calculate min/max/average loss for all input→output paths
- [ ] Identify highest-loss paths
- [ ] Visual color-coding (green: <3dB, yellow: 3-10dB, red: >10dB)
- [ ] Loss budget table with breakdown
- [ ] Highlight critical connections

**Technical Notes**: Add `LossBudgetAnalyzer` in `Connect-A-Pic-Core/Analysis/`, use existing `WaveguideConnection.TotalLossDb`, graph analysis for all paths.

**Strategic Fit**: Answers "is this architecture feasible?" before detailed simulation.

---

### Issue #5: Side-by-side topology comparison tool
**Labels**: `agent-task`, `architecture-insight`, `exploration`

**Description**: Compare two design variants side-by-side to evaluate architectural trade-offs.

**Acceptance Criteria**:
- [ ] Load two .cappro files for comparison
- [ ] Split-view canvas showing both designs
- [ ] Metrics comparison table (complexity, loss, component count)
- [ ] Highlight differences in topology
- [ ] Export comparison report

**Technical Notes**: Add `ComparisonView` in `CAP.Avalonia/Views/`, reuse `ArchitectureMetricsCalculator`, read-only mode for both designs.

**Strategic Fit**: Core exploration tool - "which architecture is better?"

---

### Issue #6: Quick parameter sweep for sensitivity analysis
**Labels**: `agent-task`, `architecture-insight`, `exploration`

**Description**: Vary component parameters (coupling ratio, phase, etc.) and observe system response trends.

**Acceptance Criteria**:
- [ ] Select parameter to sweep (e.g., coupling ratio)
- [ ] Define range and step count
- [ ] Run simulation for each value
- [ ] Plot output power vs parameter
- [ ] Export sweep results to CSV

**Technical Notes**: Add `ParameterSweeper` in `Connect-A-Pic-Core/Analysis/`, reuse existing simulation infrastructure, simple line plots in UI.

**Strategic Fit**: Answers "how sensitive is this architecture to parameter variations?"

---

### Issue #7: Highlight dominant signal paths in system
**Labels**: `agent-task`, `architecture-insight`, `visualization`

**Description**: Visualize which paths carry most optical power, helping understand system behavior.

**Acceptance Criteria**:
- [ ] Calculate power flow through all connections after simulation
- [ ] Color-code connections by power (thick/bright = high power)
- [ ] Fade out low-power paths (<-20dB)
- [ ] Toggle on/off with keyboard shortcut
- [ ] Show power values on hover

**Technical Notes**: Add `PowerFlowVisualizer` in `CAP.Avalonia/`, use `Complex.Magnitude` from simulation results, modify connection rendering in `DesignCanvas`.

**Strategic Fit**: Visual understanding of "where does the light actually go?"

---

## 3. Diagnostic Tools

### Issue #8: Calculate routing complexity metrics per design
**Labels**: `agent-task`, `diagnostics`, `routing`

**Description**: Quantify routing complexity to warn about designs that may be hard to realize physically.

**Acceptance Criteria**:
- [ ] Total bend count
- [ ] Average bends per connection
- [ ] Routing congestion score (waveguide density)
- [ ] Longest routed path
- [ ] Display in diagnostics panel
- [ ] Warning if complexity exceeds threshold

**Technical Notes**: Add `RoutingComplexityAnalyzer` in `Connect-A-Pic-Core/Routing/`, use existing `RoutedPath.BendCount`, spatial density calculation on `PathfindingGrid`.

**Strategic Fit**: Early warning "this topology will be hard to route in practice"

---

### Issue #9: Export S-Matrix validation results to JSON
**Labels**: `agent-task`, `diagnostics`, `validation`

**Description**: Export validation warnings/errors to help debug unphysical system matrices.

**Acceptance Criteria**:
- [ ] Export `LastValidationResult` from `GridLightCalculator`
- [ ] JSON format with errors, warnings, component IDs
- [ ] Timestamp and wavelength info
- [ ] File menu: "Export > Validation Report"
- [ ] Auto-export after simulation (optional setting)

**Technical Notes**: Extend `SMatrixValidationResult` with `ToJson()`, add export command in `CAP.Avalonia`, store as `.validation.json`.

**Strategic Fit**: Faster identification of unphysical designs during exploration.

---

### Issue #10: Post-simulation architecture sanity check
**Labels**: `agent-task`, `diagnostics`, `validation`

**Description**: After simulation, show quick summary of potential issues (energy conservation violations, unconnected pins, extreme losses).

**Acceptance Criteria**:
- [ ] Check for unconnected pins
- [ ] Energy conservation violations (from S-Matrix validator)
- [ ] Paths with >20dB loss
- [ ] Components with no light flow
- [ ] Show summary dialog after simulation
- [ ] Color-coded severity (info/warning/error)

**Technical Notes**: Add `SanityChecker` in `Connect-A-Pic-Core/Analysis/`, aggregate from multiple validators, dialog in `CAP.Avalonia`.

**Strategic Fit**: Quick feedback "does this make sense physically?"

---

### Issue #11: Export detailed routing diagnostics on failure
**Labels**: `agent-task`, `diagnostics`, `routing`

**Description**: When routing fails, export detailed diagnostics to help understand why.

**Acceptance Criteria**:
- [ ] Track A* search stats (nodes expanded, time, timeout)
- [ ] Export failed routes with reason
- [ ] Obstacle map around failed connection
- [ ] Pin alignment information
- [ ] JSON format for analysis
- [ ] Auto-export on routing failure

**Technical Notes**: Add `RoutingDiagnosticsCollector` in `Connect-A-Pic-Core/Routing/`, extend `AStarPathfinder` to collect stats, export to `.routing-diag.json`.

**Strategic Fit**: Debug complex topologies faster.

---

## 4. Core Engine Improvements

### Issue #12: Add parametric S-Matrix templates for components
**Labels**: `agent-task`, `core-engine`, `components`

**Description**: Define component S-matrices with formulas/parameters instead of fixed values, enabling rapid variation.

**Acceptance Criteria**:
- [ ] Extend PDK JSON format with formula support
- [ ] Support parameters like "coupling_ratio", "phase_shift"
- [ ] Parse and evaluate formulas at runtime
- [ ] UI sliders to adjust parameters live
- [ ] Re-simulate on parameter change

**Technical Notes**: Add `ParametricSMatrix` in `Connect-A-Pic-Core/Components/`, formula parser (use existing or simple expression evaluator), extend `PdkLoader` for formula parsing.

**Strategic Fit**: Core exploration capability - vary designs instantly.

---

### Issue #13: Add simple stochastic variation for robustness estimation
**Labels**: `agent-task`, `core-engine`, `analysis`

**Description**: Add random variations to component parameters to estimate design robustness.

**Acceptance Criteria**:
- [ ] Define variation percentage per parameter type
- [ ] Run N simulations with random variations
- [ ] Report mean and std deviation of outputs
- [ ] Simple histogram of output power distribution
- [ ] Export results to CSV

**Technical Notes**: Add `StochasticSimulator` in `Connect-A-Pic-Core/Analysis/`, use `Random` for parameter perturbation, parallel execution for speed.

**Strategic Fit**: Answers "how robust is this architecture to fabrication variations?"

---

### Issue #14: Display component library usage statistics
**Labels**: `agent-task`, `core-engine`, `analysis`

**Description**: Show which components are used most, helping understand design patterns.

**Acceptance Criteria**:
- [ ] Count instances per component type
- [ ] Show in component library panel
- [ ] Sort by usage count
- [ ] Export statistics to JSON
- [ ] Track across multiple designs (optional)

**Technical Notes**: Add `LibraryStatistics` in `Connect-A-Pic-Core/Components/`, count from `GridManager.Components`, display in UI library panel.

**Strategic Fit**: Insight into architectural patterns and preferences.

---

### Issue #15: Detect and analyze feedback loops in network
**Labels**: `agent-task`, `core-engine`, `analysis`

**Description**: Detect feedback loops (resonators, rings) and analyze stability implications.

**Acceptance Criteria**:
- [ ] Graph traversal to detect cycles
- [ ] Count and classify feedback loops
- [ ] Estimate loop gain (if S-matrices available)
- [ ] Warn about potential instability (gain >1)
- [ ] Visualize loops in canvas

**Technical Notes**: Add `FeedbackLoopDetector` in `Connect-A-Pic-Core/Analysis/`, cycle detection algorithm (Tarjan's or DFS), loop gain = product of connection transmissions.

**Strategic Fit**: Critical for resonator/filter architectures - "will this oscillate?"

---

## How to Create These Issues

Run the script:
```bash
./create-issues.sh
```

Or create manually on GitHub with the `agent-task` label.
