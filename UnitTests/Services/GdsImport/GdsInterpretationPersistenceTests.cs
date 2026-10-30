using CAP_DataAccess.Persistence.NativeDesign;
using CAP.Avalonia.Services.GdsImport.DesignScope;
using CAP_DataAccess.Import.Gds;
using CAP_DataAccess.Import.Gds.Interpretation;
using Shouldly;
using Xunit;

namespace UnitTests.Services.GdsImport;

public class GdsInterpretationPersistenceTests
{
    [Fact]
    public void ReviewedPortsAndGroupsSurviveDesignScopeRoundTrip()
    {
        using var host = new GdsDesignScopeTestHost();
        var interpretation = new GdsImportInterpretation
        {
            TopCellName = "chip",
            Cells = [new() { CellName = "device", Pins = [new() { Name = "input", XUm = 2.5 }] }],
            Groups = [new() { Name = "stage", CellNames = ["device"] }]
        };
        host.Scope.AddAndRegister(new DesignScopedGdsSet
        {
            PdkName = "review",
            GdsFileName = "chip.gds",
            GdsBytes = [1, 2],
            Drafts = [],
            Interpretation = interpretation
        });
        var saved = host.Scope.CaptureForSave(["review"]);
        var payload = System.Text.Json.JsonSerializer.Serialize(saved);
        host.Scope.RestoreDesignScope(System.Text.Json.JsonSerializer.Deserialize<List<ImportedGdsComponentSetData>>(payload));
        var restored = host.Scope.Sets.Single().Interpretation.ShouldNotBeNull();
        restored.Cells.Single().Pins!.Single().XUm.ShouldBe(2.5);
        restored.Groups.Single().CellNames.ShouldBe(["device"]);
        var copy = new CAP.Avalonia.Services.GdsImport.GdsImportService(new CAP_DataAccess.Import.Gds.GdsSourceReader(), host.Scope).FindInterpretation("", "chip")!;
        copy.Cells.Clear();
        host.Scope.Sets.Single().Interpretation!.Cells.Count.ShouldBe(1);
    }
}
