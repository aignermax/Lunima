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

## Bespoke overlays (EyeStackAnimation, LayerStackAnimation)

Panel-specific compositions (eye-diagram stacking, process layer stack) that live in the
same folder but are not primitives — they draw in their flyout's fixed coordinate space and
are placed as transparent overlays over the flyout's static diagram. They take no content
properties beyond the base `Progress`/`AutoPlay`/`LoopDuration`; add a new one only when no
combination of the primitives above expresses the concept.

## Rules

- One animation per flyout; keep text sections to ≤3 short sentences (enforced by
  `HelpTextBudgetTests`).
- Animations loop the last frame into the first; the last frame keeps the finished state
  visible (no fade-out at the end).
- Never block the UI thread: per-tick work is a few `Canvas`/`Opacity` sets; the timer
  stops when the flyout closes.
- Set `IsHitTestVisible="False"` so the animation never swallows clicks.
