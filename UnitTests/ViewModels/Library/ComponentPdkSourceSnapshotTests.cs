using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Components.Core;
using Shouldly;
using UnitTests.Helpers;
using Xunit;

namespace UnitTests.ViewModels.Library;

/// <summary>
/// Verifies <see cref="ComponentPdkSourceSnapshot"/>: the design checks capture it on the UI
/// thread and resolve it on the validation worker, so it must resolve exactly like the
/// placement guards (template snapshot, else live-library match, group children at every
/// depth) while reading only what was captured.
/// </summary>
public class ComponentPdkSourceSnapshotTests
{
    private const string LibraryPdk = "Library PDK";
    private const string SnapshotPdk = "Snapshot PDK";
    private const string KnownFunction = "demo.shallow.strt";

    private static readonly IReadOnlyList<ComponentTemplate> Library = new[]
    {
        new ComponentTemplate { Name = "Straight", NazcaFunctionName = KnownFunction, PdkSource = LibraryPdk },
    };

    [Fact]
    public void Resolve_PlacedComponentWithTemplateSnapshot_KeepsTheSnapshot()
    {
        var placed = Placed(KnownFunction, SnapshotPdk);

        var sources = ComponentPdkSourceSnapshot.Capture(new[] { placed }, NoGroups(), Library).Resolve();

        sources[placed.Component].ShouldBe(SnapshotPdk);
    }

    [Fact]
    public void Resolve_PlacedComponentWithoutTemplateSnapshot_FallsBackToTheLibrary()
    {
        var placed = Placed(KnownFunction, templatePdkSource: null);

        var sources = ComponentPdkSourceSnapshot.Capture(new[] { placed }, NoGroups(), Library).Resolve();

        sources[placed.Component].ShouldBe(LibraryPdk);
    }

    [Fact]
    public void Resolve_ComponentWithoutLibraryMatch_IsNull()
    {
        var placed = Placed("unknown.function", templatePdkSource: null);

        var sources = ComponentPdkSourceSnapshot.Capture(new[] { placed }, NoGroups(), Library).Resolve();

        sources[placed.Component].ShouldBeNull();
    }

    [Fact]
    public void Resolve_GroupChildren_AtEveryDepth_ResolveThroughTheLibrary()
    {
        var (groupVm, group, inner, child, grandchild) = NestedGroup();

        var sources = ComponentPdkSourceSnapshot.Capture(new[] { groupVm }, new[] { group }, Library).Resolve();

        sources.Keys.ShouldBe(new Component[] { group, child, inner, grandchild }, ignoreOrder: true);
        sources[group].ShouldBeNull("a group matches no library template");
        sources[inner].ShouldBeNull("a nested group matches no library template either");
        sources[child].ShouldBe(LibraryPdk);
        sources[grandchild].ShouldBe(LibraryPdk);
    }

    [Fact]
    public void Resolve_AfterCapture_IgnoresLaterCanvasAndGroupEdits()
    {
        var (groupVm, group, inner, child, grandchild) = NestedGroup();
        var loose = Placed(KnownFunction, templatePdkSource: null);
        var canvas = new List<ComponentViewModel> { groupVm, loose };
        var snapshot = ComponentPdkSourceSnapshot.Capture(canvas, new[] { group }, Library);
        var captured = new Component[] { group, child, inner, grandchild, loose.Component };

        canvas.Remove(loose);
        group.RemoveChild(child);
        group.AddChild(Placed(KnownFunction, templatePdkSource: null).Component);

        snapshot.Resolve().Keys.ShouldBe(captured, ignoreOrder: true);
    }

    [Fact]
    public void Resolve_SameComponentPlacedTwice_Throws()
    {
        var placed = Placed(KnownFunction, SnapshotPdk);

        var snapshot = ComponentPdkSourceSnapshot.Capture(new[] { placed, placed }, NoGroups(), Library);

        Should.Throw<ArgumentException>(() => snapshot.Resolve());
    }

    private static ComponentViewModel Placed(string nazcaFunctionName, string? templatePdkSource)
    {
        var placed = TestComponentFactory.CreateComponentViewModel(nazcaFunctionName);
        placed.TemplatePdkSource = templatePdkSource;
        return placed;
    }

    private static (ComponentViewModel GroupVm, ComponentGroup Group, ComponentGroup Inner, Component Child, Component Grandchild)
        NestedGroup()
    {
        var group = TestComponentFactory.CreateComponentGroup("Outer");
        var inner = TestComponentFactory.CreateComponentGroup("Inner");
        var child = Placed(KnownFunction, templatePdkSource: null).Component;
        var grandchild = Placed(KnownFunction, templatePdkSource: null).Component;
        inner.AddChild(grandchild);
        group.AddChild(child);
        group.AddChild(inner);
        return (new ComponentViewModel(group), group, inner, child, grandchild);
    }

    private static IEnumerable<ComponentGroup> NoGroups() => Array.Empty<ComponentGroup>();
}
