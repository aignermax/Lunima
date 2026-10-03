using System.Collections.ObjectModel;
using System.Diagnostics;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Export;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core;
using CAP_Core.Analysis.LogicAnalysis;
using CAP_Core.Export;
using Moq;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace UnitTests.Integration.RamScale;

/// <summary>
/// Rung-5 spike (issue #1337): measures what a photonic 4-word × 4-bit RAM costs before
/// anyone builds it — gate count, connection count, <see cref="LogicNetworkAssembler"/>
/// time, a behavioural write/read-back check through the real assembled network, and the
/// full-route wall-clock of the freshly generated design on the current router (the load
/// routes every wire: the generated file carries no cached geometry, so the post-load pass
/// IS a full route of the design). The route measurement is bounded by a 60-second timeout
/// (<c>CAP_RAM_SPIKE_ROUTE_TIMEOUT_S</c> overrides it) — a timeout is itself a result and
/// does not fail the test; the structural and behavioural assertions always hold. The 2×4
/// variant gives the scaling slope. Numbers land in
/// <c>docs/logic/RAM-4x4-FEASIBILITY.md</c>.
/// </summary>
[Trait("Category", "Slow")]
public class Ram4x4FeasibilityTests
{
    /// <summary>
    /// Default bound of the full-route measurement: 60 s, so the unfiltered CI suite stays
    /// within its job cap. Set the override to 900 to re-take the 15-minute measurement.
    /// </summary>
    private const double DefaultRouteTimeoutSeconds = 60;
    private const string RouteTimeoutVariable = "CAP_RAM_SPIKE_ROUTE_TIMEOUT_S";
    private const int WavelengthNm = 1550;

    private readonly ITestOutputHelper _output;

    /// <summary>Attaches the test output sink.</summary>
    public Ram4x4FeasibilityTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public Task Ram4Words4Bits_AssemblyBehaviorAndRoute_Measured() =>
        MeasureAsync(words: 4, bits: 4, expectedGates: 183, expectedWires: 260);

    [Fact]
    public Task Ram2Words4Bits_AssemblyBehaviorAndRoute_Measured() =>
        MeasureAsync(words: 2, bits: 4, expectedGates: 71, expectedWires: 97);

    private async Task MeasureAsync(int words, int bits, int expectedGates, int expectedWires)
    {
        string label = $"RAM {words}x{bits}";
        var buildWatch = Stopwatch.StartNew();
        var design = RamScaleDesignBuilder.Build(words, bits);
        buildWatch.Stop();
        design.GateCount.ShouldBe(expectedGates, "the generated gate census is pinned");
        design.WireCount.ShouldBe(expectedWires, "the generated wire census is pinned");

        var tempPath = design.WriteToTempFile();
        try
        {
            var canvas = new DesignCanvasViewModel();
            ApplyChipSize(canvas, design.ChipWidthMicrometers, design.ChipHeightMicrometers);
            var fileOps = CreateFileOperations(canvas);
            fileOps.ApplyChipSizeAfterLoad = (width, height) => ApplyChipSize(canvas, width, height);

            var loadWatch = Stopwatch.StartNew();
            (await fileOps.LoadDesignFromPathAsync(tempPath)).ShouldBeTrue($"'{label}' must load onto the canvas");
            loadWatch.Stop();
            canvas.Components.Count.ShouldBe(design.GateCount, "every generated gate group must load");
            canvas.Connections.Count.ShouldBe(design.WireCount, "every generated wire must load");

            var route = await MeasureFullRoute(canvas, fileOps, label);
            var network = await MeasureAssembly(canvas, label);
            AssertNetworkShape(network, design, words, bits);
            AssertStoreReadHold(network, design, words, bits);

            Report($"[ram-spike] {label}: gates={design.GateCount} wires={design.WireCount} "
                + $"chip={design.ChipWidthMicrometers:F0}x{design.ChipHeightMicrometers:F0}um "
                + $"build={buildWatch.Elapsed.TotalSeconds:F1}s load={loadWatch.Elapsed.TotalSeconds:F1}s {route}");
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    /// <summary>
    /// Times the loader's post-load pass — a full route of every wire, since the generated
    /// file ships no cached geometry — bounded by the route timeout. A timeout cancels the
    /// pass and is reported as the measurement, not a failure.
    /// </summary>
    private async Task<string> MeasureFullRoute(
        DesignCanvasViewModel canvas, FileOperationsViewModel fileOps, string label)
    {
        var timeout = RouteTimeout;
        var watch = Stopwatch.StartNew();
        bool timedOut = false;
        try
        {
            await fileOps.PostLoadRouting.WaitAsync(timeout);
        }
        catch (TimeoutException)
        {
            timedOut = true;
            canvas.Routing.CancelRouting();
            try
            {
                await fileOps.PostLoadRouting.WaitAsync(TimeSpan.FromMinutes(2));
            }
            catch (TimeoutException)
            {
                Report($"[ram-spike] {label}: route pass did not observe cancellation within 2 min");
            }
        }
        watch.Stop();

        int unrouted = canvas.Connections.Count(c => c.Connection.RoutedPath == null);
        int blocked = canvas.Connections.Count(c => c.Connection.IsBlockedFallback);
        if (!timedOut)
        {
            unrouted.ShouldBe(0, $"'{label}': every wire must carry a route once the pass completed");
        }
        bool wasCancelled = canvas.ConnectionManager.LastRoutingPassTimings?.WasCancelled ?? false;
        var result = $"route={watch.Elapsed.TotalSeconds:F1}s timeout={timedOut} cancelled={wasCancelled} "
            + $"blocked={blocked} unrouted={unrouted} bound={timeout.TotalSeconds:F0}s";
        if (canvas.ConnectionManager.LastRoutingPassTimings is { } timings)
            result += " | " + ExampleRouteBakeTests.FormatPassTimingsLine(label, timings);
        return result;
    }

    /// <summary>Assembles the logic network through the real assembler and times it.</summary>
    private async Task<LogicNetworkEvaluator> MeasureAssembly(DesignCanvasViewModel canvas, string label)
    {
        var watch = Stopwatch.StartNew();
        var network = await LogicGateMuxExampleTests.AssembleNetwork(canvas);
        watch.Stop();
        Report($"[ram-spike] {label}: assemble={watch.Elapsed.TotalSeconds:F1}s "
            + $"inputs={network.InputPinNames.Count} outputs={network.OutputPinNames.Count} "
            + $"registers={network.RegisterState.Count}");
        return network;
    }

    /// <summary>The assembled network must expose the merged input signals, the read taps and one register per stored bit.</summary>
    private static void AssertNetworkShape(LogicNetworkEvaluator network, RamScaleDesign design, int words, int bits)
    {
        network.InputPinNames.ShouldBe(
            design.AddressSignals.Concat(new[] { RamScaleDesign.LoadSignal }).Concat(design.DataSignals).ToArray(),
            ignoreOrder: true,
            customMessage: "the signal names merge the unconnected pins into the address, LOAD and data inputs (#1025)");
        foreach (var tap in design.ReadTaps)
            network.OutputPinNames.ShouldContain(tap);
        network.RegisterState.Count.ShouldBe(words * bits, "one state element per stored bit");
    }

    /// <summary>
    /// Behavioural check through the real assembled network: clear every word, store a
    /// distinct pattern per word (isolation: only the addressed word commits), sweep all 16
    /// values through word 0, then hold across further clock steps with LOAD low.
    /// </summary>
    private static void AssertStoreReadHold(LogicNetworkEvaluator network, RamScaleDesign design, int words, int bits)
    {
        for (int address = 0; address < words; address++)
        {
            network.Evaluate(InputBits(design, address, load: true, data: 0));
            network.Step();
        }
        for (int address = 0; address < words; address++)
            ReadWord(network, design, address).ShouldBe(0, $"word {address} powers up cleared");

        var patterns = Enumerable.Range(0, words).Select(w => (w * 5 + 3) & 0xF).ToArray();
        for (int address = 0; address < words; address++)
        {
            network.Evaluate(InputBits(design, address, load: true, data: patterns[address]));
            network.Step();
            for (int read = 0; read < words; read++)
            {
                int expected = read <= address ? patterns[read] : 0;
                ReadWord(network, design, read).ShouldBe(expected,
                    $"after storing word {address}: word {read} answers {expected} — the addressed word alone commits");
            }
        }

        for (int value = 0; value < (1 << bits); value++)
        {
            network.Evaluate(InputBits(design, address: 0, load: true, data: value));
            network.Step();
            ReadWord(network, design, address: 0).ShouldBe(value, $"word 0 stores and reads back {value}");
        }

        var beforeHold = Enumerable.Range(0, words).Select(a => ReadWord(network, design, a)).ToArray();
        network.Evaluate(InputBits(design, address: 0, load: false, data: 0));
        network.Step();
        network.Step();
        for (int address = 0; address < words; address++)
            ReadWord(network, design, address).ShouldBe(beforeHold[address], $"LOAD=0: word {address} holds across steps");
    }

    /// <summary>Reads the word at the address: LOAD low, one evaluate, the read bus R as a decimal.</summary>
    private static int ReadWord(LogicNetworkEvaluator network, RamScaleDesign design, int address)
    {
        var read = network.Evaluate(InputBits(design, address, load: false, data: 0));
        int value = 0;
        for (int i = 0; i < design.ReadTaps.Count; i++)
            if (read[design.ReadTaps[i]])
                value |= 1 << i;
        return value;
    }

    /// <summary>The network input bits for one address/LOAD/data triple — one bit per signal (#1025).</summary>
    private static Dictionary<string, bool> InputBits(RamScaleDesign design, int address, bool load, int data)
    {
        var bits = new Dictionary<string, bool>();
        for (int b = 0; b < design.AddressSignals.Count; b++)
            bits[design.AddressSignals[b]] = (address & (1 << b)) != 0;
        bits[RamScaleDesign.LoadSignal] = load;
        for (int i = 0; i < design.DataSignals.Count; i++)
            bits[design.DataSignals[i]] = (data & (1 << i)) != 0;
        return bits;
    }

    private static TimeSpan RouteTimeout =>
        double.TryParse(Environment.GetEnvironmentVariable(RouteTimeoutVariable), out double seconds) && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : TimeSpan.FromSeconds(DefaultRouteTimeoutSeconds);

    /// <summary>Mirrors <c>ChipSizeViewModel.ApplyToCanvas</c>: chip bounds plus a chip-sized routing grid.</summary>
    private static void ApplyChipSize(DesignCanvasViewModel canvas, double widthUm, double heightUm)
    {
        if (widthUm <= 0 || heightUm <= 0)
            return;
        canvas.ChipMinX = 0;
        canvas.ChipMinY = 0;
        canvas.ChipMaxX = widthUm;
        canvas.ChipMaxY = heightUm;
        canvas.InitializeAStarRouting(0, 0, widthUm, heightUm);
    }

    private static FileOperationsViewModel CreateFileOperations(DesignCanvasViewModel canvas)
    {
        var fileOps = new FileOperationsViewModel(
            canvas,
            new CommandManager(),
            new SimpleNazcaExporter(),
            new SaxExporter(),
            new ObservableCollection<ComponentTemplate>(TestPdkLoader.LoadAllTemplates()),
            new GdsExportViewModel(new GdsExportService()),
            new PhotonTorchExportViewModel(new PhotonTorchExporter(), canvas),
            null!,
            errorConsole: new ErrorConsoleService());
        fileOps.FileDialogService = new Mock<IFileDialogService>().Object;
        return fileOps;
    }

    private void Report(string line)
    {
        _output.WriteLine(line);
        Console.WriteLine(line);
    }
}
