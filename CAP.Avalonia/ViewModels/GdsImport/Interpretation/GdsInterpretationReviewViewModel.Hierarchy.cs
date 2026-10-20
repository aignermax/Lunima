using CAP_DataAccess.Import.Gds;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.GdsImport.Interpretation;

public sealed record GdsChildCellReviewRow(string CellName, long PlacementCount, int ChildTypeCount)
{
    public string Counts => string.Format(CAP.Avalonia.Services.Localization.LocalizationService.Instance
        .Translate("GdsReview.ChildCellCounts"), PlacementCount, ChildTypeCount);
}

public sealed partial class GdsInterpretationReviewViewModel
{
    private readonly List<string> _hierarchyPath = new();
    private IReadOnlyList<GdsChildCellReviewRow> _childCells = [];
    public IReadOnlyList<GdsChildCellReviewRow> ChildCells => _childCells;
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private GdsChildCellReviewRow? _selectedChild;
    public string HierarchyPath => string.Join(" / ", _hierarchyPath);
    public bool CanNavigateParent => _hierarchyPath.Count > 2;
    public bool CanOpenAssembly => SelectedCell?.CanExpand == true;
    public string CellHierarchyHint => Text(CanOpenAssembly ? "HierarchyChildrenHelp" : "HierarchyLeafHelp");

    [RelayCommand]
    private void OpenSelectedChild()
    {
        if (!IsCellStep || SelectedCell is not { CanExpand: true } parent || SelectedChild is not { } child ||
            !ChildCells.Contains(child)) return;
        // Exposing children changes the import boundary and records exactly one undo step.
        parent.ExpandChildren = true;
        _hierarchyPath.Add(child.CellName);
        SelectedCell = Cells.FirstOrDefault(row => row.CellName == child.CellName) ?? parent;
    }

    [RelayCommand]
    private void OpenAssembly()
    {
        SelectedChild ??= ChildCells.FirstOrDefault();
        OpenSelectedChild();
    }

    [RelayCommand]
    private void ParentAssembly()
    {
        if (!IsCellStep || !CanNavigateParent) return;
        SelectedCell = Cells.FirstOrDefault(row => row.CellName == _hierarchyPath[^2]);
    }

    private void RefreshHierarchyPresentation()
    {
        var name = SelectedCell?.CellName;
        if (_hierarchyPath.LastOrDefault() != name)
        {
            var existing = name == null ? -1 : _hierarchyPath.IndexOf(name);
            if (existing >= 0) _hierarchyPath.RemoveRange(existing + 1, _hierarchyPath.Count - existing - 1);
            else
            {
                _hierarchyPath.Clear();
                var visited = new HashSet<string>(StringComparer.Ordinal);
                bool Find(string current)
                {
                    if (!visited.Add(current)) return false;
                    _hierarchyPath.Add(current);
                    if (current == name) return true;
                    foreach (var reference in _library.Cells[current].Elements.OfType<GdsReference>())
                        if (Find(reference.CellName)) return true;
                    _hierarchyPath.RemoveAt(_hierarchyPath.Count - 1);
                    return false;
                }
                if (name != null) Find(_topCell);
            }
        }
        _childCells = name == null ? [] : _library.Cells[name].Elements.OfType<GdsReference>()
            .GroupBy(reference => reference.CellName, StringComparer.Ordinal)
            .Select(group => new GdsChildCellReviewRow(group.Key,
                group.Sum(reference => (long)reference.Columns * reference.Rows),
                _library.Cells[group.Key].Elements.OfType<GdsReference>().Select(reference => reference.CellName)
                    .Distinct(StringComparer.Ordinal).Count())).ToArray();
        var nextChild = _childCells.FirstOrDefault(child => child.CellName == SelectedChild?.CellName)
            ?? _childCells.FirstOrDefault();
        OnPropertyChanged(nameof(ChildCells));
        SelectedChild = nextChild;
        OnPropertyChanged(nameof(HierarchyPath));
        OnPropertyChanged(nameof(CanNavigateParent));
    }

    /// <summary>Selects the exposed component containing a clicked source cell.</summary>
    public void PickCell(IReadOnlyList<string> cellPath)
    {
        if (!IsCellStep) return;
        var visible = DisplayCells;
        var selected = cellPath.Reverse().Select(name => visible.FirstOrDefault(x => x.CellName == name))
            .FirstOrDefault(row => row != null);
        if (selected != null) SelectedCell = selected;
    }

    /// <summary>Only exposed cells can be edited as independently placed components.</summary>
    public IReadOnlyList<GdsCellReviewRow> DisplayCells => IsCellStep ? VisibleHierarchy() : ActiveCells;
    public IReadOnlyList<GdsCellReviewRow> ActiveCells => VisibleHierarchy().Where(x => !x.ExpandChildren).ToArray();

    private IReadOnlyList<GdsCellReviewRow> VisibleHierarchy()
    {
        var result = new List<GdsCellReviewRow>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        void Visit(string name)
        {
            if (!visited.Add(name)) return;
            var row = Cells.FirstOrDefault(x => x.CellName == name);
            if (row == null) return;
            result.Add(row);
            if (row.ExpandChildren)
                foreach (var reference in _library.Cells[name].Elements.OfType<GdsReference>())
                    Visit(reference.CellName);
        }
        var children = _library.Cells[_topCell].Elements.OfType<GdsReference>().ToArray();
        if (children.Length == 0) Visit(_topCell);
        else foreach (var child in children) Visit(child.CellName);
        return result;
    }

    private void RefreshCellSelection()
    {
        var available = DisplayCells;
        if (SelectedCell == null || !available.Contains(SelectedCell))
            SelectedCell = available.FirstOrDefault();
        OnPropertyChanged(nameof(DisplayCells));
        OnPropertyChanged(nameof(ActiveCells));
    }
}
