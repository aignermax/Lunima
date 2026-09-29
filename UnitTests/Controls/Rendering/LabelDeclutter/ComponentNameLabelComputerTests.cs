using Avalonia;
using Avalonia.Headless.XUnit;
using CAP.Avalonia.Controls.Rendering.LabelDeclutter;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using Shouldly;
using UnitTests.Helpers;
using Xunit;

namespace UnitTests.Controls.Rendering.LabelDeclutter;

/// <summary>
/// Tests for <see cref="ComponentNameLabelComputer"/>: measures each simple component's name
/// label at the current screen-space-clamped font size, resolves overlaps via
/// <see cref="LabelOverlapResolver"/>, and caches the (expensive) result until the content
/// signature actually changes — panning alone must never re-trigger it (see
/// <see cref="PanningAlone_DoesNotTriggerRebuild"/>). <see cref="AvaloniaFactAttribute"/> is
/// required because measuring text needs an initialized Avalonia font manager.
/// </summary>
public class ComponentNameLabelComputerTests
{
    private static readonly Rect WideViewport = new(-1000, -1000, 4000, 4000);

    /// <summary>Typical measured world-space height of the name label at zoom 1.</summary>
    private const double TypicalLabelHeight = 14.0;

    [AvaloniaFact]
    public void LabelAnchor_FlatComponent_SitsBelowTheFootprint_NeverOverTheGeometry()
    {
        // Regression: a flat component (e.g. a 7 µm tall directional coupler) had its name
        // label drawn straight over its waveguide body — the anchor must be below the
        // footprint's bottom edge so the geometry stays visible.
        var comp = MakeComponent("flat", x: 100, y: 200, width: 70, height: 7);

        var anchor = ComponentNameLabelComputer.GetLabelAnchor(comp, TypicalLabelHeight);

        anchor.Y.ShouldBeGreaterThan(200 + 7, "the label must start below the footprint's bottom edge");
        anchor.X.ShouldBeGreaterThanOrEqualTo(100);
    }

    [AvaloniaFact]
    public void LabelAnchor_TallComponent_StaysInsideTheFootprint()
    {
        // A component tall enough to host its own label keeps the classic inside-top-left
        // anchor — pushing every label below its footprint would drop it onto neighbouring
        // components or waveguides in dense layouts (e.g. the Full Adder example).
        var comp = MakeComponent("tall", x: 100, y: 200, width: 250, height: 250);

        var anchor = ComponentNameLabelComputer.GetLabelAnchor(comp, TypicalLabelHeight);

        anchor.X.ShouldBeInRange(100, 100 + 250);
        anchor.Y.ShouldBeInRange(200, 200 + 250 - TypicalLabelHeight,
            "a tall component's label must stay inside its own footprint");
    }

    [AvaloniaFact]
    public void NonOverlappingComponents_BothNamesVisible()
    {
        var far = MakeComponent("far", x: 1000, y: 1000);
        var near = MakeComponent("near", x: 0, y: 0);
        var computer = new ComponentNameLabelComputer();

        var visible = computer.GetVisibleLabelIds(new[] { far, near }, hoveredComponentId: null, WideViewport, zoom: 1.0);

        visible.ShouldBe(new[] { far.Component.Id, near.Component.Id }, ignoreOrder: true);
    }

    [AvaloniaFact]
    public void OverlappingComponents_SelectedNameWinsOverUnselected()
    {
        var a = MakeComponent("aName", x: 0, y: 0);
        var b = MakeComponent("bName", x: 5, y: 0); // overlaps a's label
        b.IsSelected = true;
        var computer = new ComponentNameLabelComputer();

        var visible = computer.GetVisibleLabelIds(new[] { a, b }, hoveredComponentId: null, WideViewport, zoom: 1.0);

        visible.ShouldBe(new[] { b.Component.Id });
    }

    [AvaloniaFact]
    public void OverlappingComponents_HoveredNameWinsOverUnhoveredNormal()
    {
        var a = MakeComponent("aName", x: 0, y: 0);
        var b = MakeComponent("bName", x: 5, y: 0);
        var computer = new ComponentNameLabelComputer();

        var visible = computer.GetVisibleLabelIds(new[] { a, b }, b.Component.Id, WideViewport, zoom: 1.0);

        visible.ShouldBe(new[] { b.Component.Id });
    }

    [AvaloniaFact]
    public void ComponentGroups_AreNeverIncluded()
    {
        var group = new ComponentViewModel(new ComponentGroup("MyGroup") { PhysicalX = 0, PhysicalY = 0 });
        var plain = MakeComponent("plain", x: 500, y: 500);
        var computer = new ComponentNameLabelComputer();

        var visible = computer.GetVisibleLabelIds(new[] { group, plain }, hoveredComponentId: null, WideViewport, zoom: 1.0);

        visible.ShouldBe(new[] { plain.Component.Id });
    }

    [AvaloniaFact]
    public void GroupChildren_TwoFlatChildrenStackedSixteenMicrometersApart_ExactlyOneLabelVisible()
    {
        // Regression (issue #1158 review): the Full Adder's paired straight waveguides are
        // flat group CHILDREN stacked micrometers apart. Child labels used to bypass the
        // overlap resolver entirely, so both names were drawn on top of each other as
        // illegible text. At zoom 0.25 the screen-space font floor makes each label ~28 µm
        // tall in world space, so the two bounds 16 µm apart must collide — exactly one wins.
        var groupVm = MakeGroupWithTwoFlatStackedChildren(verticalSpacing: 16, out var childA, out var childB);
        var computer = new ComponentNameLabelComputer();

        var visible = computer.GetVisibleLabelIds(new[] { groupVm }, hoveredComponentId: null, WideViewport, zoom: 0.25);

        visible.Count.ShouldBe(1, "two overlapping flat child labels must be thinned to exactly one");
        visible.ShouldBeSubsetOf(new[] { childA.Id, childB.Id });
    }

    [AvaloniaFact]
    public void GroupChildren_FarApart_BothLabelsVisibleAndMeasured()
    {
        var groupVm = MakeGroupWithTwoFlatStackedChildren(verticalSpacing: 500, out var childA, out var childB);
        var computer = new ComponentNameLabelComputer();

        var visible = computer.GetVisibleLabelIds(new[] { groupVm }, hoveredComponentId: null, WideViewport, zoom: 1.0);

        visible.ShouldBe(new[] { childA.Id, childB.Id }, ignoreOrder: true);
        computer.TryGetLabelText(childA.Id).ShouldNotBeNull(
            "the renderer must be able to draw the child label from the computer's measured text");
    }

    [AvaloniaFact]
    public void GroupChildMoving_InvalidatesTheCache()
    {
        // A child's absolute position changes when its group is dragged — the cached overlap
        // resolution must follow, or a moved group keeps its stale label bounds.
        var groupVm = MakeGroupWithTwoFlatStackedChildren(verticalSpacing: 500, out var childA, out _);
        var computer = new ComponentNameLabelComputer();
        var components = new[] { groupVm };

        computer.GetVisibleLabelIds(components, hoveredComponentId: null, WideViewport, zoom: 1.0);
        computer.RebuildCount.ShouldBe(1);

        childA.PhysicalX += 100;
        computer.GetVisibleLabelIds(components, hoveredComponentId: null, WideViewport, zoom: 1.0);

        computer.RebuildCount.ShouldBe(2, "a moved group child changes the content signature and must trigger a rebuild");
    }

    [AvaloniaFact]
    public void ComponentFarOutsideViewport_IsCulled()
    {
        var offscreen = MakeComponent("offscreen", x: 100_000, y: 100_000);
        var computer = new ComponentNameLabelComputer();

        var visible = computer.GetVisibleLabelIds(new[] { offscreen }, hoveredComponentId: null, WideViewport, zoom: 1.0);

        visible.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public void CullingUsesMeasuredLabelBounds_NotJustComponentFootprint()
    {
        // A small component's own footprint (x:[-20,-10]) sits entirely outside the viewport
        // (x:[0,50]), but its long name label — anchored just below the footprint's left edge
        // and extending rightward by its measured text width — reaches into the viewport.
        // Culling against the footprint alone would wrongly drop a label that is genuinely
        // drawn on screen.
        var viewport = new Rect(0, 0, 50, 50);
        var comp = MakeComponent("VeryLongComponentNameThatExtendsFar", x: -20, y: 0, width: 10, height: 10);
        var computer = new ComponentNameLabelComputer();

        var visible = computer.GetVisibleLabelIds(new[] { comp }, hoveredComponentId: null, viewport, zoom: 1.0);

        visible.ShouldBe(new[] { comp.Component.Id });
    }

    [AvaloniaFact]
    public void MovingAComponent_TriggersRebuildAndUpdatesResult()
    {
        var comp = MakeComponent("mover", x: 0, y: 0);
        var computer = new ComponentNameLabelComputer();
        var components = new[] { comp };
        // Covers the label's home inside the (tall, 250 µm) footprint after the move.
        var farViewport = new Rect(190, -10, 20, 20);

        computer.GetVisibleLabelIds(components, hoveredComponentId: null, WideViewport, zoom: 1.0);
        computer.RebuildCount.ShouldBe(1);

        comp.X = 200;
        var after = computer.GetVisibleLabelIds(components, hoveredComponentId: null, farViewport, zoom: 1.0);

        computer.RebuildCount.ShouldBe(2, "a moved component changes the content signature and must trigger a rebuild");
        after.ShouldBe(new[] { comp.Component.Id });
    }

    [AvaloniaFact]
    public void RenamingAComponent_InvalidatesTheCache()
    {
        // Component.Identifier is a stable alias, but HumanReadableName is the user-facing,
        // editable display name (ComponentViewModel.Name) actually measured and drawn — a
        // rename must invalidate cached bounds/text or the old label lingers as stale overlap
        // input until an unrelated change happens to invalidate it.
        var comp = MakeComponent("original", x: 0, y: 0);
        var computer = new ComponentNameLabelComputer();
        var components = new[] { comp };

        computer.GetVisibleLabelIds(components, hoveredComponentId: null, WideViewport, zoom: 1.0);
        computer.RebuildCount.ShouldBe(1);

        comp.Component.HumanReadableName = "SomethingCompletelyDifferentAndMuchLonger";
        computer.GetVisibleLabelIds(components, hoveredComponentId: null, WideViewport, zoom: 1.0);

        computer.RebuildCount.ShouldBe(2, "renaming a component must invalidate the cached signature");
    }

    [AvaloniaFact]
    public void ResizingAComponent_InvalidatesTheCache()
    {
        var comp = MakeComponent("resizable", x: 0, y: 0);
        var computer = new ComponentNameLabelComputer();
        var components = new[] { comp };

        computer.GetVisibleLabelIds(components, hoveredComponentId: null, WideViewport, zoom: 1.0);
        computer.RebuildCount.ShouldBe(1);

        comp.Component.WidthMicrometers *= 2;
        computer.GetVisibleLabelIds(components, hoveredComponentId: null, WideViewport, zoom: 1.0);

        computer.RebuildCount.ShouldBe(2, "resizing a component must invalidate the cached signature");
    }

    [AvaloniaFact]
    public void RotatingAComponent_InvalidatesTheCache()
    {
        var comp = MakeComponent("rotatable", x: 0, y: 0);
        var computer = new ComponentNameLabelComputer();
        var components = new[] { comp };

        computer.GetVisibleLabelIds(components, hoveredComponentId: null, WideViewport, zoom: 1.0);
        computer.RebuildCount.ShouldBe(1);

        comp.Component.RotationDegrees = 90;
        computer.GetVisibleLabelIds(components, hoveredComponentId: null, WideViewport, zoom: 1.0);

        computer.RebuildCount.ShouldBe(2, "rotating a component must invalidate the cached signature");
    }

    [AvaloniaFact]
    public void PanningAlone_DoesNotTriggerRebuild()
    {
        // Two viewports of the same size and zoom, one a pure translation of the other: the
        // overlap RESULT is translation-invariant (relative component positions are unchanged),
        // so the expensive resolve must not rerun just because the visible world region shifted
        // — only the cheap per-frame viewport-intersection culling may differ.
        var a = MakeComponent("aName", x: 0, y: 0);
        var b = MakeComponent("bName", x: 5, y: 0);
        var computer = new ComponentNameLabelComputer();
        var components = new[] { a, b };
        var viewport1 = new Rect(-50, -50, 200, 200);
        var viewport2 = new Rect(-30, -20, 200, 200); // panned, same size/zoom

        var visible1 = computer.GetVisibleLabelIds(components, hoveredComponentId: null, viewport1, zoom: 1.0);
        computer.RebuildCount.ShouldBe(1);
        var visible2 = computer.GetVisibleLabelIds(components, hoveredComponentId: null, viewport2, zoom: 1.0);

        computer.RebuildCount.ShouldBe(1, "panning the viewport must not re-trigger the overlap sweep");
        visible2.ShouldBe(visible1, ignoreOrder: true);
    }

    [AvaloniaFact]
    public void SmallZoomChangeWithinQuantizationBucket_DoesNotTriggerRebuild()
    {
        var comp = MakeComponent("stable", x: 0, y: 0);
        var computer = new ComponentNameLabelComputer();
        var components = new[] { comp };

        computer.GetVisibleLabelIds(components, hoveredComponentId: null, WideViewport, zoom: 1.0);
        computer.RebuildCount.ShouldBe(1);

        computer.GetVisibleLabelIds(components, hoveredComponentId: null, WideViewport, zoom: 1.01);

        computer.RebuildCount.ShouldBe(1, "a sub-5% zoom change must stay within the same quantization bucket");
    }

    [AvaloniaFact]
    public void ZoomChangeCrossingQuantizationBucket_TriggersRebuild()
    {
        var comp = MakeComponent("stable", x: 0, y: 0);
        var computer = new ComponentNameLabelComputer();
        var components = new[] { comp };

        computer.GetVisibleLabelIds(components, hoveredComponentId: null, WideViewport, zoom: 1.0);
        computer.RebuildCount.ShouldBe(1);

        computer.GetVisibleLabelIds(components, hoveredComponentId: null, WideViewport, zoom: 3.0);

        computer.RebuildCount.ShouldBe(2, "a large zoom change must cross a 5% bucket and trigger a rebuild");
    }

    [AvaloniaFact]
    public void UnrelatedComponentMoving_ReusesTheUnchangedComponentsMeasuredText()
    {
        // Both components' text is measured once. Moving "beta" changes the content signature
        // (forces a rebuild), but "alpha"'s name and font size are unchanged, so its cached
        // FormattedText must be reused rather than re-measured — the fix for the double-measure
        // this computer used to cause every rebuild.
        var alpha = MakeComponent("Alpha", x: 0, y: 0);
        var beta = MakeComponent("Beta", x: 500, y: 500);
        var computer = new ComponentNameLabelComputer();
        var components = new[] { alpha, beta };

        computer.GetVisibleLabelIds(components, hoveredComponentId: null, WideViewport, zoom: 1.0);
        int textCountAfterFirstBuild = computer.MeasuredTextCount;

        beta.X = 600;
        computer.GetVisibleLabelIds(components, hoveredComponentId: null, WideViewport, zoom: 1.0);

        computer.RebuildCount.ShouldBe(2);
        computer.MeasuredTextCount.ShouldBe(textCountAfterFirstBuild,
            "alpha's (name, font size) text was already cached and must not be re-measured");
    }

    [AvaloniaFact]
    public void AtExtremeZoomOut_LabelRemainsVisibleAtClampedMinimumSize()
    {
        // A hard "hide below readability" cutoff used to make even a hovered/selected label
        // disappear at low zoom, breaking hover feedback and name-based orientation. The font
        // size now only clamps to a legible minimum — the label itself always stays visible.
        var comp = MakeComponent("stillHere", x: 0, y: 0);
        var computer = new ComponentNameLabelComputer();

        var visible = computer.GetVisibleLabelIds(new[] { comp }, hoveredComponentId: null, WideViewport, zoom: 0.05);

        visible.ShouldBe(new[] { comp.Component.Id });
        computer.TryGetLabelText(comp.Component.Id).ShouldNotBeNull();
    }

    [AvaloniaFact]
    public void TryGetLabelText_ReturnsTheSamePreMeasuredInstance_ForRepeatedCalls()
    {
        var comp = MakeComponent("cached", x: 0, y: 0);
        var computer = new ComponentNameLabelComputer();

        computer.GetVisibleLabelIds(new[] { comp }, hoveredComponentId: null, WideViewport, zoom: 1.0);
        var first = computer.TryGetLabelText(comp.Component.Id);
        var second = computer.TryGetLabelText(comp.Component.Id);

        first.ShouldNotBeNull();
        ReferenceEquals(first, second).ShouldBeTrue(
            "the renderer must draw the exact FormattedText this computer measured, not a fresh copy");
    }

    /// <summary>Group with two flat (1 µm tall, label-height-exceeding) children sharing the
    /// same X, the second <paramref name="verticalSpacing"/> µm below the first — the Full
    /// Adder's stacked "Straight Waveguide 100µm" pair in miniature.</summary>
    private static ComponentViewModel MakeGroupWithTwoFlatStackedChildren(
        double verticalSpacing, out Component childA, out Component childB)
    {
        childA = MakeComponent("Straight Waveguide 100µm", x: 0, y: 0, width: 100, height: 1).Component;
        childB = MakeComponent("Straight Waveguide 100µm", x: 0, y: verticalSpacing, width: 100, height: 1).Component;
        var group = new ComponentGroup("Gate") { PhysicalX = 0, PhysicalY = 0 };
        group.AddChild(childA);
        group.AddChild(childB);
        return new ComponentViewModel(group);
    }

    private static ComponentViewModel MakeComponent(
        string identifier, double x, double y, double? width = null, double? height = null)
    {
        var component = TestComponentFactory.CreateBasicComponent();
        component.Identifier = identifier;
        component.PhysicalX = x;
        component.PhysicalY = y;
        if (width.HasValue) component.WidthMicrometers = width.Value;
        if (height.HasValue) component.HeightMicrometers = height.Value;
        return new ComponentViewModel(component) { X = x, Y = y };
    }
}
