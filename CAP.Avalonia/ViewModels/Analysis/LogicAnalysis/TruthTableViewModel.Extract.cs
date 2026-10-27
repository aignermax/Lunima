using System.Globalization;
using CAP_Core.Analysis.LogicAnalysis;
using CAP_Core.Components.Core;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Analysis.LogicAnalysis;

/// <summary>
/// Extraction half of <see cref="TruthTableViewModel"/>: runs the
/// <see cref="TruthTableExtractor"/> asynchronously with cancellation and maps the
/// result (or any validation failure) onto the panel's display state.
/// </summary>
public partial class TruthTableViewModel
{
    /// <summary>Extracts the truth table for the current pin assignment.</summary>
    [RelayCommand]
    private Task Extract() => _work.RunAsync(ExtractOwnedAsync);

    private async Task ExtractOwnedAsync(CancellationToken ownerToken)
    {
        if (IsProcessing || !IsGroupSelected || _group == null || !_work.IsConfiguredCurrent) return;
        var group = _group;
        var canvas = _canvas;
        var inputs = InputPins.Where(p => p.IsChecked).Select(p => p.PinName).ToArray();
        var outputs = OutputPins.Where(p => p.IsChecked).Select(p => p.PinName).ToArray();
        var biases = BiasPins.Where(p => p.IsChecked).Select(p => p.PinName).ToArray();
        if (inputs.Length == 0 || outputs.Length == 0)
        {
            StatusText = Translate("Analysis.TruthTable.SelectPins");
            return;
        }

        AnalysisSweepOperation? operation;
        try { operation = _work.Begin(ownerToken); }
        catch (OperationCanceledException) { return; }
        if (operation == null) return;
        var threshold = Threshold;
        var wavelength = ResolveWavelengthNm();
        var previous = group.TruthTablePinAssignment;
        var assignment = new TruthTablePinAssignment
        {
            InputPinNames = inputs.ToList(), OutputPinNames = outputs.ToList(), BiasPinNames = biases.ToList(),
            Threshold = threshold, IsRegister = IsRegister,
            InputSignalNames = PreservedSignalNames(previous?.InputSignalNames, inputs),
            OutputSignalNames = PreservedSignalNames(previous?.OutputSignalNames, outputs)
        };
        try
        {
            await _publication.PublishAsync(() =>
            {
                PublishStep(operation, () => IsProcessing = true);
                PublishStep(operation, () => StatusText = Translate("Analysis.TruthTable.Running"));
            });
            operation.ThrowIfObsolete();
            // ExtractAsync synchronously captures the detached optical snapshot before its first await.
            var table = await _extractor.ExtractAsync(group, inputs, outputs, biases, threshold, wavelength, operation.Token);
            await _publication.PublishAsync(() =>
            {
                operation.ThrowIfObsolete();
                ShowTable(table, operation);
                SuggestThreshold(table, operation);
                operation.CommitAndAdvance(() => group.TruthTablePinAssignment = assignment);
                PublishStep(operation, () => canvas?.RepaintRequested?.Invoke());
                PublishStep(operation, () => SignalNamesVisible = true);
                PublishStep(operation, () => StatusText = string.Format(
                    Translate("Analysis.TruthTable.Complete"), table.Rows.Count));
            });
        }
        catch (OperationCanceledException)
        {
            await PublishFailureAsync(operation, Translate("Analysis.TruthTable.Cancelled"));
        }
        catch (Exception error)
        {
            // Validation and solver failures remain readable; a replaced selection owns its own display.
            await PublishFailureAsync(operation, string.Format(Translate("Analysis.TruthTable.Failed"), error.Message));
        }
        finally
        {
            try { await _publication.PublishAsync(() => IsProcessing = false); }
            finally { await _work.CompleteAsync(operation); }
        }
    }

    private Task PublishFailureAsync(AnalysisSweepOperation operation, string message) =>
        _publication.PublishAsync(() =>
        {
            // User cancellation retains context for feedback; document/selection changes do not.
            if (!operation.IsContextCurrent) return;
            HasResult = false;
            if (!operation.IsContextCurrent) return;
            Rows.Clear();
            if (operation.IsContextCurrent) StatusText = message;
        });

    /// <summary>Cancels the running extraction while its owner continues to drain it.</summary>
    [RelayCommand]
    private void Cancel() => _work.Cancel();

    private static void PublishStep(AnalysisSweepOperation operation, Action publish)
    {
        if (!operation.TryPublish(publish)) throw new OperationCanceledException();
        operation.ThrowIfObsolete();
    }

    private void ShowTable(TruthTable table, AnalysisSweepOperation operation)
    {
        PublishStep(operation, Rows.Clear);
        PublishStep(operation, InputHeaders.Clear);
        PublishStep(operation, OutputHeaders.Clear);
        foreach (var name in table.InputPinNames)
            PublishStep(operation, () => InputHeaders.Add(name));
        foreach (var name in table.OutputPinNames)
            PublishStep(operation, () => OutputHeaders.Add(name));
        foreach (var row in table.Rows)
        {
            var inputBits = table.InputPinNames.Select(name => row.InputBits[name] ? "1" : "0");
            var cells = table.OutputPinNames.Select(name => ToCell(row.Outputs[name])).ToArray();
            PublishStep(operation, () => Rows.Add(new TruthTableRowViewModel(string.Join(" ", inputBits), cells)));
        }
        PublishStep(operation, () => BiasSummaryText = table.BiasPinNames.Count > 0
            ? string.Format(Translate("TruthTable.BiasSummary"), string.Join(", ", table.BiasPinNames)) : "");
        PublishStep(operation, () => HasResult = true);
    }

    private static TruthTableOutputCellViewModel ToCell(LogicOutputValue value) =>
        new(value.IsOne, value.Power.ToString("F2", CultureInfo.InvariantCulture));

    /// <summary>
    /// The signal names that survive a re-extraction: entries whose pin keeps the
    /// role the map belongs to keep their name; entries for pins that left the role
    /// are dropped (a signal only exists on pins of that role). Null when nothing
    /// survives — the .lun format stays free of empty blocks.
    /// </summary>
    private static Dictionary<string, string>? PreservedSignalNames(
        IReadOnlyDictionary<string, string>? previous, IReadOnlyCollection<string> rolePins)
    {
        if (previous == null)
            return null;
        var preserved = previous
            .Where(pair => rolePins.Contains(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        return preserved.Count > 0 ? preserved : null;
    }
}
