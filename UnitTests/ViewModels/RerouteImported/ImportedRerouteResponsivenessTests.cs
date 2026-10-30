using UnitTests.ViewModels.Canvas;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Canvas.RerouteImported;
using Shouldly;
using System.Diagnostics;
using CAP_Core.Routing;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using Xunit;
using Xunit.Abstractions;

namespace UnitTests.ViewModels.RerouteImported;

public partial class RerouteImportedRoutesViewModelTests
{
    private readonly ITestOutputHelper _output;

    public RerouteImportedRoutesViewModelTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task CancelGroupConversion_KeepsUncomputedImportedRoutesAndTheirMasks()
    {
        var canvas = ReportedRoutingWorker.CreateCanvas();
        var mask = new AsDrawnGeometry(new[] { new OutlinePolygon { Points = new[]
        {
            new OutlinePoint(0, 0), new OutlinePoint(100, 0), new OutlinePoint(100, 2)
        } } });
        for (int i = 0; i < 2; i++)
        {
            var (start, end) = AddComponentPair(canvas, i * 2000);
            ConnectFrozenImported(canvas, start, end).Connection.AttachAsDrawnGeometry(mask);
        }
        var commands = new CommandManager();
        commands.ExecuteCommand(new CreateGroupCommand(canvas, canvas.Components.ToList(), "Imported"));
        await canvas.RecalculateRoutesAsync();
        var group = (ComponentGroup)canvas.Components.Single().Component;
        group.InternalPaths.ShouldAllBe(path => path.IsRouteFrozen && ReferenceEquals(path.AsDrawnGeometry, mask),
            "the grouped baseline must retain both original imported masks before conversion starts");
        var routing = (ReportedRoutingWorker)canvas.Routing;
        routing.BlockWorker = true;
        var vm = new RerouteImportedRoutesViewModel(canvas, commands);
        var conversion = vm.RerouteAllCommand.ExecuteAsync(null);
        try
        {
            await routing.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            canvas.Routing.CancelRouting();
        }
        finally { routing.Release.TrySetResult(); await conversion.WaitAsync(TimeSpan.FromSeconds(30)); }
        group.InternalPaths.Count.ShouldBe(2, "cancellation must not discard uncomputed imported wires on group exit");
        foreach (var path in group.InternalPaths)
            _output.WriteLine($"After cancellation: frozen={path.IsRouteFrozen}, maskPresent={path.AsDrawnGeometry != null}, "
                + $"sameMask={ReferenceEquals(path.AsDrawnGeometry, mask)}, segments={path.Path.Segments.Count}");
        group.InternalPaths.ShouldContain(path => path.IsRouteFrozen && ReferenceEquals(path.AsDrawnGeometry, mask));
    }

    [Fact]
    public async Task GroupConversion_RepeatedRedoDoesNotReuseMutableHistoryGeometry()
    {
        var canvas = new DesignCanvasViewModel();
        var (start, end) = AddComponentPair(canvas, 0);
        var route = ConnectFrozenImported(canvas, start, end);
        route.Connection.BendRadiusMicrometers = 25;
        var mask = new AsDrawnGeometry(new[] { new OutlinePolygon { Points = new[]
        {
            new OutlinePoint(0, 0), new OutlinePoint(100, 0), new OutlinePoint(100, 2)
        } } });
        route.Connection.AttachAsDrawnGeometry(mask);
        var commands = new CommandManager();
        commands.ExecuteCommand(new CreateGroupCommand(canvas, canvas.Components.ToList(), "Imported"));
        await canvas.RecalculateRoutesAsync();
        var group = (ComponentGroup)canvas.Components.Single().Component;
        var vm = new RerouteImportedRoutesViewModel(canvas, commands);
        await vm.RerouteAllCommand.ExecuteAsync(null);
        var convertedLength = group.InternalPaths.Single().Path.TotalLengthMicrometers;
        for (int i = 0; i < 2; i++)
        {
            var live = group.InternalPaths.Single();
            live.Path.Segments[0].EndPoint = (9999, 9999);
            live.BendRadiusMicrometers = 999;
            live.AsDrawnGeometry = mask;
            commands.Undo().ShouldBeTrue();
            await canvas.RecalculateRoutesAsync();
            group.InternalPaths.Single().AsDrawnGeometry.ShouldBeSameAs(mask);
            group.InternalPaths.Single().BendRadiusMicrometers.ShouldBe(25);
            commands.Redo().ShouldBeTrue();
            await canvas.RecalculateRoutesAsync();
            group.InternalPaths.Single().Path.TotalLengthMicrometers.ShouldBe(convertedLength, 0.001);
            group.InternalPaths.Single().BendRadiusMicrometers.ShouldBe(25);
            group.InternalPaths.Single().AsDrawnGeometry.ShouldBeNull();
        }
    }

    [AvaloniaFact]
    public async Task LargeSourceConversion_KeepsDispatcherHeartbeatUnderBudget()
    {
        const int sourceSegments = 200_000;
        var canvas = new DesignCanvasViewModel();
        var (start, end) = AddComponentPair(canvas, 0);
        var route = ConnectFrozenImported(canvas, start, end);
        var source = route.Connection.RoutedPath!;
        var first = source.Segments[0];
        var detailed = new RoutedPath();
        for (int i = 0; i < sourceSegments; i++)
        {
            var x0 = first.StartPoint.X + (first.EndPoint.X - first.StartPoint.X) * i / sourceSegments;
            var x1 = first.StartPoint.X + (first.EndPoint.X - first.StartPoint.X) * (i + 1) / sourceSegments;
            detailed.Segments.Add(new StraightSegment(x0, first.StartPoint.Y, x1, first.EndPoint.Y, 0));
        }
        detailed.Segments.AddRange(source.Segments.Skip(1));
        route.Connection.RestoreCachedPath(detailed);
        var vm = new RerouteImportedRoutesViewModel(canvas, new CommandManager()) { SelectedConnection = route };
        var clock = Stopwatch.StartNew();
        double last = 0, maximumGap = 0;
        int pulses = 0;
        bool done = false;
        async Task Pulse()
        {
            while (!done)
            {
                await Task.Delay(1);
                var now = clock.Elapsed.TotalMilliseconds;
                maximumGap = Math.Max(maximumGap, now - last);
                last = now;
                pulses++;
            }
        }
        var heartbeat = Pulse();
        try { await vm.RerouteSelectedCommand.ExecuteAsync(null); }
        finally { done = true; await heartbeat; }
        var measurement = $"Source segments {sourceSegments}; conversion {clock.Elapsed.TotalMilliseconds:F1} ms; "
            + $"dispatcher pulses {pulses}; longest gap {maximumGap:F1} ms";
        _output.WriteLine(measurement);
        if (Environment.GetEnvironmentVariable("LUNIMA_ROUTING_PROBE_LOG") is { Length: > 0 } log)
            await File.WriteAllTextAsync(log, measurement);
        pulses.ShouldBeGreaterThan(1);
        maximumGap.ShouldBeLessThan(100, "source preparation and completion must keep processing UI work");
        route.Connection.IsRouteFrozen.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task ConvertImportedRoute_YieldsToPendingUiWorkBeforeMutatingGeometry()
    {
        var canvas = new DesignCanvasViewModel();
        var (start, end) = AddComponentPair(canvas, 0);
        var route = ConnectFrozenImported(canvas, start, end);
        var original = route.Connection.RoutedPath;
        var vm = new RerouteImportedRoutesViewModel(canvas, new CommandManager()) { SelectedConnection = route };
        bool heartbeatSawOriginal = false;
        Dispatcher.UIThread.Post(() => heartbeatSawOriginal =
            ReferenceEquals(route.Connection.RoutedPath, original), DispatcherPriority.Normal);
        await vm.RerouteSelectedCommand.ExecuteAsync(null);
        heartbeatSawOriginal.ShouldBeTrue("conversion preparation must yield before replacing the imported route");
    }

    [AvaloniaFact]
    public async Task ConvertImportedRoute_DeletedDuringPreparationDoesNotCreateHistory()
    {
        var canvas = new DesignCanvasViewModel();
        var (start, end) = AddComponentPair(canvas, 0);
        var route = ConnectFrozenImported(canvas, start, end);
        var commands = new CommandManager();
        var vm = new RerouteImportedRoutesViewModel(canvas, commands) { SelectedConnection = route };
        Dispatcher.UIThread.Post(() => canvas.Connections.Remove(route), DispatcherPriority.Normal);
        await vm.RerouteSelectedCommand.ExecuteAsync(null);
        commands.CanUndo.ShouldBeFalse();
        route.Connection.IsRouteFrozen.ShouldBeTrue("a stale conversion must leave its detached source intact");
    }
}
