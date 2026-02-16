# PR Integration Tasks - Making Features User-Visible

**Philosophy**: A feature only counts when users can actually use it in the product.

**Approach**:
- ✅ Complete one PR at a time
- ✅ Add all necessary UI integration
- ✅ User reviews functionality
- ✅ Move to next PR

---

## Status Legend
- 🟢 **READY** - Fully integrated, ready for user review
- 🟡 **BACKEND COMPLETE** - Code works but no UI
- 🔴 **NEEDS WORK** - Missing UI integration

---

## PR #32: Grid Snapping (Issue #3)
**Status**: 🟢 **READY FOR REVIEW**

### What's Done:
- ✅ Grid snap logic implemented
- ✅ Visual grid overlay
- ✅ Keyboard shortcut (G key)
- ✅ Status display in canvas
- ✅ Settings object with toggle

### What's Needed:
- [ ] Settings panel to adjust grid size (currently defaults to 50µm)
- [ ] Persistence of user preference across sessions
- [ ] Visual indicator when component snaps to grid

### User Testing Checklist:
- [ ] Press 'G' key - does grid overlay appear?
- [ ] Drag a component - does it snap to grid points?
- [ ] Press 'G' again - does snap disable?
- [ ] Is the grid size appropriate for typical components?

**Recommendation**: Start here - mostly complete, just needs settings UI

---

## PR #31: Zoom-to-Fit (Issue #4)
**Status**: 🟢 **READY FOR REVIEW**

### What's Done:
- ✅ Bounding box calculation
- ✅ Zoom calculation with 10% padding
- ✅ Keyboard shortcut (F key)
- ✅ Empty canvas handling

### What's Needed:
- [ ] Menu item: View > Zoom to Fit (with F key indicator)
- [ ] Toolbar button with icon
- [ ] Visual feedback during zoom animation (optional)

### User Testing Checklist:
- [ ] Press 'F' key - does viewport zoom to show all components?
- [ ] Works with single component?
- [ ] Works with many scattered components?
- [ ] Proper padding around design?

**Recommendation**: Tackle second - nearly complete, just needs menu/toolbar

---

## PR #26: Signal Path Highlighting (Issue #9)
**Status**: 🟡 **BACKEND COMPLETE**

### What's Done:
- ✅ Power flow calculation
- ✅ Connection color-coding by power level
- ✅ Fade out low-power paths
- ✅ Hover tooltips showing power values

### What's Needed:
- [ ] Keyboard shortcut to toggle (suggest 'P' for power)
- [ ] Menu item: View > Show Power Flow
- [ ] Status bar indicator when active
- [ ] Trigger power flow calculation after simulation
- [ ] **CRITICAL**: Wire up to simulation results (currently no connection)

### User Testing Checklist:
- [ ] Run a simulation
- [ ] Toggle power flow view
- [ ] Do high-power connections appear brighter/thicker?
- [ ] Do low-power paths fade out?
- [ ] Hover over connection - see power value?

**Recommendation**: Tackle third - good visual feature but needs wiring to simulation

---

## PR #30: Architecture Metrics (Issue #5)
**Status**: 🟡 **BACKEND COMPLETE**

### What's Done:
- ✅ Component count by type
- ✅ Network depth calculation
- ✅ Fan-out distribution
- ✅ Feedback loop detection
- ✅ Path length distribution

### What's Needed:
- [ ] **NEW VIEW**: Architecture metrics side panel
- [ ] Menu item: Analysis > Architecture Metrics
- [ ] Display metrics in formatted panel:
  - Component counts (pie chart or table)
  - Network depth visualization
  - Fan-out histogram
  - Feedback loops list
  - Path statistics
- [ ] Refresh metrics when design changes
- [ ] Export button to save metrics

### User Testing Checklist:
- [ ] Open metrics panel
- [ ] Create a simple design - see metrics update?
- [ ] Metrics make sense for the design?
- [ ] Export metrics to file

**Recommendation**: Medium complexity - needs full panel UI

---

## PR #29: Loss Budget Estimation (Issue #6)
**Status**: 🟡 **BACKEND COMPLETE**

### What's Done:
- ✅ Path loss calculation (input → output)
- ✅ Min/max/average loss per path
- ✅ Loss severity classification (green/yellow/red)
- ✅ Critical path identification

### What's Needed:
- [ ] **NEW PANEL**: Loss Budget Analyzer
- [ ] Menu item: Analysis > Loss Budget
- [ ] Visual display:
  - Table of all input→output paths with losses
  - Color-coded severity indicators
  - Highlight critical paths in canvas
  - Loss breakdown per connection
- [ ] Button: "Analyze Loss Budget"
- [ ] Export results to CSV/PDF

### User Testing Checklist:
- [ ] Open loss budget panel
- [ ] Run analysis
- [ ] See all signal paths with loss values
- [ ] Visual highlighting of high-loss paths
- [ ] Makes sense for test design?

**Recommendation**: Medium-high complexity - needs panel + canvas visualization

---

## PR #28: Topology Comparison (Issue #7)
**Status**: 🟡 **BACKEND COMPLETE**

### What's Done:
- ✅ ComparisonView.axaml exists
- ✅ ComparisonViewModel implemented
- ✅ Design loading logic
- ✅ Metrics calculation for both designs
- ✅ Diff detection

### What's Needed:
- [ ] Menu item: File > Compare Designs...
- [ ] File picker for two .cappro files
- [ ] Ensure ComparisonView is wired to menu
- [ ] Export comparison report button
- [ ] Better visual diff highlighting

### User Testing Checklist:
- [ ] File > Compare Designs
- [ ] Select two .cappro files
- [ ] See both designs side-by-side?
- [ ] Metrics comparison visible?
- [ ] Differences highlighted?
- [ ] Export report works?

**Recommendation**: Low complexity - UI exists, just needs menu wiring

---

## PR #27: Parameter Sweep (Issue #8)
**Status**: 🟡 **BACKEND COMPLETE**

### What's Done:
- ✅ Parameter sweep engine
- ✅ Range/step configuration
- ✅ Simulation runner
- ✅ CSV export

### What's Needed:
- [ ] **NEW DIALOG**: Parameter Sweep Configuration
- [ ] Menu item: Analysis > Parameter Sweep...
- [ ] UI elements:
  - Parameter selection dropdown
  - Min/Max/Steps input fields
  - "Run Sweep" button
  - Progress bar
  - Results plot (line graph: parameter vs output)
  - Export CSV button
- [ ] Integration with simulation engine

### User Testing Checklist:
- [ ] Open parameter sweep dialog
- [ ] Select a parameter (e.g., coupling ratio)
- [ ] Define range and steps
- [ ] Run sweep - see progress?
- [ ] View results plot
- [ ] Export data

**Recommendation**: High complexity - needs dialog + plotting

---

## PR #25: Routing Complexity Metrics (Issue #10)
**Status**: 🟡 **BACKEND COMPLETE**

### What's Done:
- ✅ Bend count calculation
- ✅ Routing congestion score
- ✅ Complexity thresholds
- ✅ Warning generation

### What's Needed:
- [ ] Display in diagnostics panel or architecture metrics panel
- [ ] Menu item: Analysis > Routing Complexity
- [ ] Visual indicators:
  - Total bends count
  - Avg bends per connection
  - Congestion heatmap (optional)
  - Warning badges if thresholds exceeded
- [ ] Auto-run after routing completes

### User Testing Checklist:
- [ ] Route a design
- [ ] View routing complexity metrics
- [ ] Warnings appear for complex routes?
- [ ] Metrics make sense?

**Recommendation**: Medium complexity - needs panel integration

---

## PR #24: S-Matrix Validation Export (Issue #11)
**Status**: 🟡 **BACKEND COMPLETE**

### What's Done:
- ✅ Validation export logic
- ✅ JSON serialization
- ✅ Menu item exists (?)

### What's Needed:
- [ ] Verify menu item: File > Export > Validation Report
- [ ] File save dialog
- [ ] Auto-export option in settings
- [ ] Visual notification when export completes

### User Testing Checklist:
- [ ] Run simulation with validation issues
- [ ] Export validation report
- [ ] Open JSON file - readable?
- [ ] Contains all error details?

**Recommendation**: Low complexity - mostly done, verify menu wiring

---

## PR #23: Sanity Check (Issue #12)
**Status**: 🟡 **BACKEND COMPLETE**

### What's Done:
- ✅ Unconnected pins check
- ✅ Energy conservation check
- ✅ High loss path check
- ✅ No light flow check

### What's Needed:
- [ ] **POST-SIMULATION DIALOG**: Sanity Check Results
- [ ] Automatically show after simulation completes
- [ ] Display:
  - Color-coded severity (info/warning/error)
  - List of all issues found
  - "Fix" buttons where applicable
  - "Ignore" or "OK" to dismiss
- [ ] Manual trigger: Analysis > Run Sanity Check

### User Testing Checklist:
- [ ] Run simulation
- [ ] Sanity check dialog appears automatically?
- [ ] Issues listed clearly?
- [ ] Severity levels make sense?
- [ ] Can dismiss dialog?

**Recommendation**: Medium complexity - needs dialog + auto-trigger

---

## PR #22: Routing Diagnostics Export (Issue #13)
**Status**: 🟡 **BACKEND COMPLETE**

### What's Done:
- ✅ A* search stats collection
- ✅ Failed route tracking
- ✅ Obstacle map extraction
- ✅ JSON export

### What's Needed:
- [ ] Auto-export on routing failure
- [ ] Menu item: File > Export > Routing Diagnostics
- [ ] Notification dialog when export occurs
- [ ] Link from routing error message to diagnostics file

### User Testing Checklist:
- [ ] Create unroutable design
- [ ] Routing fails
- [ ] Diagnostics automatically exported?
- [ ] JSON file contains useful debug info?

**Recommendation**: Low complexity - mostly done, needs auto-trigger

---

## PR #21: Parametric S-Matrix (Issue #14)
**Status**: 🟡 **BACKEND COMPLETE**

### What's Done:
- ✅ Formula parsing and evaluation
- ✅ Parameter definitions
- ✅ Runtime parameter updates
- ✅ PDK loader extended

### What's Needed:
- [ ] **COMPONENT PROPERTIES PANEL**: Show parametric controls
- [ ] UI elements for each parameter:
  - Sliders with min/max from definition
  - Numeric input
  - Live preview
  - "Reset to Default" button
- [ ] Sample parametric PDK component for testing
- [ ] Auto-resimulate when parameter changes (debounced)

### User Testing Checklist:
- [ ] Load parametric component
- [ ] See parameter sliders in properties
- [ ] Adjust parameter - component updates?
- [ ] Re-simulate - results reflect change?

**Recommendation**: High complexity - needs properties panel + sample PDK

---

## PR #20: Stochastic Variation (Issue #15)
**Status**: 🟡 **BACKEND COMPLETE**

### What's Done:
- ✅ Parameter perturbation
- ✅ Monte Carlo simulation runner
- ✅ Statistical analysis (mean, std dev)
- ✅ Histogram generation
- ✅ CSV export

### What's Needed:
- [ ] **NEW DIALOG**: Robustness Analysis
- [ ] Menu item: Analysis > Robustness Analysis...
- [ ] UI elements:
  - Variation percentage input (e.g., ±5%)
  - Number of iterations (N = 100, 1000, etc.)
  - "Run Analysis" button
  - Progress bar
  - Results display:
    - Mean/StdDev table
    - Histogram plot
    - Yield estimate
  - Export CSV button

### User Testing Checklist:
- [ ] Open robustness dialog
- [ ] Set 5% variation, 100 iterations
- [ ] Run analysis - see progress?
- [ ] View histogram of results
- [ ] Statistics make sense?
- [ ] Export data

**Recommendation**: High complexity - needs dialog + plotting

---

## PR #19: Library Statistics (Issue #16)
**Status**: 🟡 **BACKEND COMPLETE**

### What's Done:
- ✅ Component usage counting
- ✅ Statistics calculation
- ✅ JSON export

### What's Needed:
- [ ] Display in component library panel
- [ ] Show usage count badge on each component
- [ ] Sort library by usage count
- [ ] Menu item: View > Library Statistics
- [ ] Optional: Usage tracking across multiple designs

### User Testing Checklist:
- [ ] Open component library
- [ ] See usage counts on components?
- [ ] Sort by most-used?
- [ ] Counts accurate?

**Recommendation**: Low-medium complexity - enhance existing library panel

---

## PR #18: Feedback Loop Detection (Issue #17)
**Status**: 🟡 **BACKEND COMPLETE**

### What's Done:
- ✅ Cycle detection (graph algorithm)
- ✅ Loop gain estimation
- ✅ Instability warnings
- ✅ Loop classification

### What's Needed:
- [ ] Display in architecture metrics panel
- [ ] Visual overlay on canvas:
  - Highlight detected loops
  - Show loop gain
  - Warning icon for unstable loops (gain > 1)
- [ ] Menu item: Analysis > Detect Feedback Loops
- [ ] List of all loops with properties

### User Testing Checklist:
- [ ] Create design with resonator/ring
- [ ] Run loop detection
- [ ] Loops highlighted in canvas?
- [ ] Loop gain shown?
- [ ] Warnings for unstable loops?

**Recommendation**: Medium complexity - needs panel + canvas overlay

---

## Recommended Order of Implementation

### Phase 1: Quick Wins (1-2 days each)
1. ✅ **PR #32**: Grid Snapping - Add settings panel
2. ✅ **PR #31**: Zoom-to-Fit - Add menu items
3. **PR #28**: Topology Comparison - Wire menu
4. **PR #24**: Validation Export - Verify wiring

### Phase 2: Analysis Features (3-5 days each)
5. **PR #26**: Power Flow - Wire to simulation
6. **PR #23**: Sanity Check - Add dialog
7. **PR #30**: Architecture Metrics - Create panel
8. **PR #25**: Routing Complexity - Integrate into panel
9. **PR #19**: Library Statistics - Enhance library UI
10. **PR #18**: Feedback Loops - Add visualization
11. **PR #29**: Loss Budget - Create analysis panel

### Phase 3: Advanced Features (5-7 days each)
12. **PR #22**: Routing Diagnostics - Auto-export
13. **PR #27**: Parameter Sweep - Full dialog + plotting
14. **PR #21**: Parametric S-Matrix - Properties panel
15. **PR #20**: Stochastic Variation - Full analysis dialog

---

## Quality Checkpoints

Before marking any PR as "complete":
- [ ] Feature accessible from menu/toolbar/keyboard
- [ ] Visual feedback for all actions
- [ ] Works as expected with real designs
- [ ] User can understand what it does without docs
- [ ] No crashes or errors
- [ ] Integrates naturally into workflow

---

**Next Step**: Start with PR #32 (Grid Snapping)?
