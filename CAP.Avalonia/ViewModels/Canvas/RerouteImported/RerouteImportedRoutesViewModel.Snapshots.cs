using CAP.Avalonia.Commands;
using CAP_Core.Components.Core;
using CAP_Core.Routing;

namespace CAP.Avalonia.ViewModels.Canvas.RerouteImported;

public partial class RerouteImportedRoutesViewModel
{
    // Workers own these scalar snapshots; they never traverse live segment collections.
    private sealed record RouteCopyInput(RoutedPath Flags, SegmentValue[] Segments)
    {
        public static RouteCopyInput Capture(RoutedPath path) => new(new RoutedPath
        {
            IsBlockedFallback = path.IsBlockedFallback,
            IsInvalidGeometry = path.IsInvalidGeometry,
            IsPlaceholderGeometry = path.IsPlaceholderGeometry,
            FailureReason = path.FailureReason,
            ViolatesProcessMinBendRadius = path.ViolatesProcessMinBendRadius,
            PassesThroughComponent = path.PassesThroughComponent,
            IsDirectStyledRoute = path.IsDirectStyledRoute,
            DirectStyle = path.DirectStyle
        }, path.Segments.Select(SegmentValue.Capture).ToArray());

        public RoutedPath Build()
        {
            var path = Flags.DeepCopy();
            foreach (var segment in Segments)
                path.Segments.Add(segment.Build());
            return path;
        }
    }

    private readonly record struct SegmentValue(bool IsBend, double X, double Y, double A, double B, double Angle)
    {
        public static SegmentValue Capture(PathSegment segment) => segment is BendSegment bend
            ? new(true, bend.Center.X, bend.Center.Y, bend.RadiusMicrometers, bend.SweepAngleDegrees, bend.StartAngleDegrees)
            : new(false, segment.StartPoint.X, segment.StartPoint.Y, segment.EndPoint.X, segment.EndPoint.Y, segment.StartAngleDegrees);

        public PathSegment Build() => IsBend
            ? new BendSegment(X, Y, A, Angle, B)
            : new StraightSegment(X, Y, A, B, Angle);
    }

    internal static Func<FrozenWaveguidePath> CaptureTopologyPath(FrozenWaveguidePath source) => CaptureFrozenPath(source);

    private static Func<FrozenWaveguidePath> CaptureFrozenPath(FrozenWaveguidePath source, RouteCopyInput? capturedGeometry = null)
    {
        var geometry = capturedGeometry ?? RouteCopyInput.Capture(source.Path);
        var drawn = source.AsDrawnGeometry;
        var settings = new FrozenWaveguidePath
        {
            Path = new RoutedPath(), PathId = source.PathId,
            StartPin = source.StartPin, EndPin = source.EndPin
        };
        settings.CopySettingsFrom(source);
        return () =>
        {
            var copy = new FrozenWaveguidePath
            {
                Path = geometry.Build(), PathId = settings.PathId,
                StartPin = settings.StartPin, EndPin = settings.EndPin
            };
            copy.CopySettingsFrom(settings);
            copy.AsDrawnGeometry = drawn;
            return copy;
        };
    }

    private static async Task<(List<RerouteImportedRoutesStateCommand.ConnectionState> Connections,
        List<RerouteImportedRoutesStateCommand.GroupState> Groups, List<FrozenWaveguidePath>? Scope)> PrepareSnapshotsAsync(
        IReadOnlyList<WaveguideConnectionViewModel> targets,
        IReadOnlyList<(ComponentGroup Group, List<FrozenWaveguidePath> Paths)> groups,
        IReadOnlyList<Func<FrozenWaveguidePath>>? scope, RerouteImportedRootTopology? topology)
    {
        var connections = targets.Select(t => (t.Connection,
            Path: RouteCopyInput.Capture(t.Connection.RoutedPath!),
            Frozen: t.Connection.IsRouteFrozen, Drawn: t.Connection.AsDrawnGeometry,
            Radius: t.Connection.BendRadiusMicrometers)).ToArray();
        var groupInputs = groups.Select(g => (g.Group, Targets: g.Paths.Select(path => (path.StartPin, path.EndPin)).ToArray(), Children: g.Group.ChildComponents.ToArray(),
            Paths: g.Group.InternalPaths.Select(path => CaptureFrozenPath(path)).ToArray())).ToArray();
        await Task.Yield();
        return await Task.Run(() =>
        {
            topology?.Prepare();
            return (
            connections.Select(c => new RerouteImportedRoutesStateCommand.ConnectionState
            {
                Connection = c.Connection, OldPath = c.Path.Build(),
                OldIsFrozen = c.Frozen, OldAsDrawn = c.Drawn, OldBendRadius = c.Radius
            }).ToList(),
            groupInputs.Select(g => new RerouteImportedRoutesStateCommand.GroupState
            {
                Group = g.Group, TargetPins = g.Targets, OldChildren = g.Children, OldPaths = g.Paths.Select(build => build()).ToList()
            }).ToList(), scope?.Select(build => build()).ToList());
        });
    }

    private void UnfreezeTargets(IReadOnlyList<WaveguideConnectionViewModel> targets)
    {
        foreach (var target in targets)
        {
            target.Connection.PreserveImportedBendRadius();
            target.Connection.IsRouteFrozen = false;
            target.Connection.InvalidateRoute();
        }
        _canvas.InvalidateSimulation();
    }

    private static void CaptureFinalSnapshots(
        IReadOnlyList<RerouteImportedRoutesStateCommand.ConnectionState> connections,
        IReadOnlyList<RerouteImportedRoutesStateCommand.GroupState> groups)
    {
        foreach (var state in connections)
        {
            state.NewIsFrozen = state.Connection.IsRouteFrozen;
            state.NewBendRadius = state.Connection.BendRadiusMicrometers;
            state.NewAsDrawn = state.Connection.AsDrawnGeometry;
            state.CreateNewPath = RouteCopyInput.Capture(state.Connection.RoutedPath!).Build;
        }
        foreach (var state in groups)
        {
            state.NewChildren = state.Group.ChildComponents.ToArray();
            var paths = state.Group.InternalPaths.Select(path => CaptureFrozenPath(path)).ToArray();
            state.NewPathCount = paths.Length;
            state.CreateNewPaths = () => paths.Select(build => build()).ToList();
        }
    }

    private List<Func<FrozenWaveguidePath>> CaptureScopePaths(ComponentGroup scope)
    {
        var paths = scope.InternalPaths.Where(path => path.StartPin == null || path.EndPin == null)
            .Select(path => CaptureFrozenPath(path)).ToList();
        foreach (var vm in _canvas.Connections)
        {
            var connection = vm.Connection;
            if (connection.RoutedPath == null) continue;
            var settings = new FrozenWaveguidePath
            {
                StartPin = connection.StartPin, EndPin = connection.EndPin, Path = new RoutedPath()
            };
            settings.CaptureSettingsFrom(connection);
            paths.Add(CaptureFrozenPath(settings, RouteCopyInput.Capture(connection.RoutedPath)));
        }
        return paths;
    }
}
