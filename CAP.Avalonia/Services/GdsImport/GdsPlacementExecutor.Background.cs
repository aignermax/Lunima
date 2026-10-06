using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.Services.GdsImport;

/// <summary>
/// Background detection of <see cref="GdsPlacementExecutor"/>: imported geometry that
/// is not a device — pin-less cells (logos, marks) and die frames whose box encloses
/// other placed cells (a chip-sized "area" cell). As a routing obstacle or placement
/// collider such a component would wall off the whole chip: no route could be found
/// and nothing inside it could move. It stays visible and exportable.
/// </summary>
public sealed partial class GdsPlacementExecutor
{
    /// <summary>A frame must enclose at least this many cells that carry pins.</summary>
    public const int MinEnclosedCellsForFrame = 2;

    /// <summary>
    /// …and at least this share of all pin-carrying placed cells: a die frame holds the
    /// chip, whereas an unconnected test structure with a few marks inside is a device.
    /// </summary>
    public const double MinEnclosedShareForFrame = 0.5;

    /// <summary>Slack (µm) on the enclosure test so cells touching the frame edge still count.</summary>
    private const double FrameEnclosureSlackUm = 1.0;

    /// <summary>
    /// Marks the background components among <paramref name="placed"/> (index-aligned with
    /// the plan's placements), drops their obstacles and names the reclassified pin-carrying
    /// cells in the report — routes may then pass through them, so the user must know.
    /// A frame candidate that any planned connection uses is a real device and stays an obstacle.
    /// </summary>
    private void MarkBackgroundComponents(GdsPlacementPlan plan, IReadOnlyList<ComponentViewModel?> placed, GdsPlacementReport report)
    {
        var connected = plan.Connections
            .SelectMany(c => new[] { c.A.InstanceIndex, c.B.InstanceIndex })
            .ToHashSet();
        var devices = placed.OfType<ComponentViewModel>().Select(vm => vm.Component)
            .Where(c => c.PhysicalPins.Count > 0).ToList();
        var frames = new List<string>();
        for (int i = 0; i < placed.Count; i++)
        {
            if (placed[i]?.Component is not { IsRoutingObstacle: true } component) continue;
            bool pinLess = component.PhysicalPins.Count == 0;
            bool frame = !pinLess && !connected.Contains(i) && IsFrame(component, devices);
            if (!pinLess && !frame) continue;
            component.IsRoutingObstacle = false;
            _canvas.Router.RemoveComponentObstacle(component);
            if (frame) frames.Add(component.Identifier);
        }
        if (frames.Count > 0)
            report.Warnings.Add(
                $"Treated {frames.Count} enclosing cell(s) as background — no routing obstacle, no move collisions: {string.Join(", ", frames)}.");
    }

    /// <summary>
    /// True when <paramref name="candidate"/>'s box encloses at least
    /// <see cref="MinEnclosedCellsForFrame"/> of <paramref name="devices"/> (pin-carrying
    /// cells) and at least <see cref="MinEnclosedShareForFrame"/> of them.
    /// </summary>
    internal static bool IsFrame(Component candidate, IReadOnlyList<Component> devices)
    {
        double minX = candidate.PhysicalX - FrameEnclosureSlackUm, minY = candidate.PhysicalY - FrameEnclosureSlackUm;
        double maxX = candidate.PhysicalX + candidate.WidthMicrometers + FrameEnclosureSlackUm;
        double maxY = candidate.PhysicalY + candidate.HeightMicrometers + FrameEnclosureSlackUm;
        int others = 0, enclosed = 0;
        foreach (var other in devices)
        {
            if (ReferenceEquals(other, candidate)) continue;
            others++;
            if (other.PhysicalX >= minX && other.PhysicalY >= minY
                && other.PhysicalX + other.WidthMicrometers <= maxX
                && other.PhysicalY + other.HeightMicrometers <= maxY)
                enclosed++;
        }
        return enclosed >= MinEnclosedCellsForFrame && enclosed >= others * MinEnclosedShareForFrame;
    }
}
