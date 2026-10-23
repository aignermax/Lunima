using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Canvas.Services;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Routing;
using Shouldly;
using UnitTests.Helpers;
using Xunit;

namespace UnitTests.ViewModels;

public sealed class GroupRoutePublicationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AcceptedStyleIntent_IsInTheDocumentBeforeDeferredRouting(bool undo)
    {
        using var fixture = new GroupFixture();
        var wire = fixture.Canvas.Connections[0];
        var command = new ChangeRoutingStyleCommand(fixture.Canvas, new[] { wire }, WaveguideType.Auto);

        // History restoration suppresses new routing: accepted intent must not rely on
        // a worker finishing, a navigation flush, or a later save to become canonical.
        fixture.Canvas.Routing.RestoreMove(command.Execute, () => { });
        if (undo)
        {
            wire.NotifyPathChanged();
            fixture.Canvas.Routing.RestoreMove(command.Undo, () => { });
        }

        var stored = fixture.Group.InternalPaths.Single(path => path.PathId == wire.Connection.Id);
        stored.ConnectionType.ShouldBe(undo ? WaveguideType.SBend : WaveguideType.Auto);
        stored.IsRouteFrozen.ShouldBe(undo);
        if (undo)
        {
            stored.Path.Segments.Single().StartPoint.ShouldBe((10.0, 20.0));
            stored.BendRadiusOverrides[0].ShouldBe(12);
            stored.StraightShiftOffsets[0].ShouldBe(3);
        }
        else stored.Path.Segments.ShouldBeEmpty();
    }

    [Fact]
    public void RoutingFrame_PublishesGroupGeometryOnceAfterAllViewNotifications()
    {
        using var fixture = new GroupFixture();
        foreach (var wire in fixture.Canvas.Connections)
            wire.Connection.ReplaceRoutedPath(wire.Connection.RoutedPath!.TranslatedCopy(0, 100));
        var before = Positions(fixture.Group);
        var observed = ObserveCanonicalState(fixture, before);

        fixture.Canvas.Routing.NotifyRouteChanges();

        observed.Count.ShouldBe(2);
        observed.ShouldAllBe(unchanged => unchanged, "a routing frame must publish one complete group update");
        Positions(fixture.Group).ShouldBe(new[] { (10.0, 120.0), (10.0, 140.0) });
    }

    [Fact]
    public void HistorySnapshot_PublishesGroupGeometryOnceAfterAllViewNotifications()
    {
        using var fixture = new GroupFixture();
        var snapshot = RouteGeometrySnapshot.Capture(fixture.Canvas);
        foreach (var wire in fixture.Canvas.Connections)
        {
            wire.Connection.ReplaceRoutedPath(wire.Connection.RoutedPath!.TranslatedCopy(0, 100));
            wire.NotifyPathChanged();
        }
        var before = Positions(fixture.Group);
        var observed = ObserveCanonicalState(fixture, before);

        snapshot.Restore(fixture.Canvas);

        observed.Count.ShouldBe(2);
        observed.ShouldAllBe(unchanged => unchanged, "history must not rebuild group bounds once per restored wire");
        Positions(fixture.Group).ShouldBe(new[] { (10.0, 20.0), (10.0, 40.0) });
    }

    private static List<bool> ObserveCanonicalState(GroupFixture fixture, (double, double)[] before)
    {
        var observed = new List<bool>();
        foreach (var wire in fixture.Canvas.Connections)
            wire.PropertyChanged += (_, change) =>
            {
                if (change.PropertyName == nameof(WaveguideConnectionViewModel.PathLength))
                    observed.Add(Positions(fixture.Group).SequenceEqual(before));
            };
        return observed;
    }

    private static (double, double)[] Positions(ComponentGroup group) =>
        group.InternalPaths.Select(path => path.Path.Segments.Single().StartPoint).ToArray();

    private sealed class GroupFixture : IDisposable
    {
        internal DesignCanvasViewModel Canvas { get; } = new();
        internal ComponentGroup Group { get; } = new("route publication");

        internal GroupFixture()
        {
            for (var i = 0; i < 2; i++)
            {
                var start = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
                var end = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
                Group.AddChild(start);
                Group.AddChild(end);
                var path = new FrozenWaveguidePath
                {
                    StartPin = start.PhysicalPins[1], EndPin = end.PhysicalPins[0],
                    Path = new RoutedPath { Segments = { new StraightSegment(10, 20 + i * 20, 90, 20 + i * 20, 0) } },
                    ConnectionType = WaveguideType.SBend, IsRouteFrozen = true
                };
                path.BendRadiusOverrides[0] = 12;
                path.StraightShiftOffsets[0] = 3;
                Group.AddInternalPath(path);
            }
            Canvas.AddComponent(Group);
            Canvas.EnterGroupEditMode(Group);
        }

        public void Dispose()
        {
            Canvas.Routing.CancelRouting();
            Canvas.DisposeDocumentBindings();
        }
    }
}
