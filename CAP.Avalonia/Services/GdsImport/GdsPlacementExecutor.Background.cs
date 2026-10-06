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
    /// <summary>A cell enclosing at least this many other placed cells is a frame, not a device.</summary>
    public const int MinEnclosedCellsForFrame = 2;

    /// <summary>Slack (µm) on the enclosure test so cells touching the frame edge still count.</summary>
    private const double FrameEnclosureSlackUm = 1.0;

    /// <summary>
    /// Marks the background components among <paramref name="placed"/> (index-aligned with
    /// the plan's placements) and drops their obstacles. A frame candidate that any planned
    /// connection uses is a real device (e.g. a composite block containing sub-cells) and
    /// stays an obstacle.
    /// </summary>
    private void MarkBackgroundComponents(GdsPlacementPlan plan, IReadOnlyList<ComponentViewModel?> placed)
    {
        var connected = plan.Connections
            .SelectMany(c => new[] { c.A.InstanceIndex, c.B.InstanceIndex })
            .ToHashSet();
        var components = placed.OfType<ComponentViewModel>().Select(vm => vm.Component).ToList();
        for (int i = 0; i < placed.Count; i++)
        {
            if (placed[i]?.Component is not { IsRoutingObstacle: true } component) continue;
            bool background = component.PhysicalPins.Count == 0
                || (!connected.Contains(i) && IsFrame(component, components));
            if (!background) continue;
            component.IsRoutingObstacle = false;
            _canvas.Router.RemoveComponentObstacle(component);
        }
    }

    /// <summary>True when <paramref name="candidate"/>'s box encloses at least <see cref="MinEnclosedCellsForFrame"/> other cells.</summary>
    internal static bool IsFrame(Component candidate, IReadOnlyList<Component> all)
    {
        double minX = candidate.PhysicalX - FrameEnclosureSlackUm, minY = candidate.PhysicalY - FrameEnclosureSlackUm;
        double maxX = candidate.PhysicalX + candidate.WidthMicrometers + FrameEnclosureSlackUm;
        double maxY = candidate.PhysicalY + candidate.HeightMicrometers + FrameEnclosureSlackUm;
        int enclosed = 0;
        foreach (var other in all)
        {
            if (ReferenceEquals(other, candidate)) continue;
            if (other.PhysicalX >= minX && other.PhysicalY >= minY
                && other.PhysicalX + other.WidthMicrometers <= maxX
                && other.PhysicalY + other.HeightMicrometers <= maxY
                && ++enclosed >= MinEnclosedCellsForFrame)
                return true;
        }
        return false;
    }
}
