using CAP.Avalonia.Services.GdsImport;
using CAP_DataAccess.Import.Gds;
using CAP_DataAccess.Import.Gds.Interpretation;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.GdsImport.Interpretation;

public sealed record GdsReviewOccurrence(string InstanceName, string CellName, GdsTransform Transform, GdsBoundingBox Bounds);

public sealed partial class GdsInterpretationReviewViewModel
{
    private readonly List<GdsReviewOccurrence> _occurrences = [];
    private readonly HashSet<string> _selectedOccurrences = new(StringComparer.Ordinal);
    private string? _occurrenceSignature;
    public IReadOnlyList<GdsReviewOccurrence> Occurrences => _occurrences;
    public IReadOnlyList<GdsReviewOccurrence> SelectedOccurrences => _occurrences.Where(x => _selectedOccurrences.Contains(x.InstanceName)).ToArray();
    public IReadOnlyList<GdsBoundingBox> SelectedOccurrenceBounds => IsCellStep ? SelectedOccurrences.Select(x => x.Bounds).ToArray() : [];
    public string OccurrenceSelectionText => string.Format(Text("OccurrenceSelection"), _selectedOccurrences.Count);
    public bool OccurrenceSelectionLimited { get; private set; }
    public bool CanCreateOccurrenceGroup => _selectedOccurrences.Count >= 2 && !string.IsNullOrWhiteSpace(GroupName)
        && (!OccurrenceSelectionLimited || _groups.All(g => g.InstanceNames.Count > 0))
        && !_groups.Any(g => g.Name == GroupName.Trim() && _selectedOccurrences.SetEquals(g.InstanceNames));

    private void RefreshOccurrences()
    {
        var expanded = Cells.Where(x => x.ExpandChildren).Select(x => x.CellName).ToHashSet(StringComparer.Ordinal);
        var signature = string.Join("\0", expanded.Order(StringComparer.Ordinal));
        if (_occurrenceSignature == signature) return;
        _occurrenceSignature = signature;
        _occurrences.Clear();
        _selectedOccurrences.Clear();
        var counters = new Dictionary<string, int>(StringComparer.Ordinal);
        int visited = 0;
        foreach (var reference in _library.Cells[_topCell].Elements.OfType<GdsReference>())
            Visit(reference, GdsTransform.Identity, new HashSet<string>(StringComparer.Ordinal));
        OccurrenceSelectionLimited = _occurrences.Count >= 100_000 || visited > 200_000;
        OnPropertyChanged(nameof(OccurrenceSelectionLimited));
        OnPropertyChanged(nameof(Occurrences));
        NotifyOccurrenceSelection();

        void Visit(GdsReference reference, GdsTransform outer, HashSet<string> path)
        {
            // Keep review navigation bounded even for pathological hierarchy arrays.
            for (int row = 0; row < reference.Rows && _occurrences.Count < 100_000; row++)
            for (int column = 0; column < reference.Columns && _occurrences.Count < 100_000; column++)
            {
                if (++visited > 200_000) return;
                var transform = GdsTransform.FromReference(reference, column, row).Then(outer);
                if (expanded.Contains(reference.CellName))
                {
                    if (!path.Add(reference.CellName)) return;
                    foreach (var child in _library.Cells[reference.CellName].Elements.OfType<GdsReference>()) Visit(child, transform, path);
                    path.Remove(reference.CellName);
                    continue;
                }
                var bounds = _sceneBuilder.GetBounds(reference.CellName);
                if (bounds.Width <= 0 && bounds.Height <= 0) continue;
                counters.TryGetValue(reference.CellName, out int count);
                counters[reference.CellName] = count + 1;
                var points = new[] { new GdsPoint(bounds.MinX, bounds.MinY), new GdsPoint(bounds.MaxX, bounds.MinY),
                    new GdsPoint(bounds.MaxX, bounds.MaxY), new GdsPoint(bounds.MinX, bounds.MaxY) }.Select(transform.Apply).ToArray();
                _occurrences.Add(new($"{reference.CellName}#{count}", reference.CellName, transform,
                    new(points.Min(p => p.X), points.Min(p => p.Y), points.Max(p => p.X), points.Max(p => p.Y))));
            }
        }
    }

    public void PickPlacement(IReadOnlyList<GdsReviewCellPlacement> path, bool additive)
    {
        if (!IsCellStep) return;
        RefreshOccurrences();
        var occurrence = path.Reverse().Select(hit => _occurrences.FirstOrDefault(x => x.CellName == hit.CellName && x.Transform == hit.Transform))
            .FirstOrDefault(x => x != null);
        if (occurrence == null) return;
        if (!additive) _selectedOccurrences.Clear();
        if (!additive || !_selectedOccurrences.Remove(occurrence.InstanceName)) _selectedOccurrences.Add(occurrence.InstanceName);
        SelectedCell = Cells.First(x => x.CellName == occurrence.CellName);
        NotifyOccurrenceSelection();
    }

    [RelayCommand] private void RemoveOccurrence(GdsReviewOccurrence occurrence)
    {
        if (occurrence == null) return;
        _selectedOccurrences.Remove(occurrence.InstanceName);
        NotifyOccurrenceSelection();
    }
    [RelayCommand] private void ClearOccurrenceSelection() { _selectedOccurrences.Clear(); NotifyOccurrenceSelection(); }
    [RelayCommand(CanExecute = nameof(CanCreateOccurrenceGroup))]
    private void CreateOccurrenceGroup()
    {
        if (!CanCreateOccurrenceGroup) return;
        RecordEdit();
        var selected = SelectedOccurrences;
        for (int i = _groups.Count - 1; i >= 0; i--)
        {
            var group = _groups[i];
            if (group.InstanceNames.Count == 0)
            {
                if (!group.CellNames.Any(n => selected.Any(x => x.CellName == n))) continue;
                // Resolve legacy type-wide groups before subtracting concrete members.
                group = group with { CellNames = [], InstanceNames = _occurrences
                    .Where(x => group.CellNames.Contains(x.CellName)).Select(x => x.InstanceName).ToList() };
            }
            var remaining = group.InstanceNames.Where(n => !_selectedOccurrences.Contains(n)).ToList();
            if (remaining.Count == group.InstanceNames.Count) continue;
            if (remaining.Count < 2) _groups.RemoveAt(i);
            else _groups[i] = group with { InstanceNames = remaining };
        }
        _groups.Add(new GdsInterpretationGroup { Name = GroupName.Trim(), InstanceNames = selected.Select(x => x.InstanceName).ToList() });
        FinishEdit(); NotifyOccurrenceSelection(); Message = Text("GroupCreated");
    }
    partial void OnGroupNameChanged(string value) => NotifyOccurrenceSelection();
    private void NotifyOccurrenceSelection()
    {
        OnPropertyChanged(nameof(SelectedOccurrences)); OnPropertyChanged(nameof(SelectedOccurrenceBounds));
        OnPropertyChanged(nameof(GuideTargetName));
        OnPropertyChanged(nameof(OccurrenceSelectionText)); OnPropertyChanged(nameof(HighlightCellName));
        OnPropertyChanged(nameof(CanCreateOccurrenceGroup)); CreateOccurrenceGroupCommand.NotifyCanExecuteChanged();
    }
}
