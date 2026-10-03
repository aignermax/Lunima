# Help-flyout animations

Small looping diagrams inside the (?) help flyouts. Every control derives from
`HelpAnimationBase` (`CAP.Avalonia/Controls/HelpAnimations/`): the animation is a pure
function of `Progress` (0 = first frame, 1 = last frame). With `AutoPlay` (default true) a
dispatcher timer loops it while visible; tests set `AutoPlay="False"` and assign `Progress`
to capture an exact frame. Namespace in AXAML:
`xmlns:ha="using:CAP.Avalonia.Controls.HelpAnimations"`.

## LightPulseAlongPath

A light pulse travelling along a waveguide polyline, over a faint track. Compose several
instances with staggered `WindowStart`/`WindowEnd` (0…1 loop fractions) to show hand-offs
(e.g. one pulse into a coupler, two split pulses leaving). `PathPoints="x,y x,y …"` is the
route in the control's coordinate space; `TrackBrush`, `PulseBrush`, `PulseDiameter`,
`LoopDuration` tune the look. Size the control to the region it may draw in.

```xml
<ha:LightPulseAlongPath Width="380" Height="80" PathPoints="36,40 195,40"
    PulseBrush="#FFF176" WindowStart="0" WindowEnd="0.5" IsHitTestVisible="False"/>
```

## ParameterSweepMiniPlot

A fixed curve with a marker dot sweeping along it once per loop — for help text that would
otherwise describe a curve in words (laser line shape, RIN noise trace, MZI fringe).
`CurvePoints="x,y x,y …"` is the polyline; `PingPong="True"` sweeps there-and-back (spectra),
`False` wraps to the start (time traces). `CurveBrush`, `MarkerBrush`, `MarkerDiameter`,
`LoopDuration` tune the look.

```xml
<ha:ParameterSweepMiniPlot Width="240" Height="44" CurvePoints="0,40 120,4 240,40"
    MarkerBrush="#FFD54F" PingPong="True" IsHitTestVisible="False"/>
```

## Bespoke overlays (EyeStackAnimation, LayerStackAnimation, CarryRippleAnimation, ChipletLinkCouplingAnimation, LengthMatchArrivalAnimation, MonteCarloScatterAnimation)

Panel-specific compositions (eye-diagram stacking, process layer stack) that live in the
same folder but are not primitives — they draw in their flyout's fixed coordinate space and
are placed as transparent overlays over the flyout's static diagram. They take no content
properties beyond the base `Progress`/`AutoPlay`/`LoopDuration`; add a new one only when no
combination of the primitives above expresses the concept.

`ChipletLinkCouplingAnimation` (Design Checks help, #1247) is the exception that proves the
rule: it is a self-contained scene (two facets, a widening Gaussian beam, a spillover spot)
placed as regular flyout content, and its η readout is computed with the real
`ChipletEdgeCouplerCoupling.PowerCouplingForOffset`/`PowerCouplingForGap` at 1550 nm —
never hard-coded — so the help can never drift from the simulation. New help numbers that
mirror simulation physics must call the core functions the same way.

`LengthMatchArrivalAnimation` (Length Matching help, #1256) follows the same rule: two
pulses leave a splitter, the longer arm's pulse arrives late (phase slip), then the short
arm grows a meander and a second pair arrives together. Its ΔL / Δt readout is the real
group-delay relation Δt = ΔL·n_g/c with the core's `GateDelayCalculator.DefaultGroupIndex`
and `SpeedOfLightMicrometersPerPicosecond` (the constants `WireDelayCalculator` applies to
routed wires), and the drawing conserves length — the grown meander is exactly as long as
the detour arm it matches.

`MonteCarloScatterAnimation` (Monte Carlo help, #1344) is another self-contained scene:
a waveguide cross-section whose width wobbles, a transmission dip that shifts with it,
and dots dropping into a histogram that builds a bell shape. The dot landing bins come
from a fixed-seed generator, so every loop (and every screenshot) is identical.

## Rules

- One animation per flyout; keep text sections to ≤3 short sentences (enforced by
  `HelpTextBudgetTests`).
- Animations loop the last frame into the first; the last frame keeps the finished state
  visible (no fade-out at the end).
- Never block the UI thread: per-tick work is a few `Canvas`/`Opacity` sets; the timer
  stops when the flyout closes.
- Set `IsHitTestVisible="False"` so the animation never swallows clicks.
