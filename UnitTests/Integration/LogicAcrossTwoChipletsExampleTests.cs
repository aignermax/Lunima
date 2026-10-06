using System.Collections.ObjectModel;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Analysis.LogicAnalysis;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Diagnostics;
using CAP.Avalonia.ViewModels.Export;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core.Analysis.LogicAnalysis;
using CAP_Core.Components.Core;
using CAP_Core.Export;
using Moq;
using Shouldly;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Pinned acceptance tests for the shipped <c>examples/Logic Gate Across Two Chiplets.lun</c>
/// (issue #1455, rung 4×6): the #1430 journey composition baked into a user-openable
/// example — chiplet A's NOT drives chiplet B's AND across the aligned edge-coupler
/// link, so the network truth table reads <c>Y = NOT A AND B</c>. Covers loading
/// through the real load path, the Logic panel Build with the named toggles
/// (<c>A</c>, <c>B</c>) and the named output tap (<c>Y</c>), the truth table itself,
/// the save → load round trip, and Design Checks on the shipped file.
/// </summary>
public class LogicAcrossTwoChipletsExampleTests : IClassFixture<LogicAcrossTwoChipletsExampleTests.ChipletLogicFixture>
{
    private const string ExampleFileName = LogicAcrossTwoChipletsExampleAuthoringTests.ExampleFileName;
    private const string NotInput = LogicAcrossTwoChipletsExampleAuthoringTests.NotInputSignalName;
    private const string AndInput = "B";
    private const string AndOutput = LogicAcrossTwoChipletsExampleAuthoringTests.AndOutputSignalName;
    private const string NotOutputTap = LogicAcrossTwoChipletsExampleAuthoringTests.NotOutputSignalName;

    private readonly ChipletLogicFixture _fixture;

    /// <summary>Attaches the shared fixture.</summary>
    public LogicAcrossTwoChipletsExampleTests(ChipletLogicFixture fixture) => _fixture = fixture;

    [Fact]
    public void Example_LoadsTwoChiplets_WithPersistedSignalNames()
    {
        _fixture.Canvas.Components.Count.ShouldBe(2,
            "the example holds two top-level chiplets");
        var chipletNames = _fixture.Canvas.Components
            .Select(c => ((ComponentGroup)c.Component).GroupName)
            .ToArray();
        chipletNames.ShouldBe(
            new[] { LogicAcrossChipletLinkJourneyDesign.ChipletAName, LogicAcrossChipletLinkJourneyDesign.ChipletBName },
            ignoreOrder: true);

        var notGate = FindNestedGate(LogicAcrossChipletLinkJourneyDesign.ChipletAName, "NOT");
        notGate.TruthTablePinAssignment.ShouldNotBeNull(
            "the NOT gate must ship its persisted pin roles");
        notGate.TruthTablePinAssignment!.InputSignalNames.ShouldBe(
            new Dictionary<string, string> { ["A"] = NotInput },
            "the NOT's input pin ships its network-signal identity");
        notGate.TruthTablePinAssignment!.OutputSignalNames.ShouldBe(
            new Dictionary<string, string> { ["Y"] = NotOutputTap },
            "the NOT's output tap ships its signal name");

        var andGate = FindNestedGate(LogicAcrossChipletLinkJourneyDesign.ChipletBName, "AND");
        andGate.TruthTablePinAssignment.ShouldNotBeNull(
            "the AND gate must ship its persisted pin roles");
        andGate.TruthTablePinAssignment!.OutputSignalNames.ShouldBe(
            new Dictionary<string, string> { ["Y"] = AndOutput },
            "the AND's output tap ships its signal name");
    }

    [Fact]
    public async Task LogicPanel_Build_ExposesNamedTogglesAndNamedOutput()
    {
        var panel = new LogicPanelViewModel();
        panel.Configure(_fixture.Canvas);
        await panel.BuildNetworkCommand.ExecuteAsync(null);

        panel.HasNetwork.ShouldBeTrue($"the example must assemble: {panel.StatusText}");
        panel.Inputs.Select(i => i.PinName).ShouldBe(new[] { NotInput, AndInput }, ignoreOrder: true,
            "the panel shows the two named toggles, not raw pins");
        panel.Outputs.Select(o => o.PinName).ShouldContain(AndOutput,
            "the panel shows the named final output");
        panel.Outputs.Select(o => o.PinName).ShouldContain(NotOutputTap,
            "the panel shows the named cross-chiplet intermediate");
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    public void TruthTable_EqualsNotAAndB(bool a, bool b, bool expected)
    {
        var result = _fixture.Network.Evaluate(Bits((NotInput, a), (AndInput, b)));

        result[AndOutput].ShouldBe(expected, $"Y = NOT A AND B for A={a}, B={b}");
        result[NotOutputTap].ShouldBe(!a, $"the intermediate NOT_A tap reads NOT A for A={a}");
    }

    [Fact]
    public async Task SaveLoadRoundTrip_PreservesTruthTable()
    {
        var savedPath = await _fixture.SaveToTempFile();
        try
        {
            var reloadedCanvas = await LoadCanvas(savedPath);
            await reloadedCanvas.RecalculateRoutesAsync();

            var reloaded = await AssembleAsync(reloadedCanvas);
            reloaded.InputPinNames.ShouldBe(_fixture.Network.InputPinNames, ignoreOrder: true);
            reloaded.OutputPinNames.ShouldBe(_fixture.Network.OutputPinNames, ignoreOrder: true);
            foreach (var a in new[] { false, true })
            foreach (var b in new[] { false, true })
            {
                var bits = Bits((NotInput, a), (AndInput, b));
                reloaded.Evaluate(bits).ShouldBe(_fixture.Network.Evaluate(bits),
                    $"truth table identical after save/load for A={a}, B={b}");
            }
        }
        finally
        {
            if (File.Exists(savedPath)) File.Delete(savedPath);
        }
    }

    [Fact]
    public void DesignChecks_RunClean_OnTheShippedFile()
    {
        var validation = new DesignValidationViewModel();
        var groups = _fixture.Canvas.Components.Select(vm => vm.Component).OfType<ComponentGroup>().ToArray();
        validation.RunValidation(
            _fixture.Canvas.ConnectionManager.Connections,
            groups: groups,
            allComponents: _fixture.Canvas.Components.Select(vm => vm.Component),
            externalPortPins: groups.SelectMany(g => g.PhysicalPins),
            wavelengthNm: LogicAcrossChipletLinkJourneyDesign.WavelengthNm);

        validation.Issues.ShouldBeEmpty(
            "the shipped example must pass Design Checks clean (the link is aligned): " +
            string.Join("; ", validation.Issues.Select(i => i.ToString())));
    }

    /// <summary>Loads a design file through the real load path onto a fresh canvas.</summary>
    internal static async Task<DesignCanvasViewModel> LoadCanvas(string path)
    {
        var canvas = new DesignCanvasViewModel();
        var fileOps = CreateFileOperations(canvas);
        (await fileOps.LoadDesignFromPathAsync(path)).ShouldBeTrue(
            $"'{Path.GetFileName(path)}' must load through the real load path");
        return canvas;
    }

    /// <summary>Runs the production assembler over the canvas's components and connections.</summary>
    private static Task<LogicNetworkEvaluator> AssembleAsync(DesignCanvasViewModel canvas) =>
        new LogicNetworkAssembler().AssembleAsync(
            canvas.Components.Select(vm => vm.Component).ToList(),
            canvas.ConnectionManager.Connections,
            LogicAcrossChipletLinkJourneyDesign.WavelengthNm);

    private ComponentGroup FindNestedGate(string chipletName, string gateName) =>
        _fixture.Canvas.Components
            .Select(c => c.Component).OfType<ComponentGroup>()
            .Single(g => g.GroupName == chipletName)
            .ChildComponents.OfType<ComponentGroup>().Single(g => g.GroupName == gateName);

    private static FileOperationsViewModel CreateFileOperations(DesignCanvasViewModel canvas) =>
        new(
            canvas,
            new CommandManager(),
            new SimpleNazcaExporter(),
            new SaxExporter(),
            new ObservableCollection<ComponentTemplate>(TestPdkLoader.LoadAllTemplates()),
            new GdsExportViewModel(new GdsExportService()),
            new PhotonTorchExportViewModel(new PhotonTorchExporter(), canvas),
            null!);

    /// <summary>Builds an input-bit dictionary from (name, bit) pairs.</summary>
    private static Dictionary<string, bool> Bits(params (string Name, bool Bit)[] bits) =>
        bits.ToDictionary(pair => pair.Name, pair => pair.Bit);

    /// <summary>Loads the shipped example once and assembles its network for the whole test class.</summary>
    public sealed class ChipletLogicFixture : IAsyncLifetime
    {
        /// <summary>The loaded canvas.</summary>
        public DesignCanvasViewModel Canvas { get; private set; } = null!;

        /// <summary>The logic network derived from the loaded canvas wiring.</summary>
        public LogicNetworkEvaluator Network { get; private set; } = null!;

        /// <summary>Loads the shipped example and assembles its logic network.</summary>
        public async Task InitializeAsync()
        {
            var path = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), ExampleFileName);
            Canvas = await LoadCanvas(path);
            Network = await AssembleAsync(Canvas);
        }

        /// <summary>No shared state to release.</summary>
        public Task DisposeAsync() => Task.CompletedTask;

        /// <summary>Saves the loaded design through the real save path and returns the file path.</summary>
        public async Task<string> SaveToTempFile()
        {
            var path = Path.Combine(Path.GetTempPath(), $"logic-across-two-chiplets-{Guid.NewGuid():N}.lun");
            var saveVm = CreateFileOperations(Canvas);
            var dialog = new Mock<IFileDialogService>();
            dialog.Setup(f => f.ShowSaveFileDialogAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(path);
            saveVm.FileDialogService = dialog.Object;
            await saveVm.SaveDesignAsCommand.ExecuteAsync(null);
            File.Exists(path).ShouldBeTrue("the real save path must write the temp .lun");
            return path;
        }
    }
}
