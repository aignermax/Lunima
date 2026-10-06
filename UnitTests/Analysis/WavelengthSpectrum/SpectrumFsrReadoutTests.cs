using System;
using System.Collections.Generic;
using System.Linq;
using CAP.Avalonia.ViewModels.Analysis.WavelengthSpectrum;
using CAP_Core.Analysis.WavelengthSpectrum;
using Shouldly;
using Xunit;

namespace UnitTests.Analysis.WavelengthSpectrum;

/// <summary>
/// Readout tests for #1382: the FSR of a synthetic periodic spectrum must come
/// back within 1 % — dip-dominated curves (through port) directly, peak-dominated
/// curves (drop port) via the inverted analysis. Featureless curves produce no
/// readout line at all.
/// </summary>
public class SpectrumFsrReadoutTests
{
    private const double SweepStartNm = 1500.0;
    private const double SweepEndNm = 1600.0;
    private const int PointCount = 501; // 0.2 nm grid
    private const double KnownFsrNm = 10.0;
    private const double FsrToleranceFraction = 0.01;

    /// <summary>T(λ) = 0.55 + 0.45·cos(2π(λ−1500)/FSR) — ten clean dips, median near the top.</summary>
    private static TransmissionCurve SyntheticDipCurve()
    {
        var (wl, power) = BuildGrid(lambda =>
            0.55 + 0.45 * Math.Cos(2.0 * Math.PI * (lambda - SweepStartNm) / KnownFsrNm));
        return new TransmissionCurve(Guid.NewGuid(), wl, power, isAtNoiseFloor: false);
    }

    /// <summary>
    /// Narrow Gaussian resonances (width 1 nm, spacing FSR) clamped to a hard
    /// floor — a ring drop port whose flattened inter-resonance floor makes the
    /// direct dip analysis ambiguous, so the inverted (peak) analysis must win.
    /// </summary>
    private static TransmissionCurve SyntheticPeakCurve()
    {
        const double floor = 0.02;
        const double amplitude = 0.93;
        const double widthNm = 1.0;
        var (wl, power) = BuildGrid(lambda =>
        {
            double nearestResonance = Math.Round((lambda - SweepStartNm) / KnownFsrNm) * KnownFsrNm + SweepStartNm;
            double detuning = lambda - nearestResonance;
            return Math.Max(floor, amplitude * Math.Exp(-detuning * detuning / (widthNm * widthNm)));
        });
        return new TransmissionCurve(Guid.NewGuid(), wl, power, isAtNoiseFloor: false);
    }

    private static (double[] Wavelengths, double[] Power) BuildGrid(Func<double, double> transmission)
    {
        var wl = new double[PointCount];
        var power = new double[PointCount];
        for (int i = 0; i < PointCount; i++)
        {
            wl[i] = SweepStartNm + (SweepEndNm - SweepStartNm) * i / (PointCount - 1);
            power[i] = transmission(wl[i]);
        }
        return (wl, power);
    }

    [Fact]
    public void Analyze_DipDominatedCurve_FsrWithinOnePercent_AsDips()
    {
        var result = CurveFsrAnalyzer.Analyze(SyntheticDipCurve());

        result.ShouldNotBeNull();
        result.ExtremumKind.ShouldBe(SpectrumExtremumKind.Dips);
        result.ExtremumCount.ShouldBeGreaterThanOrEqualTo(8);
        result.MeanFsrNm.ShouldBe(KnownFsrNm, KnownFsrNm * FsrToleranceFraction);
    }

    [Fact]
    public void Analyze_PeakDominatedCurve_FsrWithinOnePercent_AsPeaks()
    {
        var result = CurveFsrAnalyzer.Analyze(SyntheticPeakCurve());

        result.ShouldNotBeNull("the inverted analysis must report a drop-port curve's FSR too");
        result.ExtremumKind.ShouldBe(SpectrumExtremumKind.Peaks);
        result.ExtremumCount.ShouldBeGreaterThanOrEqualTo(8);
        result.MeanFsrNm.ShouldBe(KnownFsrNm, KnownFsrNm * FsrToleranceFraction);
    }

    [Fact]
    public void Analyze_FlatCurve_ReturnsNull()
    {
        var (wl, _) = BuildGrid(_ => 0.0);
        var flat = new TransmissionCurve(
            Guid.NewGuid(), wl, Enumerable.Repeat(0.5, PointCount).ToArray(), isAtNoiseFloor: false);

        CurveFsrAnalyzer.Analyze(flat).ShouldBeNull();
    }

    [Fact]
    public void Analyze_NoiseFloorCurve_ReturnsNull()
    {
        var curve = SyntheticDipCurve();
        var atFloor = new TransmissionCurve(
            curve.PinId, curve.WavelengthsNm, curve.Transmission, isAtNoiseFloor: true);

        CurveFsrAnalyzer.Analyze(atFloor).ShouldBeNull("a dark pin must not produce a bogus readout");
    }

    [Fact]
    public void Analyze_SingleDipCurve_ReturnsNull()
    {
        // One wide dip in the whole window: fewer than two fringes, no FSR.
        var (wl, power) = BuildGrid(lambda =>
            0.55 + 0.45 * Math.Cos(2.0 * Math.PI * (lambda - SweepStartNm) / 500.0));
        var curve = new TransmissionCurve(Guid.NewGuid(), wl, power, isAtNoiseFloor: false);

        CurveFsrAnalyzer.Analyze(curve).ShouldBeNull();
    }

    [Fact]
    public void ViewModel_UpdateFsrReadouts_SyntheticCurve_ReadoutMatchesKnownFsrWithinOnePercent()
    {
        var vm = new WavelengthSpectrumViewModel();
        var curve = SyntheticDipCurve();
        var pinNames = new Dictionary<Guid, string> { [curve.PinId] = "Through.port 2" };

        vm.UpdateFsrReadouts(new[] { curve }, pinNames, inputLabel: "In");

        vm.HasFsrReadouts.ShouldBeTrue();
        var line = vm.FsrReadouts.ShouldHaveSingleItem();
        line.CurveLabel.ShouldBe("In → Through.port 2");
        line.ExtremumKind.ShouldBe(SpectrumExtremumKind.Dips);
        line.MeanFsrNm.ShouldBe(KnownFsrNm, KnownFsrNm * FsrToleranceFraction);
        line.Text.ShouldContain("Through.port 2");
        line.Text.ShouldContain("10.0");
    }

    [Fact]
    public void ViewModel_UpdateFsrReadouts_FeaturelessCurves_YieldsNoLines()
    {
        var vm = new WavelengthSpectrumViewModel();
        var (wl, _) = BuildGrid(_ => 0.0);
        var flat = new TransmissionCurve(
            Guid.NewGuid(), wl, Enumerable.Repeat(0.5, PointCount).ToArray(), isAtNoiseFloor: false);

        vm.UpdateFsrReadouts(new[] { flat }, new Dictionary<Guid, string>(), inputLabel: null);

        vm.FsrReadouts.ShouldBeEmpty();
        vm.HasFsrReadouts.ShouldBeFalse();
    }

    [Fact]
    public void ViewModel_Configure_ClearsReadouts()
    {
        var vm = new WavelengthSpectrumViewModel();
        vm.UpdateFsrReadouts(new[] { SyntheticDipCurve() }, new Dictionary<Guid, string>(), inputLabel: null);
        vm.HasFsrReadouts.ShouldBeTrue();

        vm.Configure(new CAP.Avalonia.ViewModels.Canvas.DesignCanvasViewModel());

        vm.FsrReadouts.ShouldBeEmpty();
        vm.HasFsrReadouts.ShouldBeFalse();
    }
}
