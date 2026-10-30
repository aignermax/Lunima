namespace CAP_Core.Analysis.MeasuredSpectrum;

/// <summary>Loads a lab measurement; completion includes accepted read and parse work.</summary>
public interface IMeasuredSpectrumSource
{
    Task<MeasuredSpectrum> ReadAsync(string path, CancellationToken cancellationToken = default);
}
