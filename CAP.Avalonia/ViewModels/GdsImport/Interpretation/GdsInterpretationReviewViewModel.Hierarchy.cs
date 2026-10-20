using CAP_DataAccess.Import.Gds;

namespace CAP.Avalonia.ViewModels.GdsImport.Interpretation;

public sealed partial class GdsInterpretationReviewViewModel
{
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
