#!/usr/bin/env python3
"""Create GitHub issues using the GitHub API."""

import json
import os
import sys
import urllib.request
import urllib.error

REPO_OWNER = "aignermax"
REPO_NAME = "Connect-A-PIC-Pro"
API_URL = f"https://api.github.com/repos/{REPO_OWNER}/{REPO_NAME}/issues"

def create_issue(token, title, body, labels):
    """Create a GitHub issue via API."""
    data = {
        "title": title,
        "body": body,
        "labels": labels
    }

    headers = {
        "Authorization": f"token {token}",
        "Accept": "application/vnd.github.v3+json",
        "Content-Type": "application/json"
    }

    req = urllib.request.Request(
        API_URL,
        data=json.dumps(data).encode('utf-8'),
        headers=headers,
        method='POST'
    )

    try:
        with urllib.request.urlopen(req) as response:
            result = json.loads(response.read().decode('utf-8'))
            print(f"  ✓ Created: #{result['number']}")
            return True
    except urllib.error.HTTPError as e:
        print(f"  ✗ Failed: {e.code} - {e.reason}")
        return False
    except Exception as e:
        print(f"  ✗ Error: {e}")
        return False

def main():
    token = os.environ.get('GITHUB_TOKEN')
    if not token:
        print("Error: GITHUB_TOKEN environment variable not set")
        sys.exit(1)

    print("Creating GitHub issues for Connect-A-PIC Pro...")
    print()

    issues = [
        # UX Efficiency
        {
            "title": "Implement optional grid snapping for component placement",
            "labels": ["agent-task", "ux-efficiency", "enhancement"],
            "body": """## Description
Add optional snap-to-grid to speed up clean layout creation during exploration.

## Acceptance Criteria
- [ ] GridSnapEnabled setting (default off)
- [ ] GridSnapSizeMicrometers (default 50µm)
- [ ] Snap during drag operations
- [ ] Keyboard toggle (G key)
- [ ] Visual grid overlay when enabled

## Technical Notes
- Modify `DesignCanvas.cs` drag logic
- Store in user preferences
- Minimal overhead when disabled

## Strategic Fit
Speeds up layout iteration without compromising exploration focus."""
        },
        {
            "title": "Add zoom-to-fit functionality for quick overview",
            "labels": ["agent-task", "ux-efficiency", "enhancement"],
            "body": """## Description
Auto-fit entire design in viewport for rapid context switching between detail and overview.

## Acceptance Criteria
- [ ] Calculate bounding box of all components
- [ ] 10% padding around design
- [ ] Keyboard shortcut (F key)
- [ ] Handle empty canvas gracefully

## Technical Notes
- Add `ZoomToFitCommand`
- Modify viewport transform in `DesignCanvas`

## Strategic Fit
Faster navigation when exploring large system architectures."""
        },
        # Architecture Insight
        {
            "title": "Add system complexity metrics for architecture assessment",
            "labels": ["agent-task", "architecture-insight", "core"],
            "body": """## Description
Calculate and display metrics that characterize architectural complexity, helping users reason about scalability and stability.

## Acceptance Criteria
- [ ] Component count by type
- [ ] Network depth (longest path from input to output)
- [ ] Fan-out distribution (connections per component)
- [ ] Feedback loop detection and count
- [ ] Average path length distribution
- [ ] Display in side panel or overlay

## Technical Notes
- Add `ArchitectureMetricsCalculator` in `Connect-A-Pic-Core/Analysis/`
- Graph traversal for depth/loops
- Update on topology change

## Strategic Fit
Core architecture reasoning - helps answer "how complex is this system?\""""
        },
        {
            "title": "Implement system-level loss budget estimation",
            "labels": ["agent-task", "architecture-insight", "diagnostics"],
            "body": """## Description
Calculate and visualize total loss budget from inputs to outputs, helping identify bottlenecks and feasibility issues early.

## Acceptance Criteria
- [ ] Calculate min/max/average loss for all input→output paths
- [ ] Identify highest-loss paths
- [ ] Visual color-coding (green: <3dB, yellow: 3-10dB, red: >10dB)
- [ ] Loss budget table with breakdown
- [ ] Highlight critical connections

## Technical Notes
- Add `LossBudgetAnalyzer` in `Connect-A-Pic-Core/Analysis/`
- Use existing `WaveguideConnection.TotalLossDb`
- Graph analysis for all paths

## Strategic Fit
Answers "is this architecture feasible?" before detailed simulation."""
        },
        {
            "title": "Side-by-side topology comparison tool",
            "labels": ["agent-task", "architecture-insight", "exploration"],
            "body": """## Description
Compare two design variants side-by-side to evaluate architectural trade-offs.

## Acceptance Criteria
- [ ] Load two .cappro files for comparison
- [ ] Split-view canvas showing both designs
- [ ] Metrics comparison table (complexity, loss, component count)
- [ ] Highlight differences in topology
- [ ] Export comparison report

## Technical Notes
- Add `ComparisonView` in `CAP.Avalonia/Views/`
- Reuse `ArchitectureMetricsCalculator`
- Read-only mode for both designs

## Strategic Fit
Core exploration tool - "which architecture is better?\""""
        },
        {
            "title": "Quick parameter sweep for sensitivity analysis",
            "labels": ["agent-task", "architecture-insight", "exploration"],
            "body": """## Description
Vary component parameters (coupling ratio, phase, etc.) and observe system response trends.

## Acceptance Criteria
- [ ] Select parameter to sweep (e.g., coupling ratio)
- [ ] Define range and step count
- [ ] Run simulation for each value
- [ ] Plot output power vs parameter
- [ ] Export sweep results to CSV

## Technical Notes
- Add `ParameterSweeper` in `Connect-A-Pic-Core/Analysis/`
- Reuse existing simulation infrastructure
- Simple line plots in UI

## Strategic Fit
Answers "how sensitive is this architecture to parameter variations?\""""
        },
        {
            "title": "Highlight dominant signal paths in system",
            "labels": ["agent-task", "architecture-insight", "visualization"],
            "body": """## Description
Visualize which paths carry most optical power, helping understand system behavior.

## Acceptance Criteria
- [ ] Calculate power flow through all connections after simulation
- [ ] Color-code connections by power (thick/bright = high power)
- [ ] Fade out low-power paths (<-20dB)
- [ ] Toggle on/off with keyboard shortcut
- [ ] Show power values on hover

## Technical Notes
- Add `PowerFlowVisualizer` in `CAP.Avalonia/`
- Use `Complex.Magnitude` from simulation results
- Modify connection rendering in `DesignCanvas`

## Strategic Fit
Visual understanding of "where does the light actually go?\""""
        },
        # Diagnostic Tools
        {
            "title": "Calculate routing complexity metrics per design",
            "labels": ["agent-task", "diagnostics", "routing"],
            "body": """## Description
Quantify routing complexity to warn about designs that may be hard to realize physically.

## Acceptance Criteria
- [ ] Total bend count
- [ ] Average bends per connection
- [ ] Routing congestion score (waveguide density)
- [ ] Longest routed path
- [ ] Display in diagnostics panel
- [ ] Warning if complexity exceeds threshold

## Technical Notes
- Add `RoutingComplexityAnalyzer` in `Connect-A-Pic-Core/Routing/`
- Use existing `RoutedPath.BendCount`
- Spatial density calculation on `PathfindingGrid`

## Strategic Fit
Early warning "this topology will be hard to route in practice\""""
        },
        {
            "title": "Export S-Matrix validation results to JSON",
            "labels": ["agent-task", "diagnostics", "validation"],
            "body": """## Description
Export validation warnings/errors to help debug unphysical system matrices.

## Acceptance Criteria
- [ ] Export `LastValidationResult` from `GridLightCalculator`
- [ ] JSON format with errors, warnings, component IDs
- [ ] Timestamp and wavelength info
- [ ] File menu: "Export > Validation Report"
- [ ] Auto-export after simulation (optional setting)

## Technical Notes
- Extend `SMatrixValidationResult` with `ToJson()`
- Add export command in `CAP.Avalonia`
- Store as `.validation.json`

## Strategic Fit
Faster identification of unphysical designs during exploration."""
        },
        {
            "title": "Post-simulation architecture sanity check",
            "labels": ["agent-task", "diagnostics", "validation"],
            "body": """## Description
After simulation, show quick summary of potential issues (energy conservation violations, unconnected pins, extreme losses).

## Acceptance Criteria
- [ ] Check for unconnected pins
- [ ] Energy conservation violations (from S-Matrix validator)
- [ ] Paths with >20dB loss
- [ ] Components with no light flow
- [ ] Show summary dialog after simulation
- [ ] Color-coded severity (info/warning/error)

## Technical Notes
- Add `SanityChecker` in `Connect-A-Pic-Core/Analysis/`
- Aggregate from multiple validators
- Dialog in `CAP.Avalonia`

## Strategic Fit
Quick feedback "does this make sense physically?\""""
        },
        {
            "title": "Export detailed routing diagnostics on failure",
            "labels": ["agent-task", "diagnostics", "routing"],
            "body": """## Description
When routing fails, export detailed diagnostics to help understand why.

## Acceptance Criteria
- [ ] Track A* search stats (nodes expanded, time, timeout)
- [ ] Export failed routes with reason
- [ ] Obstacle map around failed connection
- [ ] Pin alignment information
- [ ] JSON format for analysis
- [ ] Auto-export on routing failure

## Technical Notes
- Add `RoutingDiagnosticsCollector` in `Connect-A-Pic-Core/Routing/`
- Extend `AStarPathfinder` to collect stats
- Export to `.routing-diag.json`

## Strategic Fit
Debug complex topologies faster."""
        },
        # Core Engine
        {
            "title": "Add parametric S-Matrix templates for components",
            "labels": ["agent-task", "core-engine", "components"],
            "body": """## Description
Define component S-matrices with formulas/parameters instead of fixed values, enabling rapid variation.

## Acceptance Criteria
- [ ] Extend PDK JSON format with formula support
- [ ] Support parameters like "coupling_ratio", "phase_shift"
- [ ] Parse and evaluate formulas at runtime
- [ ] UI sliders to adjust parameters live
- [ ] Re-simulate on parameter change

## Technical Notes
- Add `ParametricSMatrix` in `Connect-A-Pic-Core/Components/`
- Formula parser (use existing or simple expression evaluator)
- Extend `PdkLoader` for formula parsing

## Strategic Fit
Core exploration capability - vary designs instantly."""
        },
        {
            "title": "Add simple stochastic variation for robustness estimation",
            "labels": ["agent-task", "core-engine", "analysis"],
            "body": """## Description
Add random variations to component parameters to estimate design robustness.

## Acceptance Criteria
- [ ] Define variation percentage per parameter type
- [ ] Run N simulations with random variations
- [ ] Report mean and std deviation of outputs
- [ ] Simple histogram of output power distribution
- [ ] Export results to CSV

## Technical Notes
- Add `StochasticSimulator` in `Connect-A-Pic-Core/Analysis/`
- Use `Random` for parameter perturbation
- Parallel execution for speed

## Strategic Fit
Answers "how robust is this architecture to fabrication variations?\""""
        },
        {
            "title": "Display component library usage statistics",
            "labels": ["agent-task", "core-engine", "analysis"],
            "body": """## Description
Show which components are used most, helping understand design patterns.

## Acceptance Criteria
- [ ] Count instances per component type
- [ ] Show in component library panel
- [ ] Sort by usage count
- [ ] Export statistics to JSON
- [ ] Track across multiple designs (optional)

## Technical Notes
- Add `LibraryStatistics` in `Connect-A-Pic-Core/Components/`
- Count from `GridManager.Components`
- Display in UI library panel

## Strategic Fit
Insight into architectural patterns and preferences."""
        },
        {
            "title": "Detect and analyze feedback loops in network",
            "labels": ["agent-task", "core-engine", "analysis"],
            "body": """## Description
Detect feedback loops (resonators, rings) and analyze stability implications.

## Acceptance Criteria
- [ ] Graph traversal to detect cycles
- [ ] Count and classify feedback loops
- [ ] Estimate loop gain (if S-matrices available)
- [ ] Warn about potential instability (gain >1)
- [ ] Visualize loops in canvas

## Technical Notes
- Add `FeedbackLoopDetector` in `Connect-A-Pic-Core/Analysis/`
- Cycle detection algorithm (Tarjan's or DFS)
- Loop gain = product of connection transmissions

## Strategic Fit
Critical for resonator/filter architectures - "will this oscillate?\""""
        }
    ]

    created = 0
    failed = 0

    for i, issue in enumerate(issues, 1):
        print(f"[{i}/15] Creating: {issue['title']}")
        if create_issue(token, issue['title'], issue['body'], issue['labels']):
            created += 1
        else:
            failed += 1

    print()
    print(f"✅ Successfully created {created} issues")
    if failed > 0:
        print(f"❌ Failed to create {failed} issues")
    print()
    print(f"View at: https://github.com/{REPO_OWNER}/{REPO_NAME}/issues")

if __name__ == '__main__':
    main()
