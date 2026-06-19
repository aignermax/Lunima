using System.Numerics;
using CAP_Core.Components.Connections;
using CAP_Core.LightCalculation.MaterialDispersion;
using Shouldly;
using Xunit;

namespace UnitTests.LightCalculation.MaterialDispersion;

/// <summary>
/// Tests that <see cref="WaveguideConnection"/> uses <see cref="IDispersionModel.LossDbPerCmAt"/>
/// when a dispersion model is assigned, and falls back to <see cref="WaveguideConnection.PropagationLossDbPerCm"/>
/// when no model is set.
/// </summary>
public class WaveguideConnectionDispersionTests
{
    /// <summary>
    /// A minimal fake dispersion model with wavelength-dependent loss for testing.
    /// </summary>
    private sealed class FakeLossModel : IDispersionModel
    {
        private readonly double _lossAt1500;
        private readonly double _lossAt1600;

        public FakeLossModel(double lossAt1500, double lossAt1600)
        {
            _lossAt1500 = lossAt1500;
            _lossAt1600 = lossAt1600;
        }

        public double NEffAt(double wavelengthNm) => 2.45;
        public double GroupIndexAt(double wavelengthNm) => 4.2;

        public double LossDbPerCmAt(double wavelengthNm)
        {
            // Linear interpolation between 1500 and 1600 nm
            double t = (wavelengthNm - 1500.0) / 100.0;
            return _lossAt1500 + t * (_lossAt1600 - _lossAt1500);
        }
    }

    [Fact]
    public void DispersionModel_Null_UsesScalarLoss()
    {
        var conn = new WaveguideConnection { PropagationLossDbPerCm = 0.5 };
        conn.DispersionModel.ShouldBeNull();
    }

    [Fact]
    public void DispersionModel_Set_ExposedAsProperty()
    {
        var model = new ConstantDispersion(lossDbPerCm: 1.0);
        var conn = new WaveguideConnection { DispersionModel = model };
        conn.DispersionModel.ShouldBeSameAs(model);
    }

    [Fact]
    public void DispersionModel_LossAt_DiffersAcrossWavelengths()
    {
        var model = new FakeLossModel(lossAt1500: 0.7, lossAt1600: 0.4);

        double loss1500 = model.LossDbPerCmAt(1500.0);
        double loss1600 = model.LossDbPerCmAt(1600.0);

        // Acceptance criterion: loss differs between 1500 nm and 1600 nm
        loss1500.ShouldNotBe(loss1600);
        loss1500.ShouldBe(0.7, tolerance: 1e-10);
        loss1600.ShouldBe(0.4, tolerance: 1e-10);
    }

    [Fact]
    public void ConstantDispersion_AllWavelengths_SameLoss()
    {
        var model = new ConstantDispersion(lossDbPerCm: 0.5);
        model.LossDbPerCmAt(1500).ShouldBe(0.5, tolerance: 1e-12);
        model.LossDbPerCmAt(1550).ShouldBe(0.5, tolerance: 1e-12);
        model.LossDbPerCmAt(1600).ShouldBe(0.5, tolerance: 1e-12);
    }
}
