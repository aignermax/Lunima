using CAP_Core.Components.Core;

namespace CAP_Core.Analysis.LogicAnalysis;

/// <summary>Captures a grouped optical circuit and extracts its coherent, thresholded truth table on a worker.</summary>
public class TruthTableExtractor
{
    /// <summary>Maximum input width: four inputs require sixteen optical simulations.</summary>
    public const int MaxLogicInputs = 4;

    /// <summary>Extracts the group at one wavelength without constantly-on bias pins.</summary>
    public Task<TruthTable> ExtractAsync(ComponentGroup group, IReadOnlyList<string> inputPinNames,
        IReadOnlyList<string> outputPinNames, double powerThreshold, int wavelengthNm,
        CancellationToken cancellationToken = default) =>
        ExtractAsync(group, inputPinNames, outputPinNames, [], powerThreshold, wavelengthNm, cancellationToken);

    /// <summary>
    /// Captures all pin roles and optical definitions before dispatch. Each on input and bias injects
    /// unit coherent amplitude; outputs are classified against a threshold strictly between zero and one.
    /// The gate is isolated from canvas wiring, and cancellation rejects incomplete tables.
    /// </summary>
    public async Task<TruthTable> ExtractAsync(ComponentGroup group, IReadOnlyList<string> inputPinNames,
        IReadOnlyList<string> outputPinNames, IReadOnlyList<string> biasPins, double powerThreshold,
        int wavelengthNm, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = TruthTableSnapshot.Capture(group, inputPinNames, outputPinNames, biasPins,
            powerThreshold, wavelengthNm);
        return await SolveSnapshotAsync(snapshot, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Worker boundary for solving a captured gate input.</summary>
    protected virtual Task<TruthTable> SolveSnapshotAsync(TruthTableSnapshot snapshot, CancellationToken cancellationToken) =>
        Task.Run(() => snapshot.SolveAsync(cancellationToken), cancellationToken);

    internal Task<TruthTable> ExtractCapturedAsync(TruthTableSnapshot snapshot, CancellationToken cancellationToken) =>
        SolveSnapshotAsync(snapshot, cancellationToken);
}
