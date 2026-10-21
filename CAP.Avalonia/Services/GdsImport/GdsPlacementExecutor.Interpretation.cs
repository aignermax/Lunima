using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;

namespace CAP.Avalonia.Services.GdsImport;

public sealed partial class GdsPlacementExecutor
{
    private static void ValidateReviewedGroups(GdsPlacementPlan plan)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in plan.Groups)
        {
            if (string.IsNullOrWhiteSpace(group.Name))
                throw new InvalidDataException("A reviewed group must have a name.");
            var names = group.InstanceNames.ToHashSet(StringComparer.Ordinal);
            var cells = group.CellNames.ToHashSet(StringComparer.Ordinal);
            foreach (var placement in plan.Placements.Where(p => names.Count > 0
                ? names.Contains(p.InstanceName) : cells.Contains(p.CellName)))
                if (!seen.Add(placement.InstanceName))
                    throw new InvalidDataException($"Instance '{placement.InstanceName}' belongs to more than one reviewed group.");
        }
    }

    private IReadOnlyList<ComponentViewModel?> ApplyReviewedGroups(GdsPlacementPlan plan,
        IReadOnlyList<ComponentViewModel?> placed, GdsPlacementReport report)
    {
        var candidates = placed.ToList();
        foreach (var group in plan.Groups)
        {
            var names = group.CellNames.ToHashSet(StringComparer.Ordinal);
            var occurrences = group.InstanceNames.ToHashSet(StringComparer.Ordinal);
            var members = placed.Where((_, i) => occurrences.Count > 0
                    ? occurrences.Contains(plan.Placements[i].InstanceName)
                    : names.Contains(plan.Placements[i].CellName))
                .OfType<ComponentViewModel>().ToList();
            if (members.Count < 2)
            {
                report.Warnings.Add($"Group '{group.Name}' needs at least two placed instances; its members remain individually editable.");
                continue;
            }
            var command = new CreateGroupCommand(_canvas, members, group.Name);
            if (_commandManager is not null)
                _commandManager.ExecuteCommand(command);
            else
                command.Execute();
            if (command.CreatedGroup is null)
                continue;
            candidates.RemoveAll(c => c is not null && members.Contains(c));
            candidates.Add(_canvas.Components.First(c => ReferenceEquals(c.Component, command.CreatedGroup)));
        }
        return candidates;
    }
}
