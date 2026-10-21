using Avalonia;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core.Components;
using CAP_Core.Components.ComponentHelpers;
using CAP_Core.Components.Core;
using CAP_Core.Components.Dies;
using CAP_Core.Components.PinKinds;

namespace CAP.Avalonia.ViewModels.Onboarding.FirstStepsTutorial;

/// <summary>A live, read-only snapshot of the optical chapter's prerequisites and precise targets.</summary>
internal sealed class OpticalTutorialState
{
    public int Stage { get; private set; }
    public ComponentTemplate? Template { get; private set; }
    public PhysicalPin? StartPin { get; private set; }
    public PhysicalPin? EndPin { get; private set; }
    public Point? PlacementPoint { get; private set; }
    public bool Connected { get; private set; }

    public void ClearTargets() { Template = null; StartPin = null; EndPin = null; PlacementPoint = null; }

    public static OpticalTutorialState Read(DesignCanvasViewModel canvas, CanvasInteractionViewModel? interaction,
        IEnumerable<ComponentTemplate>? templates, bool isOptics)
    {
        var state = new OpticalTutorialState();
        var parts = canvas.Components.Concat(canvas.Surface.Items).Where(c => c.Component is not ComponentGroup
            && c.Component.PhysicalPins.Any(p => !PinKindHelper.IsElectrical(p))).ToList();
        var source = parts.FirstOrDefault(c => c.IsLightSource && !c.IsLaserOff);
        var targetDie = source?.Component.ParentGroup as ComponentGroup
            ?? canvas.Dies.ActiveDie ?? canvas.Surface.LastWorkedDie ?? canvas.Surface.Dies.FirstOrDefault();
        bool Allowed(ComponentTemplate t) => t.PinDefinitions.Any(p => p.Kind != MatterType.Electricity)
            && interaction?.ComponentPlacementRefusal?.Invoke(t) == null
            && (interaction == null || (targetDie is { IsDie: true }
                ? interaction.PlacementContext.CheckPlacementOnDie(t.PdkSource, targetDie)
                : interaction.PlacementContext.CheckPlacementAt(t.PdkSource, targetDie)).IsAllowed);
        bool SourceTemplate(ComponentTemplate t) => Allowed(t) && LightSourceClassifier.IsLightInjectingCoupler(t.Name);
        bool ReceiverTemplate(ComponentTemplate t) => Allowed(t) && !LightSourceClassifier.IsLightInjectingCoupler(t.Name)
            && (source?.TemplatePdkSource == null || source.TemplatePdkSource == t.PdkSource);
        var available = templates?.ToList() ?? new List<ComponentTemplate>();
        bool Armed(Func<ComponentTemplate, bool> predicate) => interaction?.CurrentMode == InteractionMode.PlaceComponent
            && interaction.SelectedTemplate is { } selected && predicate(selected);
        if (source == null)
        {
            state.Template = available.Where(SourceTemplate)
                .OrderBy(s => available.Where(r => ReceiverTemplate(r) && r.PdkSource == s.PdkSource)
                    .Select(ReceiverRank).DefaultIfEmpty(3).Min())
                .ThenBy(s => EdgeCouplerFacet.IsEdgeCouplerTemplate(s.Name))
                .FirstOrDefault();
            state.Stage = Armed(SourceTemplate) ? 1 : 0;
            state.PlacementPoint = Placement(canvas, interaction?.SelectedTemplate, null, targetDie);
            return state;
        }

        var receivers = parts.Where(c => c != source && !c.IsLightSource
            && (source.TemplatePdkSource == null || c.TemplatePdkSource == null || c.TemplatePdkSource == source.TemplatePdkSource)).ToList();
        var connectedPins = new HashSet<(PhysicalPin, PhysicalPin)>();
        foreach (var connection in canvas.Connections.Select(c => c.Connection)
                     .Where(c => c.IsPathValid && !c.IsBlockedFallback))
        {
            connectedPins.Add((connection.StartPin, connection.EndPin));
            connectedPins.Add((connection.EndPin, connection.StartPin));
        }
        var pair = (from receiver in receivers
                    from start in source.Component.PhysicalPins.Where(p => !IsFacet(p))
                    from end in receiver.Component.PhysicalPins
                    where start.IsPlanarRoutable && end.IsPlanarRoutable
                        && !PinKindHelper.IsElectrical(start) && !PinKindHelper.IsElectrical(end)
                        && DieWiringPolicy.Evaluate(start, end).Verdict == DieWiringVerdict.Waveguide
                    let connected = connectedPins.Contains((start, end))
                    orderby connected descending, Distance(start, end)
                    select (start, end, connected)).FirstOrDefault();
        if (pair.start == null)
        {
            state.Template = available.Where(ReceiverTemplate).OrderBy(ReceiverRank).FirstOrDefault();
            state.Stage = Armed(ReceiverTemplate) ? 3 : 2;
            state.PlacementPoint = Placement(canvas, interaction?.SelectedTemplate, source, targetDie);
            return state;
        }
        state.StartPin = pair.start;
        state.EndPin = pair.end;
        state.Connected = pair.connected;
        state.Stage = pair.connected ? (isOptics ? 7 : 6) : interaction != null && interaction.CurrentMode != InteractionMode.Connect ? 4 : 5;
        return state;
    }

    private static double Distance(PhysicalPin a, PhysicalPin b)
    {
        var (ax, ay) = a.GetAbsolutePosition();
        var (bx, by) = b.GetAbsolutePosition();
        return Math.Pow(ax - bx, 2) + Math.Pow(ay - by, 2);
    }

    // These explicit external ports are for the incoming fiber, not the on-chip circuit.
    private static bool IsFacet(PhysicalPin pin) => pin.Name.Contains("fiber", StringComparison.OrdinalIgnoreCase)
        || pin.Name.Contains("facet", StringComparison.OrdinalIgnoreCase);

    private static int ReceiverRank(ComponentTemplate template) =>
        template.Name.Contains("detector", StringComparison.OrdinalIgnoreCase)
            || template.Name.Contains("photodiode", StringComparison.OrdinalIgnoreCase) ? 0
        : template.Name.Contains("straight", StringComparison.OrdinalIgnoreCase) ? 1 : 2;

    private static Point? Placement(DesignCanvasViewModel canvas, ComponentTemplate? template, ComponentViewModel? source, ComponentGroup? die)
    {
        if (template == null) return null;
        var bounds = die is { IsDie: true } ? DieAssembly.OutlineOf(die)
            : new DieOutline(canvas.ChipMinX, canvas.ChipMinY, canvas.ChipMaxX - canvas.ChipMinX, canvas.ChipMaxY - canvas.ChipMinY);
        double width = template.WidthMicrometers, height = template.HeightMicrometers;
        double x = source == null ? bounds.X + Math.Min(200, bounds.Width / 5) : source.X + source.Width + 250;
        double y = source?.Y ?? bounds.Y + Math.Min(200, bounds.Height / 5);
        var positions = new List<Point> { new(x, y) };
        // Prefer beside the source, then sample the entire usable die in bounded work.
        for (int row = 0; row <= 8; row++)
        for (int column = 0; column <= 8; column++)
            positions.Add(new Point(bounds.X + 20 + column * Math.Max(0, bounds.Width - width - 40) / 8,
                bounds.Y + 20 + row * Math.Max(0, bounds.Height - height - 40) / 8));
        foreach (var position in positions)
        {
            x = position.X;
            var candidateY = position.Y;
            if (!bounds.ContainsRect(x, candidateY, width, height)) continue;
            if (canvas.Components.Concat(canvas.Surface.Items).Where(c => c.Component != die).All(c => x + width + 20 <= c.X || x >= c.X + c.Width + 20
                || candidateY + height + 20 <= c.Y || candidateY >= c.Y + c.Height + 20))
                return new Point(x, candidateY);
        }
        return null;
    }
}
