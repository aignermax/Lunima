using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Analysis.LogicAnalysis;
using CAP.Avalonia.ViewModels.Analysis.LogicAnalysis.BusView;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Shouldly;
using UnitTests.Helpers;
using AvaloniaFactAttribute = Avalonia.Headless.XUnit.AvaloniaFactAttribute;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Issue #1402 — end-to-end journey of the shipped <c>Logic Gate RAM 2x4.lun</c>
/// through the real UI stack, not the Core fixtures: the DI-wired
/// <see cref="MainViewModel"/> (production container, <c>App.ConfigureServices</c>)
/// opens the example through the exact command the Home Examples card binds to
/// (<c>Home.OpenExampleCommand</c> → <c>FileOperations.OpenDesignAsCopyAsync</c>),
/// the panel singleton the Logic tab shows (<c>RightPanel.Logic</c>) builds the
/// network, the write/read demo runs over the input toggles plus the Step-clock
/// button and is read back from the panel's Q bus row — not the evaluator — and a
/// save/reopen round trip through <see cref="FileOperationsViewModel"/> proves the
/// hierarchical gate ids (<c>CELL0/REG00</c>, new with #1393) survive persistence:
/// the register readout must key on the network's gate ids, not the top-level
/// <c>GroupName</c> of a cell instance. Asserts target named signals, buses and the
/// register readout, never internal row order.
/// </summary>
public class Ram2x4UiStackJourneyTests
{
    private const string ExampleName = "Logic Gate RAM 2x4";
    private const string AddressSignal = "A";
    private const string LoadSignal = "LOAD";
    private const string BusPrefix = "Q";
    private const int BitCount = 4;
    private const int RegisterCount = 8;

    private static readonly string[] DataSignals = { "D0", "D1", "D2", "D3" };
    private static readonly string[] ExpectedToggles =
        { AddressSignal, LoadSignal, "D0", "D1", "D2", "D3" };

    [AvaloniaFact]
    public async Task Ram2x4_OpenBuildWriteReadSaveReopen_ThroughRealUiStack()
    {
        var preferencesPath = NewTempFilePath("prefs", "json");
        var savePath = NewTempFilePath("ram2x4-roundtrip", "lun");
        using var provider = ProductionContainerTestHelper.BuildWithTempPreferences(preferencesPath);
        try
        {
            var mainVm = provider.GetRequiredService<MainViewModel>();
            var canvas = provider.GetRequiredService<CAP.Avalonia.ViewModels.Canvas.DesignCanvasViewModel>();

            // 1. Open through the exact command the Home Examples card binds to.
            var example = mainVm.Home.Examples.Single(e => e.Name == ExampleName);
            await mainVm.Home.OpenExampleCommand.ExecuteAsync(example);
            await mainVm.FileOperations.PostLoadRouting;
            canvas.Components.ShouldNotBeEmpty(
                $"'{ExampleName}' must load onto the canvas the Logic panel observes");

            // 2. Build in the Logic panel singleton the tab shows.
            var logic = mainVm.RightPanel.Logic;
            await logic.BuildNetworkCommand.ExecuteAsync(null);
            AssertBuiltPanel(logic);

            // 3. Write 5 into word 0 and 10 into word 1 via toggles + Step clock,
            //    then read both back through the panel's Q bus row.
            StoreWord(logic, address: 0, data: 0);
            StoreWord(logic, address: 1, data: 0);
            ReadWord(logic, address: 0).ShouldBe(0, "words power up cleared");
            ReadWord(logic, address: 1).ShouldBe(0, "words power up cleared");

            StoreWord(logic, address: 0, data: 5);
            var busAfterStore = QBus(logic);
            ReadWord(logic, address: 0).ShouldBe(5, "A=0 reads back the stored 5");
            // The bus row text is the panel's own readback, not the evaluator's.
            busAfterStore.HeaderText.ShouldContain("Q = 5");
            ReadWord(logic, address: 1).ShouldBe(0, "word 1 untouched by the word-0 store");
            RegisterRow(logic, "CELL0/REG00").BitsText.ShouldBe("Y = 1", "5 = 0101: bit 0 committed");
            RegisterRow(logic, "CELL0/REG01").BitsText.ShouldBe("Y = 0");
            RegisterRow(logic, "CELL0/REG02").BitsText.ShouldBe("Y = 1", "5 = 0101: bit 2 committed");
            RegisterRow(logic, "CELL0/REG03").BitsText.ShouldBe("Y = 0");

            StoreWord(logic, address: 1, data: 10);
            ReadWord(logic, address: 1).ShouldBe(10, "A=1 reads back the stored 10");
            ReadWord(logic, address: 0).ShouldBe(5, "isolation: word 0 still answers 5");

            // 4. Save to a temp .lun, reopen, Build: same panel, registers cleared.
            var dialog = new Mock<IFileDialogService>();
            dialog.Setup(d => d.ShowSaveFileDialogAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(savePath);
            mainVm.FileOperations.FileDialogService = dialog.Object;
            await mainVm.FileOperations.SaveDesignAsCommand.ExecuteAsync(null);
            File.Exists(savePath).ShouldBeTrue("Save As must write the temp .lun");

            (await mainVm.FileOperations.LoadDesignFromPathAsync(savePath)).ShouldBeTrue(
                "the saved design must reopen through the real load path");
            await mainVm.FileOperations.PostLoadRouting;

            await logic.BuildNetworkCommand.ExecuteAsync(null);
            AssertBuiltPanel(logic);
            logic.RegisterStates.ShouldAllBe(
                row => row.BitsText == "Y = 0",
                "registers power up cleared after a reopen (documented semantics)");
            ReadWord(logic, address: 0).ShouldBe(0, "word 0 reads cleared after reopen");
            ReadWord(logic, address: 1).ShouldBe(0, "word 1 reads cleared after reopen");
        }
        finally
        {
            TryDelete(preferencesPath);
            TryDelete(savePath);
        }
    }

    /// <summary>Asserts the built panel's surface: toggles, the Q bus, the 8-row register readout.</summary>
    private static void AssertBuiltPanel(LogicPanelViewModel logic)
    {
        logic.HasNetwork.ShouldBeTrue(logic.StatusText);
        logic.Inputs.Select(i => i.PinName).ShouldBe(ExpectedToggles, ignoreOrder: true,
            customMessage: "toggles A, LOAD, D0–D3 — one per named network input signal");
        QBus(logic); // throws when the Q bus row is missing
        logic.RegisterStates.Count.ShouldBe(RegisterCount,
            "one readout row per nested register bit of the two cell instances");
        logic.RegisterStates.Select(r => r.GateName).ShouldBe(
            new[]
            {
                "CELL0/REG00", "CELL0/REG01", "CELL0/REG02", "CELL0/REG03",
                "CELL1/REG10", "CELL1/REG11", "CELL1/REG12", "CELL1/REG13",
            },
            ignoreOrder: true,
            customMessage: "the readout keys on the hierarchical network gate ids (#1393)");
    }

    /// <summary>Sets the A/LOAD/D toggles and presses Step — one committed store.</summary>
    private static void StoreWord(LogicPanelViewModel logic, int address, int data)
    {
        SetInputs(logic, address, load: true, data);
        logic.StepClockCommand.Execute(null);
    }

    /// <summary>Reads the word at <paramref name="address"/> from the panel's Q bus row.</summary>
    private static int ReadWord(LogicPanelViewModel logic, int address)
    {
        SetInputs(logic, address, load: false, data: 0);
        return (int)QBus(logic).DecimalValue;
    }

    /// <summary>Mirrors the user's toggle clicks for one A/LOAD/D triple.</summary>
    private static void SetInputs(LogicPanelViewModel logic, int address, bool load, int data)
    {
        logic.Inputs.Single(i => i.PinName == AddressSignal).IsOn = address != 0;
        logic.Inputs.Single(i => i.PinName == LoadSignal).IsOn = load;
        for (var bit = 0; bit < BitCount; bit++)
            logic.Inputs.Single(i => i.PinName == DataSignals[bit]).IsOn = (data & (1 << bit)) != 0;
    }

    /// <summary>The Q bus header row of the panel's bus view — fails the test when absent.</summary>
    private static LogicSignalBusOutputViewModel QBus(LogicPanelViewModel logic) =>
        logic.OutputRows.OfType<LogicSignalBusOutputViewModel>().SingleOrDefault(row => row.Prefix == BusPrefix)
        ?? throw new ShouldAssertException(
            $"no '{BusPrefix}' bus row in the Logic panel's output bus view " +
            $"(rows: {string.Join(", ", logic.OutputRows.Select(DescribeRow))})");

    private static string DescribeRow(LogicOutputRowViewModel row) =>
        row is LogicSignalBusOutputViewModel bus ? $"bus {bus.Prefix}" : row.ToString() ?? "?";

    private static LogicRegisterStateViewModel RegisterRow(LogicPanelViewModel logic, string gateId) =>
        logic.RegisterStates.Single(row => row.GateName == gateId);

    private static string NewTempFilePath(string stem, string extension) =>
        Path.Combine(Path.GetTempPath(), $"lunima-ram2x4-journey-{stem}-{Guid.NewGuid():N}.{extension}");

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { /* best effort */ }
    }
}
