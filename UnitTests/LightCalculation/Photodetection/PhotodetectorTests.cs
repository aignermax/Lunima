using System.Numerics;
using CAP_Core.Analysis.EyeDiagram;
using CAP_Core.LightCalculation.Photodetection;
using CAP_Core.LightCalculation.TimeDomainSimulation.CompactModels;
using CAP_Core.LightCalculation.TimeDomainSimulation.CompactModels.Models;
using Shouldly;
using Xunit;

namespace UnitTests.LightCalculation.Photodetection;

public class PhotodetectorTests
{
    [Theory]
    [InlineData(0, 0.001)]
    [InlineData(-10, 0.0001)]
    public void DbmProducesExpectedSignalCurrent(double dbm, double expected)
    {
        var pin = Guid.NewGuid();
        var result = OutputPhotocurrent.FromDbm(pin, PhotodetectorResolver.Resolve(), dbm);
        result.OutputPinId.ShouldBe(pin);
        result.PhotocurrentAmps!.Value.ShouldBe(expected, 1e-15);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0.65)]
    public void ReceiverTimeDomainAndNoiseShareResolvedResponsivity(double? overrideValue)
    {
        var parameters = overrideValue.HasValue
            ? new Dictionary<string, double> { [PhotodiodeRcModel.ResponsivityKey] = overrideValue.Value }
            : null;
        var resolved = PhotodetectorResolver.Resolve(compactModelParameters: parameters);
        var expectedR = overrideValue ?? 1;
        var receiver = OutputPhotocurrent.FromDbm(Guid.NewGuid(), resolved, 0);
        var model = (PhotodiodeRcModel)CompactModelRegistry.Create(PhotodiodeRcModel.ModelName,
            parameters, PhotodetectorResolver.Generic);
        var noise = new NoiseModel { Detector = resolved, ResponsivityAPerW = 0.2 };
        model.ResponsivityAmpsPerWatt.ShouldBe(expectedR);
        noise.ResponsivityAPerW.ShouldBe(expectedR);
        var current = model.Step(100 * model.RcTimeConstantSeconds,
            new Complex(Math.Sqrt(0.001), 0), model.CreateInitialState(), 0).ElectricalOutput;
        current.ShouldBe(receiver.PhotocurrentAmps!.Value, 1e-15);
        noise.PhotocurrentAmps(0.001).ShouldBe(current, 1e-15);
        noise.ShotNoiseSigmaAmpere(0.001).ShouldBe(
            Math.Sqrt(2 * 1.602176634e-19 * current * noise.BandwidthHz), 1e-15);
        resolved.Provenance.ShouldBe(overrideValue.HasValue ? "custom" : "generic");
    }

    [Fact]
    public void LegacyRemainsUnchangedAndUnknownPowerDoesNotBecomeCurrent()
    {
        new PhotodiodeRcModel().ResponsivityAmpsPerWatt.ShouldBe(0.8);
        new NoiseModel().ResponsivityAPerW.ShouldBe(0.8);
        new NoiseModel().PhotocurrentAmps(-1).ShouldBe(0);
        var parameters = new Dictionary<string, double> { [PhotodiodeRcModel.ResponsivityKey] = 0.6 };
        new PhotodiodeRcModel(parameters).ResponsivityAmpsPerWatt.ShouldBe(0.6);
        var result = OutputPhotocurrent.FromWatts(Guid.NewGuid(), PhotodetectorResolver.Generic, null);
        result.PhotocurrentAmps.ShouldBeNull();
        OutputPhotocurrent.FromWatts(Guid.NewGuid(), result.Detector, 0).PhotocurrentAmps.ShouldBe(0);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(-1)]
    public void InvalidAbsolutePowerAndOverridesAreRejected(double value)
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            OutputPhotocurrent.FromWatts(Guid.NewGuid(), PhotodetectorResolver.Generic, value));
        Should.Throw<ArgumentOutOfRangeException>(() => PhotodetectorResolver.Resolve(
            compactModelParameters: new Dictionary<string, double> { [PhotodiodeRcModel.ResponsivityKey] = value }));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(double.MaxValue)]
    [InlineData(double.MinValue)]
    public void UnrepresentableDbmIsRejected(double value)
        => Should.Throw<ArgumentOutOfRangeException>(() =>
            OutputPhotocurrent.FromDbm(Guid.NewGuid(), PhotodetectorResolver.Generic, value));

    [Theory]
    [InlineData("responsivityAmpsPerWatt", 0)]
    [InlineData("darkCurrentNa", -1)]
    [InlineData("bitRateHz", 0)]
    [InlineData("ber", 0)]
    [InlineData("ber", 1)]
    [InlineData("wavelengthNm", 0)]
    public void InvalidDescriptionIsRejected(string field, double value)
        => Should.Throw<ArgumentOutOfRangeException>(() => DescriptionWith(field, value));

    [Theory]
    [InlineData("responsivityAmpsPerWatt")]
    [InlineData("darkCurrentNa")]
    [InlineData("sensitivityDbm")]
    [InlineData("bitRateHz")]
    [InlineData("ber")]
    [InlineData("wavelengthNm")]
    public void EveryNumericFieldRejectsNonFiniteValues(string field)
    {
        foreach (var value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            Should.Throw<ArgumentOutOfRangeException>(() => DescriptionWith(field, value));
    }

    private static PhotodetectorDescription DescriptionWith(string field, double value) => new(
        field == "responsivityAmpsPerWatt" ? value : 1, field == "darkCurrentNa" ? value : 6,
        field == "sensitivityDbm" ? value : -20, field == "bitRateHz" ? value : 10e9,
        field == "ber" ? value : 1e-12, field == "wavelengthNm" ? value : 1550, "custom", "Test assumptions");
}
