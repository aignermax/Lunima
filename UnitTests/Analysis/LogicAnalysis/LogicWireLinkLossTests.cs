using CAP_Core.Analysis.LogicAnalysis;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using Shouldly;
using Xunit;

namespace UnitTests.Analysis.LogicAnalysis;

/// <summary>
/// The degraded-link level report (issue #1445, rung 4×6 defect 2): a logic wire
/// whose hopped path crosses a chiplet edge-coupler link charges the link's
/// coupling factor in its level report — delivered level = gate 1-level × link
/// power coupling — and flags a level warning naming the link exactly when the
/// delivered 1 falls below the receiving gate's threshold. An aligned link
/// (coupling 1) produces no loss entry and no warning; evaluation itself stays
/// idealized in every case.
/// </summary>
public class LogicWireLinkLossTests
{
    private const double Tolerance = 1e-12;
    private static readonly LogicPinRef Driver = new("G1", "Y");
    private static readonly LogicPinRef Load = new("G2", "A");

    [Fact]
    public void ForLinkLoss_DeliveredLevelIsGateOneLevelTimesCouplingFactor()
    {
        // Pinned NOT 1-level 0.5 across a link coupling 0.6 of the power: 0.3
        // delivered, −10·log10(0.6) ≈ 2.22 dB charged to the link.
        var calculator = Calculator(("G1", PinnedGateTables.NotGate()), ("G2", PinnedGateTables.NotGate()));

        var report = calculator.ForLinkLoss(Driver, Load, 0.6);

        report.DriverPowerOne.ShouldBe(0.5);
        report.BranchPower.ShouldBe(0.3, Tolerance);
        report.SplitLossDb.ShouldBe(2.2185, 0.001);
        var branch = report.Branches.ShouldHaveSingleItem();
        branch.LoadName.ShouldBe("G2.A");
        branch.Threshold.ShouldBe(PinnedGateTables.NotThreshold);
        branch.ReadsAsOne.ShouldBeFalse("0.3 < 0.375 — the link drops the 1 below the threshold");
    }

    [Fact]
    public void ForLinkLoss_AlignedLink_DeliversTheFullOneLevel()
    {
        var calculator = Calculator(("G1", PinnedGateTables.NotGate()), ("G2", PinnedGateTables.NotGate()));

        var report = calculator.ForLinkLoss(Driver, Load, 1.0);

        report.BranchPower.ShouldBe(report.DriverPowerOne);
        report.SplitLossDb.ShouldBe(0.0);
        report.Branches[0].ReadsAsOne.ShouldBeTrue();
    }

    [Fact]
    public void ForLinkLoss_CouplingOutsideUnitInterval_IsRejected()
    {
        var calculator = Calculator(("G1", PinnedGateTables.NotGate()), ("G2", PinnedGateTables.NotGate()));

        Should.Throw<ArgumentOutOfRangeException>(() => calculator.ForLinkLoss(Driver, Load, -0.1));
        Should.Throw<ArgumentOutOfRangeException>(() => calculator.ForLinkLoss(Driver, Load, 1.1));
    }

    [Fact]
    public void Evaluator_DegradedLinkBelowThreshold_WarnsAndNamesTheLink()
    {
        var network = PointToPointNetwork(new LogicWireLinkLoss(0.6, "'Chiplet A' / 'Chiplet B'"));

        var warning = network.FanOutWarnings.ShouldHaveSingleItem();
        warning.LinkDisplayName.ShouldBe("'Chiplet A' / 'Chiplet B'");
        warning.IsNetworkInputSignal.ShouldBeFalse();
        warning.DriverDisplayName.ShouldBe("G1.Y");
        warning.LoadCount.ShouldBe(1);
        warning.LoadNames.ShouldBe(new[] { "G2.A" });
        warning.Levels.DriverPowerOne.ShouldBe(0.5);
        warning.Levels.BranchPower.ShouldBe(0.3, Tolerance);
        warning.Levels.Branches[0].ReadsAsOne.ShouldBeFalse();
    }

    [Fact]
    public void Evaluator_LinkDegradedButStillAboveThreshold_StaysSilent()
    {
        // 0.5 × 0.9 = 0.45 ≥ 0.375 — lossy, but the 1 still reads: no warning.
        var network = PointToPointNetwork(new LogicWireLinkLoss(0.9, "'Chiplet A' / 'Chiplet B'"));

        network.FanOutWarnings.ShouldBeEmpty();
    }

    [Fact]
    public void Evaluator_WireWithoutLossEntry_ProducesNoWarning()
    {
        var network = PointToPointNetwork(linkLoss: null);

        network.FanOutWarnings.ShouldBeEmpty();
    }

    [Fact]
    public void ForPath_PathWithoutCrossChipletLink_ProducesNoLoss()
    {
        // A plain waveguide segment between two ungrouped, unstamped components is
        // no cross-chiplet edge-coupler link: the coupling model charges nothing.
        var path = new[] { PlainConnection() };

        WireLinkLossCalculator.ForPath(path, PinnedGateTables.WavelengthNm).ShouldBeNull();
    }

    /// <summary>A two-gate point-to-point network, optionally carrying one wire's link loss.</summary>
    private static LogicNetworkEvaluator PointToPointNetwork(LogicWireLinkLoss? linkLoss)
    {
        var gates = new Dictionary<string, LogicGateModel>
        {
            ["G1"] = PinnedGateTables.NotGate(),
            ["G2"] = PinnedGateTables.NotGate(),
        };
        var wiring = new Dictionary<LogicPinRef, LogicNetDriver>
        {
            [new LogicPinRef("G1", "A")] = new LogicNetDriver.NetworkInput("G1.A"),
            [Load] = new LogicNetDriver.GateOutput(Driver),
        };
        var taps = new Dictionary<string, LogicPinRef>
        {
            ["G1.Y"] = Driver,
            ["G2.Y"] = new("G2", "Y"),
        };
        var losses = linkLoss == null
            ? null
            : new Dictionary<LogicWireEdge, LogicWireLinkLoss> { [new LogicWireEdge(Driver, Load)] = linkLoss };
        return new LogicNetworkEvaluator(
            new[] { "G1.A" }, gates, wiring, taps, wireLinkLosses: losses);
    }

    /// <summary>A calculator over the given (gateId, model) pairs.</summary>
    private static FanOutLevelCalculator Calculator(params (string Id, LogicGateModel Model)[] gates) =>
        new(gates.ToDictionary(pair => pair.Id, pair => pair.Model));

    /// <summary>A connection between two plain, parentless pins — never a link.</summary>
    private static WaveguideConnection PlainConnection() =>
        new()
        {
            StartPin = new PhysicalPin { Name = "out" },
            EndPin = new PhysicalPin { Name = "in" },
        };
}
