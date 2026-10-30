using System.Text.Json;
using CAP.Avalonia.Services.GdsImport.DesignScope;
using CAP_Core.Components.Core;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using CAP_DataAccess.Import.Gds.Interpretation;
using Shouldly;
using UnitTests.Services.GdsImport;

namespace UnitTests.Persistence;

public sealed class ImportedSaveIsolationTests
{
    [Theory]
    [InlineData("draft")]
    [InlineData("model")]
    [InlineData("outline")]
    [InlineData("interpretation")]
    [InlineData("dispersion")]
    [InlineData("parameters")]
    [InlineData("wavelengths")]
    [InlineData("sliders")]
    [InlineData("compact_model")]
    [InlineData("interpretation_collections")]
    public void Capture_DetachesImportValuesBeforeBackgroundSerialization(string change)
    {
        using var host = new GdsDesignScopeTestHost();
        var points = new[] { new OutlinePoint(0, 0), new OutlinePoint(10, 0), new OutlinePoint(10, 10) };
        var draft = new PdkComponentDraft
        {
            Name = "part", Category = "Custom", WidthMicrometers = 10, HeightMicrometers = 10,
            Pins = [new() { Name = "o1" }],
            OutlinePolygons = [new() { Layer = 1, Points = points }],
            SMatrix = new()
            {
                Connections = [new() { FromPin = "o1", ToPin = "o1", Magnitude = 0.25 }],
                WavelengthData = [new() { WavelengthNm = 1500,
                    Connections = [new() { FromPin = "o1", ToPin = "o1", Magnitude = 0.5 }] }],
                Parameters = [new() { Name = "phase", DefaultValue = 0.2 }]
            },
            MaterialDispersion = new()
            {
                EffectiveIndex = new(), GroupIndex = new(),
                PropagationLossDbPerCm = new() { Type = "tabulated", Points = [[1500, 0.2], [1550, 0.3]] }
            },
            Sliders = [new() { SliderNumber = 0, MinVal = 0, MaxVal = 1 }],
            CompactModelParameters = new() { ["phase"] = 0.2 }
        };
        var interpretation = new GdsImportInterpretation
        {
            Cells = [new() { CellName = "part", ModelPinMap = new() { ["o1"] = "a0" },
                Pins = [new() { Name = "o1" }] }],
            Groups = [new() { Name = "assembly", CellNames = ["part"], InstanceNames = ["part:0"] }],
            Layers = [new(1, 0, CAP_DataAccess.Import.Gds.GdsLayerRole.OpticalCore)]
        };
        host.Scope.AddAndRegister(new DesignScopedGdsSet
        {
            PdkName = "import", GdsFileName = "chip.gds", GdsBytes = [1, 2, 3], Drafts = [draft], Interpretation = interpretation
        });
        var captured = host.Scope.CaptureForSave(["import"])!.Single();
        var expectedDraft = JsonSerializer.Serialize(draft);
        var expectedInterpretation = JsonSerializer.Serialize(interpretation);
        JsonSerializer.Serialize(captured.Components.Single()).ShouldBe(expectedDraft);
        JsonSerializer.Serialize(captured.Interpretation).ShouldBe(expectedInterpretation);
        switch (change)
        {
            case "draft": draft.Pins[0].Name = "changed"; break;
            case "model": draft.SMatrix.Connections[0].Magnitude = 0.9; break;
            case "outline": points[0] = new OutlinePoint(300, 400); break;
            case "interpretation": interpretation.Cells[0].ModelPinMap["o1"] = "changed"; break;
            case "dispersion":
                draft.MaterialDispersion.EffectiveIndex!.N0 = 99;
                draft.MaterialDispersion.GroupIndex!.Ng0 = 98;
                draft.MaterialDispersion.PropagationLossDbPerCm!.Points![0][1] = 97;
                break;
            case "parameters": draft.SMatrix.Parameters![0].DefaultValue = 99; break;
            case "wavelengths": draft.SMatrix.WavelengthData![0].Connections[0].Magnitude = 99; break;
            case "sliders": draft.Sliders[0].MaxVal = 99; break;
            case "compact_model": draft.CompactModelParameters["phase"] = 99; break;
            case "interpretation_collections":
                interpretation.Cells[0].Pins!.Clear();
                interpretation.Groups[0].CellNames.Clear();
                interpretation.Groups[0].InstanceNames.Clear();
                interpretation.Layers.Clear();
                break;
        }
        JsonSerializer.Serialize(captured.Components.Single()).ShouldBe(expectedDraft);
        JsonSerializer.Serialize(captured.Interpretation).ShouldBe(expectedInterpretation);
        captured.Components.Single().Pins.Single().Name.ShouldBe("o1");
        captured.Components.Single().SMatrix!.Connections.Single().Magnitude.ShouldBe(0.25);
        captured.Components.Single().OutlinePolygons!.Single().Points[0].ShouldBe(new OutlinePoint(0, 0));
        captured.Interpretation!.Cells[0].ModelPinMap["o1"].ShouldBe("a0");
    }
}
