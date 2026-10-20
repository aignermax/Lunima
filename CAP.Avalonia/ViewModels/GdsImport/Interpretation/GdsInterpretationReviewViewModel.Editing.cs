using CAP.Avalonia.Services.GdsImport;
using CAP_DataAccess.Import.Gds;
using CAP_DataAccess.Import.Gds.Interpretation;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.GdsImport.Interpretation;

public sealed partial class GdsInterpretationReviewViewModel
{
    public GdsImportInterpretation BuildInterpretation() => new()
    {
        SourceSha256 = _sourceHash,
        TopCellName = _topCell,
        Layers = Layers.Select(x => new GdsLayerAssignment(x.Layer, x.Datatype, x.Role)).ToList(),
        Cells = Cells.Select(x => new GdsCellInterpretation
        {
            CellName = x.CellName,
            DisplayName = x.DisplayName.Trim(),
            ExpandChildren = x.ExpandChildren,
            Pins = x.Ports.Select(p => p.ToPin()).ToList(),
            ModelPdkName = x.SelectedModel?.PdkName,
            ModelComponentName = x.SelectedModel?.ComponentName,
            ModelPinMap = x.Ports.Where(p => !string.IsNullOrWhiteSpace(p.ModelPortName))
                .GroupBy(p => p.Name.Trim()).ToDictionary(g => g.Key, g => g.First().ModelPortName.Trim())
        }).ToList(),
        Groups = _groups.ToList()
    };

    [RelayCommand]
    private async Task Next()
    {
        if (IsWorking || _lifetime.IsCancellationRequested) return;
        if (StageIndex == 5)
        {
            var missing = Array.FindIndex(_confirmed, 0, 5, x => !x);
            if (missing >= 0) { StageIndex = missing; Message = Text("ConfirmEarlier"); return; }
            var error = Validate();
            if (error != null) { Message = error; return; }
            RememberGuideDismissal();
            ApplyRequested?.Invoke(); return;
        }
        if (StageIndex >= 3 && Validate() is { } invalid) { Message = invalid; return; }
        if (StageIndex is 1 or 2)
        {
            try
            {
                IsPreparing = true;
                var options = GdsLayerRoleMapping.Apply(new GdsHierarchyImportOptions(), BuildInterpretation().Layers);
                var cells = ActiveCells.Where(x => !x.PortsEdited).ToArray();
                await _sceneLock.WaitAsync(_lifetime.Token);
                Dictionary<string, (IReadOnlyList<DetectedPin> Pins, bool Limited)> pins;
                try
                {
                    pins = await Task.Run(() =>
                {
                    return cells.ToDictionary(x => x.CellName, x =>
                    {
                        _lifetime.Token.ThrowIfCancellationRequested();
                        var result = GdsReviewPinDetector.Detect(_library.Cells[x.CellName], x.Bounds,
                            options.PinDetection, _lifetime.Token);
                        return (Pins: result.Pins, Limited: result.Limited);
                    });
                }, _lifetime.Token);
                }
                finally { _sceneLock.Release(); }
                _lifetime.Token.ThrowIfCancellationRequested();
                _restoring = true;
                try
                {
                    foreach (var cell in cells)
                    {
                        ReplacePorts(cell, pins[cell.CellName].Pins);
                        cell.DetectionLimited = pins[cell.CellName].Limited;
                    }
                }
                finally { _restoring = false; }
                _lastSnapshot = CaptureUndoState();
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidDataException)
            { Message = ex.Message; return; }
            catch (OperationCanceledException) { return; }
            finally { IsPreparing = false; }
        }
        _confirmed[StageIndex] = true; Message = ""; StageIndex++; RefreshPresentation();
    }

    [RelayCommand] private void Back() { if (StageIndex > 0) StageIndex--; }
    [RelayCommand] private void ChooseStep(GdsReviewStep step) { if (step != null) StageIndex = step.Index; }
    [RelayCommand] private void PauseGuide() { GuideEnabled = false; RememberGuideDismissal(); }
    [RelayCommand] private void RestartGuide()
    {
        _hasPickedLayer = false;
        OnPropertyChanged(nameof(GuideTargetName));
        OnPropertyChanged(nameof(GuideTargetItem));
        GuideEnabled = true;
    }
    private void RememberGuideDismissal()
    {
        try { _profiles.DismissGuide(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Optional preference; import stays usable. */ }
    }
    [RelayCommand] private void GeometryOnly() { if (SelectedCell != null) SelectedCell.SelectedModel = null; }
    [RelayCommand]
    private void AddPort()
    {
        if (SelectedCell is not { } cell) return;
        RecordEdit();
        var n = 1;
        while (cell.Ports.Any(x => x.Name == $"o{n}")) n++;
        var row = new GdsPortReviewRow(new DetectedPin { Name = $"o{n}", XUm = cell.Bounds.Width, YUm = cell.Bounds.Height / 2, WidthUm = .5 });
        cell.Ports.Add(row); row.PropertyChanged += OnRowChanged; cell.PortsEdited = true; SelectedPort = row;
        IsPortEditing = true;
        FinishEdit();
    }
    [RelayCommand]
    private void RemovePort()
    {
        if (SelectedCell is not { } cell || SelectedPort is not { } port) return;
        RecordEdit(); port.PropertyChanged -= OnRowChanged; cell.Ports.Remove(port); cell.PortsEdited = true;
        SelectedPort = cell.Ports.FirstOrDefault(); FinishEdit();
    }
    [RelayCommand]
    private void GroupCells()
    {
        var selected = ActiveCells.Where(x => x.IncludeInGroup).ToArray();
        if (selected.Length < 2) { Message = Text("SelectTwo"); return; }
        if (string.IsNullOrWhiteSpace(GroupName)) { Message = Text("NameRequired"); return; }
        if (selected.Any(x => x.ExpandChildren)) { Message = Text("GroupExpanded"); return; }
        RecordEdit();
        var names = selected.Select(x => x.CellName).ToHashSet();
        _groups.RemoveAll(x => x.CellNames.Any(names.Contains));
        _groups.Add(new GdsInterpretationGroup { Name = GroupName.Trim(), CellNames = names.ToList() });
        FinishEdit(); Message = Text("GroupCreated");
    }
    [RelayCommand] private void ClearGroups() { RecordEdit(); _groups.Clear(); FinishEdit(); }
    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        if (!_undo.TryPop(out var snapshot)) return;
        RestoreSnapshot(snapshot.Interpretation, snapshot.EditedCells);
    }
    private void RestoreSnapshot(GdsImportInterpretation snapshot, HashSet<string>? editedCells = null)
    {
        _restoring = true;
        try
        {
            foreach (var row in Layers) row.Role = snapshot.Layers.FirstOrDefault(x => x.Layer == row.Layer && x.Datatype == row.Datatype)?.Role ?? GdsLayerRole.Unknown;
            foreach (var cell in Cells)
            {
                var saved = snapshot.Cells.FirstOrDefault(x => x.CellName == cell.CellName);
                if (saved == null) continue;
                cell.DisplayName = saved.DisplayName ?? cell.CellName; cell.ExpandChildren = saved.ExpandChildren;
                cell.SelectedModel = Models.FirstOrDefault(x => x.PdkName == saved.ModelPdkName && x.ComponentName == saved.ModelComponentName);
                ReplacePorts(cell, saved.Pins ?? []); cell.PortsEdited = editedCells?.Contains(cell.CellName) ?? true;
                foreach (var port in cell.Ports) port.ModelPortName = saved.ModelPinMap.GetValueOrDefault(port.Name) ?? "";
            }
            _groups.Clear(); _groups.AddRange(snapshot.Groups);
            Array.Fill(_confirmed, false); _lastSnapshot = CaptureUndoState();
        }
        finally { _restoring = false; }
        RefreshCellSelection(); RefreshPresentation(); UndoCommand.NotifyCanExecuteChanged();
    }
    private bool CanUndo() => _undo.Count > 0;
    private UndoState CaptureUndoState() => new(BuildInterpretation(), Cells.Where(x => x.PortsEdited).Select(x => x.CellName).ToHashSet());
    private void PushUndo(UndoState state)
    {
        if (_undo.Count >= 100)
        {
            var retained = _undo.Take(99).Reverse().ToArray();
            _undo.Clear();
            foreach (var item in retained) _undo.Push(item);
        }
        _undo.Push(state);
    }
    private void RecordEdit() => PushUndo(CaptureUndoState());
    private void FinishEdit()
    {
        Array.Fill(_confirmed, false, 2, 4); _lastSnapshot = CaptureUndoState();
        RefreshPresentation(); UndoCommand.NotifyCanExecuteChanged();
    }
    private void ReplacePorts(GdsCellReviewRow cell, IReadOnlyList<DetectedPin> pins)
    {
        foreach (var row in cell.Ports) row.PropertyChanged -= OnRowChanged;
        cell.Ports.Clear();
        foreach (var pin in pins) { var row = new GdsPortReviewRow(pin); cell.Ports.Add(row); row.PropertyChanged += OnRowChanged; }
        if (cell == SelectedCell) SelectedPort = cell.Ports.FirstOrDefault();
    }
    private bool _draggingPort;
    private bool _dragRecorded;
    public void BeginPortDrag() { _draggingPort = true; _dragRecorded = false; }
    public void EndPortDrag() { _draggingPort = false; _dragRecorded = false; }
    public void MovePort(int index, double x, double y)
    {
        if (SelectedCell is not { } cell || index < 0 || index >= cell.Ports.Count) return;
        var port = cell.Ports[index];
        if (port.XUm == x && port.YUm == y) return;
        if (!_draggingPort || !_dragRecorded) RecordEdit();
        _dragRecorded = true;
        _restoring = true;
        try { port.XUm = x; port.YUm = y; SelectedPort = port; cell.PortsEdited = true; }
        finally { _restoring = false; }
        FinishEdit();
    }
    public void PickLayer(CAP_DataAccess.Import.Gds.LayerPicker.GdsLayerPair pair)
    {
        SelectedLayer = Layers.FirstOrDefault(x => x.Pair == pair);
        MarkLayerInteraction();
    }
    public void PickPort(int index)
    {
        if (SelectedCell is { } cell && index >= 0 && index < cell.Ports.Count) SelectedPort = cell.Ports[index];
    }

    private string? Validate()
    {
        foreach (var cell in ActiveCells)
        {
            if (string.IsNullOrWhiteSpace(cell.DisplayName)) return Text("NameRequired");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in cell.Ports)
            {
                if (string.IsNullOrWhiteSpace(p.Name) || !names.Add(p.Name.Trim())) return Text("UniquePorts");
                if (!double.IsFinite(p.XUm) || !double.IsFinite(p.YUm) || !double.IsFinite(p.AngleDegrees) ||
                   !double.IsFinite(p.WidthUm) || p.XUm < 0 || p.YUm < 0 || p.XUm > cell.Bounds.Width ||
                   p.YUm > cell.Bounds.Height || p.WidthUm < 0) return Text("InvalidPort");
            }
            if (cell.SelectedModel is { } model)
            {
                var mapped = cell.Ports.Select(p => (string.IsNullOrWhiteSpace(p.ModelPortName) ? p.Name : p.ModelPortName).Trim()).ToList();
                if (mapped.Count != model.PinNames.Count || mapped.Distinct().Count() != mapped.Count ||
                   mapped.Except(model.PinNames).Any()) return Text("ModelPortsMismatch");
                for (var i = 0; i < cell.Ports.Count; i++)
                {
                    var domain = cell.Ports[i].Kind == "electrical" ? "electrical" : "optical";
                    if (!string.Equals(domain, model.PinDomains.GetValueOrDefault(mapped[i], "optical"), StringComparison.OrdinalIgnoreCase))
                        return Text("ModelDomainMismatch");
                }
            }
        }
        return null;
    }
}
