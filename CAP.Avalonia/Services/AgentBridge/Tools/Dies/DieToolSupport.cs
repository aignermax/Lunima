using CAP.Avalonia.Commands.Dies;
using CAP_Core.Components.Core;
using CAP_Core.Components.Dies;
using CAP_Core.Components.Process;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Dies;

/// <summary>Lookups and JSON shapes shared by the die tools.</summary>
internal static class DieToolSupport
{
    /// <summary>Wavelength link losses are reported at (standard C-band, as on the canvas).</summary>
    public const double ReportWavelengthNm = 1550.0;

    /// <summary>The die named <paramref name="name"/> (case-insensitive) at the assembly root, or null.</summary>
    public static ComponentGroup? FindDie(AgentDesignSession session, string? name) =>
        session.Canvas.Dies.RootDies.FirstOrDefault(d =>
            string.Equals(d.GroupName, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The process whose display name or member PDK matches <paramref name="name"/>, or null.</summary>
    public static ActiveProcessSelection? FindProcess(AgentDesignSession session, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        var group = session.Processes.FirstOrDefault(g =>
                        string.Equals(g.DisplayName, name, StringComparison.OrdinalIgnoreCase))
                    ?? session.Processes.FirstOrDefault(g =>
                        g.MemberPdkNames.Contains(name, StringComparer.OrdinalIgnoreCase));
        return group == null ? null : ActiveProcessSelection.ForGroup(group);
    }

    /// <summary>Names of the processes the agent may choose from.</summary>
    public static string ProcessNames(AgentDesignSession session) =>
        string.Join(", ", session.Processes.Select(p => $"'{p.DisplayName}'"));

    /// <summary>JSON shape of one die.</summary>
    public static object Describe(ComponentGroup die)
    {
        var outline = DieAssembly.OutlineOf(die);
        return new
        {
            Name = die.GroupName,
            Process = die.ProcessBinding?.DisplayName,
            Outline = new { outline.X, outline.Y, outline.Width, outline.Height },
            EnabledPdks = die.Die!.EnabledPdkNames ?? die.ProcessBinding?.MemberPdkNames ?? Array.Empty<string>(),
            EdgeZone = die.Die.EdgeZoneMicrometers,
            Parts = die.ChildComponents.Count,
            EdgeCouplerFacets = DieLinkSnapper.EdgeFacets(die).Count,
        };
    }

    /// <summary>JSON shape of the design's die-to-die links.</summary>
    public static IEnumerable<object> Links(AgentDesignSession session) =>
        session.Canvas.ConnectionManager.Connections
            .Where(c => c.IsCrossChipletFacetLink)
            .Select(link => (object)new
            {
                From = DieAssembly.DieOf(link.StartPin.ParentComponent)?.GroupName,
                To = DieAssembly.DieOf(link.EndPin.ParentComponent)?.GroupName,
                GapMicrometers = Math.Round(DieLinkGeometry.Gap(link), 4),
                LateralOffsetMicrometers = Math.Round(DieLinkGeometry.LateralOffset(link), 4),
                link.FacetGapMediumIndex,
                LossDb = DieLinkGeometry.LossDecibels(link, ReportWavelengthNm) is { } loss ? Math.Round(loss, 3) : (double?)null,
            });
}
