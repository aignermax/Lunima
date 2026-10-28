using System.Collections.Immutable;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;

namespace CAP_Core.Analysis;

public readonly record struct LayoutPosition(double X, double Y);
public sealed record LayoutCompressionFrame(int Iteration, ImmutableArray<LayoutPosition> Positions);

/// <summary>Geometry and connectivity values captured before background compression starts.</summary>
public sealed class LayoutCompressionSnapshot
{
    private readonly ImmutableArray<LayoutGeometry> _geometry;
    private readonly ImmutableArray<(int Start, int End)> _edges;

    private LayoutCompressionSnapshot(ImmutableArray<LayoutGeometry> geometry,
        ImmutableArray<(int Start, int End)> edges) => (_geometry, _edges) = (geometry, edges);

    public static LayoutCompressionSnapshot Capture(IReadOnlyList<Component> components,
        IReadOnlyList<WaveguideConnection> connections) => new(
        components.Select(c => new LayoutGeometry(c.PhysicalX, c.PhysicalY,
            c.WidthMicrometers, c.HeightMicrometers, c.IsLocked)).ToImmutableArray(),
        CaptureEdges(components, connections));

    internal static ImmutableArray<(int Start, int End)> CaptureEdges(IReadOnlyList<Component> components,
        IReadOnlyList<WaveguideConnection> connections)
    {
        var indices = components.Select((component, index) => (component, index))
            .ToDictionary(pair => pair.component, pair => pair.index);
        var edges = ImmutableArray.CreateBuilder<(int, int)>();
        foreach (var connection in connections)
            if (indices.TryGetValue(connection.StartPin.ParentComponent, out var start)
                && indices.TryGetValue(connection.EndPin.ParentComponent, out var end))
                edges.Add((start, end));
        return edges.ToImmutable();
    }

    public LayoutCompressionFrame Solve(CancellationToken cancellationToken,
        IProgress<LayoutCompressionFrame>? progress = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bodies = _geometry.Select(value => new LayoutCompressionBody
        {
            PhysicalX = value.X, PhysicalY = value.Y, WidthMicrometers = value.Width,
            HeightMicrometers = value.Height, IsLocked = value.Locked
        }).ToList();
        var result = new LayoutCompressionSolver().CompressLayout(bodies, _edges,
            (iteration, positions) => progress?.Report(new(iteration,
                bodies.Select(body => new LayoutPosition(positions[body].X, positions[body].Y)).ToImmutableArray())),
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return new(100, bodies.Select(body => new LayoutPosition(result[body].X, result[body].Y)).ToImmutableArray());
    }

    private readonly record struct LayoutGeometry(double X, double Y, double Width, double Height, bool Locked);
}

public interface ILayoutCompressionRunner
{
    Task<LayoutCompressionFrame> RunAsync(LayoutCompressionSnapshot snapshot, CancellationToken cancellationToken,
        IProgress<LayoutCompressionFrame>? progress);
}

public sealed class LayoutCompressionRunner : ILayoutCompressionRunner
{
    public Task<LayoutCompressionFrame> RunAsync(LayoutCompressionSnapshot snapshot, CancellationToken cancellationToken,
        IProgress<LayoutCompressionFrame>? progress) => Task.Run(() => snapshot.Solve(cancellationToken, progress), cancellationToken);
}

/// <summary>Mutable calculation state, private to one invocation of the force solver.</summary>
internal sealed class LayoutCompressionBody
{
    internal double PhysicalX { get; set; }
    internal double PhysicalY { get; set; }
    internal double WidthMicrometers { get; set; }
    internal double HeightMicrometers { get; set; }
    internal bool IsLocked { get; set; }

    internal static LayoutCompressionBody Capture(Component component)
    {
        var body = new LayoutCompressionBody();
        body.ReadFrom(component);
        return body;
    }

    internal void ReadFrom(Component component)
    {
        PhysicalX = component.PhysicalX;
        PhysicalY = component.PhysicalY;
        WidthMicrometers = component.WidthMicrometers;
        HeightMicrometers = component.HeightMicrometers;
        IsLocked = component.IsLocked;
    }
}
