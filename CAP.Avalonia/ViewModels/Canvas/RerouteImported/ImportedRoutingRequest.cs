using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.LightCalculation.BendLoss;
using CAP_Core.LightCalculation.MaterialDispersion;
using CAP_Core.Routing;

namespace CAP.Avalonia.ViewModels.Canvas.RerouteImported;

/// <summary>Retries an uncomputed conversion once after an unrelated edit, using a fresh private routing pass.</summary>
internal static class ImportedRoutingRequest
{
    internal static async Task RunAsync(DesignCanvasViewModel canvas,
        IReadOnlyList<WaveguideConnectionViewModel> targets, Func<bool> stopped)
    {
        var document = canvas.Document.CaptureIdentity();
        var scope = canvas.CurrentEditGroup;
        var generation = canvas.Routing.MutationGeneration;
        var intents = targets.Select(target => new TargetIntent(target)).ToArray();
        var navigated = false;
        void ScopeChanged(ComponentGroup? _) => navigated = true;
        canvas.Groups.CurrentEditGroupChanging += ScopeChanged;
        try
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                if (stopped() || navigated || !canvas.Document.IsCurrentIdentity(document)
                    || !ReferenceEquals(canvas.CurrentEditGroup, scope) || generation != canvas.Routing.MutationGeneration
                    || !intents.All(intent => intent.Matches(canvas))) return;
                await using var operation = canvas.Document.CaptureOperation(document);
                await canvas.Routing.RecalculateRoutesAsync(operation);
                if (operation.IsCurrent) return;
                // A foreign edit revoked this snapshot. Never publish it; re-admit only the same untouched intent.
            }
        }
        finally { canvas.Groups.CurrentEditGroupChanging -= ScopeChanged; }
    }

    private sealed class TargetIntent(WaveguideConnectionViewModel target)
    {
        private readonly IntentValues _values = IntentValues.Capture(target.Connection);
        private readonly KeyValuePair<int, double>[] _radii = target.Connection.BendRadiusOverrides.ToArray();
        private readonly KeyValuePair<int, double>[] _shifts = target.Connection.StraightShiftOffsets.ToArray();

        internal bool Matches(DesignCanvasViewModel canvas) => canvas.Connections.Contains(target)
            && _values == IntentValues.Capture(target.Connection)
            && Equal(_radii, target.Connection.BendRadiusOverrides) && Equal(_shifts, target.Connection.StraightShiftOffsets);

        private static bool Equal(KeyValuePair<int, double>[] expected, Dictionary<int, double> actual) =>
            expected.Length == actual.Count && expected.All(pair => actual.TryGetValue(pair.Key, out var value) && value == pair.Value);
    }

    // These editor-side guards contain no copied route polygons and are never sent to a worker.
    private sealed record IntentValues(Guid Id, PhysicalPin Start, PhysicalPin End,
        (double x, double y) StartPosition, (double x, double y) EndPosition, double StartAngle, double EndAngle,
        WaveguideType Type, double Width, double Radius, bool Frozen, bool Locked,
        double? Length, double? Tolerance, RoutedPath? Path, AsDrawnGeometry? Drawn,
        IDispersionModel? Dispersion, BendLossModel? BendLoss, double PropagationLoss, double ExcessLoss)
    {
        internal static IntentValues Capture(WaveguideConnection wire) => new(wire.Id, wire.StartPin, wire.EndPin,
            wire.StartPin.GetAbsolutePosition(), wire.EndPin.GetAbsolutePosition(),
            wire.StartPin.GetAbsoluteAngle(), wire.EndPin.GetAbsoluteAngle(), wire.Type, wire.WidthMicrometers,
            wire.BendRadiusMicrometers, wire.IsRouteFrozen, wire.IsLocked, wire.TargetLengthMicrometers,
            wire.LengthToleranceMicrometers, wire.RoutedPath, wire.AsDrawnGeometry, wire.DispersionModel,
            wire.BendLoss, wire.PropagationLossDbPerCm, wire.ExcessBendLossDbPer90Deg);
    }
}
