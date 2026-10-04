using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Analysis.LogicAnalysis;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Components.Creation;
using Moq;
using Shouldly;
using Xunit;
using static UnitTests.Analysis.LogicAnalysis.LogicCellInstanceTestBed;

namespace UnitTests.Analysis.LogicAnalysis;

/// <summary>
/// The real user's hierarchical flow (issue #1388): build a gate cell once, save it as
/// a prefab to the Group Library, place it twice, wire the instances on the canvas. The
/// two instances must assemble as 2×N gates with distinct identities — instancing keeps
/// the user-named gate groups (no SubGroup_n rename), the assembler ids a nested gate by
/// its hierarchical path <c>&lt;instance&gt;/&lt;gate&gt;</c>, and a design save → load
/// round trip reproduces the same ids and the same evaluation. The cell here is a
/// two-gate buffer chain (BUF1 → BUF2, frozen intra-cell path): a single-input reading
/// of the 50/50 combiner fixture passes its input through, so the four-gate chain
/// reproduces the network input at the last gate's output.
/// </summary>
public class LogicCellInstanceAssemblyTests : IDisposable
{
    private const string TemplateName = "Cell";
    private const string FirstGate = "BUF1";
    private const string SecondGate = "BUF2";

    private readonly string _libraryPath =
        Path.Combine(Path.GetTempPath(), $"cell_instance_lib_{Guid.NewGuid():N}");
    private readonly string _designPath =
        Path.Combine(Path.GetTempPath(), $"cell_instance_{Guid.NewGuid():N}.lun");

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_libraryPath)) Directory.Delete(_libraryPath, true);
            if (File.Exists(_designPath)) File.Delete(_designPath);
        }
        catch
        {
            // Temp cleanup must never fail the test run.
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TwoInstancesOfGateCell_AssembleAsFourDistinctGates_AndChainEvaluates(bool bit)
    {
        var template = RegisterAndReload(BuildBufferCell(FirstGate, SecondGate, RawBufferGate));
        var first = Instantiate(template, 0, 0);
        var second = Instantiate(template, 2000, 0);

        first.ChildComponents.OfType<ComponentGroup>().Select(g => g.GroupName)
            .ShouldBe(new[] { FirstGate, SecondGate },
                "instancing keeps the user's gate names — no SubGroup_n rename");

        var chain = new WaveguideConnection
        {
            StartPin = Port(first, "out"),
            EndPin = Port(second, "in"),
        };
        var network = await Assemble(new Component[] { first, second }, new[] { chain });

        string id1 = first.GroupName;
        string id2 = second.GroupName;
        id1.ShouldNotBe(id2, "each placed instance gets its own group name");
        network.Gates.Keys.ShouldBe(
            new[] { $"{id1}/{FirstGate}", $"{id1}/{SecondGate}", $"{id2}/{FirstGate}", $"{id2}/{SecondGate}" },
            ignoreOrder: true,
            customMessage: "two prefab instances of a 2-gate cell assemble as 2×2 distinct gates");
        network.InputPinNames.ShouldBe(new[] { $"{id1}/{FirstGate}.a" },
            "the frozen intra-cell paths and the inter-instance wire drive every other input");
        network.OutputPinNames.ShouldBe(
            new[] { $"{id1}/{FirstGate}.y", $"{id1}/{SecondGate}.y", $"{id2}/{FirstGate}.y", $"{id2}/{SecondGate}.y" },
            ignoreOrder: true);

        var outputs = network.Evaluate(Bits(($"{id1}/{FirstGate}.a", bit)));
        outputs[$"{id2}/{SecondGate}.y"].ShouldBe(bit,
            $"the four-buffer chain passes the network input through for bit={bit}");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TwoPlacedInstances_SaveLoadReload_KeepGateIdsAndEvaluation(bool bit)
    {
        var template = RegisterAndReload(BuildBufferCell(FirstGate, SecondGate, TemplateBufferGate));
        var canvas = new DesignCanvasViewModel();
        var mainVm = CreateMainViewModel(canvas);
        var first = Instantiate(template, 100, 100);
        var second = Instantiate(template, 3000, 100);
        canvas.AddComponent(first);
        canvas.AddComponent(second);
        var chain = canvas.ConnectPinsWithCachedRoute(
            Port(first, "out"), Port(second, "in"),
            StraightPath(Port(first, "out"), Port(second, "in")));
        chain.ShouldNotBeNull("the inter-instance wire must be created");
        chain!.Connection.IsRouteFrozen = true;

        var before = await AssembleCanvas(canvas);
        var expectedIds = new[]
        {
            $"{first.GroupName}/{FirstGate}", $"{first.GroupName}/{SecondGate}",
            $"{second.GroupName}/{FirstGate}", $"{second.GroupName}/{SecondGate}",
        };
        before.Gates.Keys.ShouldBe(expectedIds, ignoreOrder: true);

        await SaveDesign(mainVm);
        var freshCanvas = await LoadDesign();

        var after = await AssembleCanvas(freshCanvas);
        after.Gates.Keys.ShouldBe(expectedIds, ignoreOrder: true,
            customMessage: "the reloaded design assembles the identical gate ids");
        string networkInput = $"{first.GroupName}/{FirstGate}.a";
        after.InputPinNames.ShouldBe(new[] { networkInput });
        after.Evaluate(Bits((networkInput, bit)))[$"{second.GroupName}/{SecondGate}.y"]
            .ShouldBe(bit, $"the reloaded chain evaluates identically for bit={bit}");
    }

    [Fact]
    public async Task SameNamedGatesInSameNamedWrappers_ThrowAReadableError()
    {
        var firstCell = WrapperWith("CELL", RawBufferGate("G"));
        var secondCell = WrapperWith("CELL", RawBufferGate("G"));

        var error = await Should.ThrowAsync<ArgumentException>(
            () => Assemble(new Component[] { firstCell, secondCell }, Array.Empty<WaveguideConnection>()));

        error.Message.ShouldContain("CELL/G");
        error.Message.ShouldContain("must be unique");
    }

    /// <summary>Saves the cell to the library, then reloads it from disk with a fresh manager.</summary>
    private GroupTemplate RegisterAndReload(ComponentGroup cell)
    {
        var saveManager = new GroupLibraryManager(_libraryPath);
        saveManager.SaveTemplate(cell, TemplateName);
        var loadManager = new GroupLibraryManager(_libraryPath);
        loadManager.LoadTemplates();
        var template = loadManager.Templates.Single();
        template.TemplateGroup.ShouldNotBeNull("the reloaded template must carry its group");
        template.TemplateGroup!.ChildComponents.OfType<ComponentGroup>()
            .ShouldAllBe(g => g.TruthTablePinAssignment != null,
                "the disk round trip keeps the nested gates' pin-role assignments");
        return template;
    }

    /// <summary>Instantiates the template through the real library path.</summary>
    private ComponentGroup Instantiate(GroupTemplate template, double x, double y) =>
        new GroupLibraryManager(_libraryPath).InstantiateTemplate(template, x, y);

    private async Task SaveDesign(MainViewModel mainVm)
    {
        var dialog = new Mock<IFileDialogService>();
        dialog.Setup(f => f.ShowSaveFileDialogAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(_designPath);
        mainVm.FileDialogService = dialog.Object;
        await mainVm.FileOperations.SaveDesignAsCommand.ExecuteAsync(null);
        File.Exists(_designPath).ShouldBeTrue("the design file must be written");
    }

    private async Task<DesignCanvasViewModel> LoadDesign()
    {
        var freshCanvas = new DesignCanvasViewModel();
        var freshVm = CreateMainViewModel(freshCanvas);
        var dialog = new Mock<IFileDialogService>();
        dialog.Setup(f => f.ShowOpenFileDialogAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(_designPath);
        freshVm.FileDialogService = dialog.Object;
        await freshVm.FileOperations.LoadDesignCommand.ExecuteAsync(null);
        await freshVm.FileOperations.PostLoadRouting.WaitAsync(TimeSpan.FromSeconds(30));
        return freshCanvas;
    }
}
