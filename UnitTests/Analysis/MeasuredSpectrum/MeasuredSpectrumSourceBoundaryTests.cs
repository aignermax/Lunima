using Avalonia.Headless.XUnit;
using CAP.Avalonia.ViewModels.Analysis.WavelengthSpectrum;
using CAP_Core.Analysis.MeasuredSpectrum;
using Shouldly;
using SpectrumData = CAP_Core.Analysis.MeasuredSpectrum.MeasuredSpectrum;

namespace UnitTests.Analysis.MeasuredSpectrum;

public sealed class MeasuredSpectrumSourceBoundaryTests
{
    [AvaloniaFact]
    public async Task Overlay_UsesConfiguredSourceWithoutProbingItsVirtualPath()
    {
        var source = new Source();
        await using var overlay = new MeasuredSpectrumOverlayViewModel(source);
        await overlay.LoadFromFileAsync("virtual-lab://measurement-17");
        source.Path.ShouldBe("virtual-lab://measurement-17");
        source.Token.CanBeCanceled.ShouldBeTrue();
        overlay.Spectrum.ShouldBeSameAs(source.Value);
        overlay.ErrorText.ShouldBeEmpty();
        overlay.HasOverlay.ShouldBeTrue();
    }

    private sealed class Source : IMeasuredSpectrumSource
    {
        internal SpectrumData Value { get; } = new([1500d, 1550d, 1600d], [0.1d, 0.8d, 0.2d], "lab source");
        internal string? Path { get; private set; }
        internal CancellationToken Token { get; private set; }
        public Task<SpectrumData> ReadAsync(string path, CancellationToken cancellationToken = default)
        {
            Path = path;
            Token = cancellationToken;
            return Task.FromResult(Value);
        }
    }
}
