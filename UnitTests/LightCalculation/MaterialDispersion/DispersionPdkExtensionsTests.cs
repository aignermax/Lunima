using CAP_Core.LightCalculation.MaterialDispersion;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using Shouldly;
using Xunit;

namespace UnitTests.LightCalculation.MaterialDispersion;

/// <summary>
/// Tests for <see cref="DispersionPdkExtensions"/>.
/// Verifies that PDK JSON DTOs are correctly mapped to domain dispersion models,
/// and that null (absent) dispersion blocks produce a null model (backwards compat).
/// </summary>
public class DispersionPdkExtensionsTests
{
    [Fact]
    public void ToDispersionModel_NullDraft_ReturnsNull()
    {
        MaterialDispersionDraft? draft = null;
        draft.ToDispersionModel().ShouldBeNull();
    }

    [Fact]
    public void ToDispersionModel_PolynomialType_ReturnsPolynomialDispersion()
    {
        var draft = new MaterialDispersionDraft
        {
            Type = "polynomial",
            CenterWavelengthNm = 1550,
            EffectiveIndex = new EffectiveIndexDraft { N0 = 2.45, N1 = -1e-3 },
            PropagationLossDbPerCm = new LossDraft { Type = "constant", ConstantDbPerCm = 0.5 }
        };

        var model = draft.ToDispersionModel();

        model.ShouldNotBeNull();
        model.ShouldBeOfType<PolynomialDispersion>();
        model!.NEffAt(1550).ShouldBe(2.45, tolerance: 1e-10);
        model.LossDbPerCmAt(1550).ShouldBe(0.5, tolerance: 1e-10);
    }

    [Fact]
    public void ToDispersionModel_TabulatedLoss_ProducesCorrectLoss()
    {
        var draft = new MaterialDispersionDraft
        {
            Type = "polynomial",
            CenterWavelengthNm = 1550,
            EffectiveIndex = new EffectiveIndexDraft { N0 = 2.45 },
            PropagationLossDbPerCm = new LossDraft
            {
                Type = "tabulated",
                Points = new List<List<double>>
                {
                    new List<double> { 1500, 0.7 },
                    new List<double> { 1550, 0.5 },
                    new List<double> { 1600, 0.4 },
                }
            }
        };

        var model = draft.ToDispersionModel(fallbackLossDbPerCm: 0.5);

        model.ShouldNotBeNull();
        // At 1550 (center), loss should be ~0.5 (derived from tabulated linear fit)
        model!.LossDbPerCmAt(1550).ShouldBe(0.5, tolerance: 0.05);
    }

    [Fact]
    public void ToDispersionModel_TabulatedType_ReturnsTabulatedDispersion()
    {
        var draft = new MaterialDispersionDraft
        {
            Type = "tabulated",
            CenterWavelengthNm = 1550,
            PropagationLossDbPerCm = new LossDraft
            {
                Type = "tabulated",
                Points = new List<List<double>>
                {
                    new List<double> { 1500, 0.7 },
                    new List<double> { 1600, 0.4 },
                }
            }
        };

        var model = draft.ToDispersionModel();

        model.ShouldNotBeNull();
        model.ShouldBeOfType<TabulatedDispersion>();
        model!.LossDbPerCmAt(1500).ShouldBe(0.7, tolerance: 1e-10);
        model.LossDbPerCmAt(1600).ShouldBe(0.4, tolerance: 1e-10);
    }

    [Fact]
    public void ToDispersionModel_FallbackLoss_UsedWhenNoPropagationLossDraft()
    {
        var draft = new MaterialDispersionDraft
        {
            Type = "polynomial",
            CenterWavelengthNm = 1550,
            EffectiveIndex = new EffectiveIndexDraft { N0 = 2.45 },
            PropagationLossDbPerCm = null   // missing → use fallback
        };

        var model = draft.ToDispersionModel(fallbackLossDbPerCm: 1.2);
        model.ShouldNotBeNull();
        model!.LossDbPerCmAt(1550).ShouldBe(1.2, tolerance: 1e-10);
    }

    [Fact]
    public void ToDispersionModel_PdkJsonRoundtrip_OldPdkWithoutBlock_ReturnsNull()
    {
        // Simulate loading an old PDK whose component has no materialDispersion block
        var component = new PdkComponentDraft
        {
            Name = "MMI",
            WidthMicrometers = 100,
            HeightMicrometers = 50,
            NazcaOriginOffsetX = 0,
            NazcaOriginOffsetY = 0,
            Pins = new(),
            MaterialDispersion = null   // absent → null
        };

        component.MaterialDispersion.ToDispersionModel().ShouldBeNull();
    }
}
