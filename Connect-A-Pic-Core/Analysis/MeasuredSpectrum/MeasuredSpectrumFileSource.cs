namespace CAP_Core.Analysis.MeasuredSpectrum;

/// <summary>Reads and interprets a lab CSV without coupling presentation to file access.</summary>
public sealed class MeasuredSpectrumFileSource : IMeasuredSpectrumSource
{
    public async Task<MeasuredSpectrum> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        var text = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        // Cancellation revokes publication; accepted CPU parsing still completes under this owner.
        var spectrum = await Task.Run(() => Parse(text, Path.GetFileName(path)), cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return spectrum;
    }

    private static MeasuredSpectrum Parse(string text, string label)
    {
        var probe = MeasuredSpectrumCsvReader.Parse(text, PowerUnit.Linear, label);
        var looksDecibel = probe.PowerLinear.Any(power => power < 0) || probe.PowerLinear.Max() > 1.5;
        return looksDecibel ? MeasuredSpectrumCsvReader.Parse(text, PowerUnit.Decibel, label) : probe;
    }
}
