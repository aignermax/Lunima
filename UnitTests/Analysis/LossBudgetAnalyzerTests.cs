using CAP_Core.Analysis;
using CAP_Core.Components;
using Shouldly;
using Xunit;

namespace UnitTests.Analysis;

public class LossBudgetAnalyzerTests
{
    private readonly LossBudgetAnalyzer _analyzer = new();

    [Fact]
    public void Analyze_NoConnections_ReturnsEmptyResult()
    {
        var result = _analyzer.Analyze(
            Array.Empty<WaveguideConnection>(),
            new HashSet<Guid>(),
            new HashSet<Guid>());

        result.Paths.Count.ShouldBe(0);
        result.CriticalConnections.Count.ShouldBe(0);
        result.HighestLossPath.ShouldBeNull();
    }

    [Fact]
    public void Analyze_SinglePath_ReturnsOnePath()
    {
        // Arrange: A -> B (single connection)
        var pinA = CreatePin("inputA");
        var pinB = CreatePin("outputB");
        var conn = CreateConnection(pinA, pinB, 2.5);

        var inputIds = new HashSet<Guid> { pinA.PinId };
        var outputIds = new HashSet<Guid> { pinB.PinId };

        // Act
        var result = _analyzer.Analyze(
            new[] { conn }, inputIds, outputIds);

        // Assert
        result.Paths.Count.ShouldBe(1);
        result.Paths[0].TotalLossDb.ShouldBe(2.5);
        result.Paths[0].Severity.ShouldBe(LossSeverity.Low);
        result.MinLossDb.ShouldBe(2.5);
        result.MaxLossDb.ShouldBe(2.5);
        result.AverageLossDb.ShouldBe(2.5);
    }

    [Fact]
    public void Analyze_MultiHopPath_AccumulatesLoss()
    {
        // Arrange: A -> B -> C (two-hop path)
        var pinA = CreatePin("inputA");
        var pinB = CreatePin("midB");
        var pinC = CreatePin("outputC");

        var conn1 = CreateConnection(pinA, pinB, 3.0);
        var conn2 = CreateConnection(pinB, pinC, 4.0);

        var inputIds = new HashSet<Guid> { pinA.PinId };
        var outputIds = new HashSet<Guid> { pinC.PinId };

        // Act
        var result = _analyzer.Analyze(
            new[] { conn1, conn2 }, inputIds, outputIds);

        // Assert
        result.Paths.Count.ShouldBe(1);
        result.Paths[0].TotalLossDb.ShouldBe(7.0);
        result.Paths[0].Connections.Count.ShouldBe(2);
        result.Paths[0].Severity.ShouldBe(LossSeverity.Medium);
    }

    [Fact]
    public void Analyze_ParallelPaths_ReturnsBothPaths()
    {
        // Arrange: A -> B and A -> C (two parallel paths from same input)
        var pinA = CreatePin("input");
        var pinB = CreatePin("out1");
        var pinC = CreatePin("out2");

        var conn1 = CreateConnection(pinA, pinB, 1.0);
        var conn2 = CreateConnection(pinA, pinC, 8.0);

        var inputIds = new HashSet<Guid> { pinA.PinId };
        var outputIds = new HashSet<Guid> { pinB.PinId, pinC.PinId };

        // Act
        var result = _analyzer.Analyze(
            new[] { conn1, conn2 }, inputIds, outputIds);

        // Assert
        result.Paths.Count.ShouldBe(2);
        result.MinLossDb.ShouldBe(1.0);
        result.MaxLossDb.ShouldBe(8.0);
        result.AverageLossDb.ShouldBe(4.5);
    }

    [Fact]
    public void Analyze_HighLossPath_IdentifiesCriticalConnections()
    {
        // Arrange: A -> B with 12 dB (High severity)
        var pinA = CreatePin("inputA");
        var pinB = CreatePin("outputB");
        var conn = CreateConnection(pinA, pinB, 12.0);

        var inputIds = new HashSet<Guid> { pinA.PinId };
        var outputIds = new HashSet<Guid> { pinB.PinId };

        // Act
        var result = _analyzer.Analyze(
            new[] { conn }, inputIds, outputIds);

        // Assert
        result.CriticalConnections.Count.ShouldBe(1);
        result.CriticalConnections[0].ShouldBeSameAs(conn);
        result.HighestLossPath!.Severity.ShouldBe(LossSeverity.High);
    }

    [Fact]
    public void Analyze_LowLossPath_NoCriticalConnections()
    {
        // Arrange: A -> B with 1 dB (Low severity)
        var pinA = CreatePin("inputA");
        var pinB = CreatePin("outputB");
        var conn = CreateConnection(pinA, pinB, 1.0);

        var inputIds = new HashSet<Guid> { pinA.PinId };
        var outputIds = new HashSet<Guid> { pinB.PinId };

        // Act
        var result = _analyzer.Analyze(
            new[] { conn }, inputIds, outputIds);

        // Assert
        result.CriticalConnections.Count.ShouldBe(0);
    }

    [Fact]
    public void Analyze_NoPathBetweenInputAndOutput_ReturnsEmpty()
    {
        // Arrange: A -> B, but output is C (disconnected)
        var pinA = CreatePin("inputA");
        var pinB = CreatePin("midB");
        var pinC = CreatePin("outputC");

        var conn = CreateConnection(pinA, pinB, 2.0);

        var inputIds = new HashSet<Guid> { pinA.PinId };
        var outputIds = new HashSet<Guid> { pinC.PinId };

        // Act
        var result = _analyzer.Analyze(
            new[] { conn }, inputIds, outputIds);

        // Assert
        result.Paths.Count.ShouldBe(0);
    }

    [Fact]
    public void Analyze_CyclicGraph_DoesNotLoop()
    {
        // Arrange: A -> B -> A (cycle) with B also connected to output C
        var pinA = CreatePin("inputA");
        var pinB = CreatePin("midB");
        var pinC = CreatePin("outputC");

        var connAB = CreateConnection(pinA, pinB, 2.0);
        var connBA = CreateConnection(pinB, pinA, 2.0);
        var connBC = CreateConnection(pinB, pinC, 3.0);

        var inputIds = new HashSet<Guid> { pinA.PinId };
        var outputIds = new HashSet<Guid> { pinC.PinId };

        // Act
        var result = _analyzer.Analyze(
            new[] { connAB, connBA, connBC }, inputIds, outputIds);

        // Assert - should find A -> B -> C but not loop forever
        result.Paths.Count.ShouldBe(1);
        result.Paths[0].TotalLossDb.ShouldBe(5.0);
    }

    [Fact]
    public void Analyze_DiamondGraph_FindsBothPaths()
    {
        // Arrange: A -> B -> D and A -> C -> D (diamond shape)
        var pinA = CreatePin("input");
        var pinB = CreatePin("mid1");
        var pinC = CreatePin("mid2");
        var pinD = CreatePin("output");

        var connAB = CreateConnection(pinA, pinB, 1.0);
        var connAC = CreateConnection(pinA, pinC, 2.0);
        var connBD = CreateConnection(pinB, pinD, 3.0);
        var connCD = CreateConnection(pinC, pinD, 4.0);

        var inputIds = new HashSet<Guid> { pinA.PinId };
        var outputIds = new HashSet<Guid> { pinD.PinId };

        // Act
        var result = _analyzer.Analyze(
            new[] { connAB, connAC, connBD, connCD }, inputIds, outputIds);

        // Assert - two paths: A->B->D (4 dB) and A->C->D (6 dB)
        result.Paths.Count.ShouldBe(2);
        result.MinLossDb.ShouldBe(4.0);
        result.MaxLossDb.ShouldBe(6.0);
    }

    [Fact]
    public void Analyze_MultipleInputsAndOutputs_FindsAllPaths()
    {
        // Arrange: Two inputs, two outputs
        var pinIn1 = CreatePin("in1");
        var pinIn2 = CreatePin("in2");
        var pinOut1 = CreatePin("out1");
        var pinOut2 = CreatePin("out2");

        var conn1 = CreateConnection(pinIn1, pinOut1, 2.0);
        var conn2 = CreateConnection(pinIn2, pinOut2, 5.0);

        var inputIds = new HashSet<Guid> { pinIn1.PinId, pinIn2.PinId };
        var outputIds = new HashSet<Guid> { pinOut1.PinId, pinOut2.PinId };

        // Act
        var result = _analyzer.Analyze(
            new[] { conn1, conn2 }, inputIds, outputIds);

        // Assert
        result.Paths.Count.ShouldBe(2);
    }

    [Fact]
    public void Analyze_InputNotInGraph_ReturnsEmpty()
    {
        var orphanInputId = Guid.NewGuid();
        var pinA = CreatePin("a");
        var pinB = CreatePin("b");
        var conn = CreateConnection(pinA, pinB, 1.0);

        var inputIds = new HashSet<Guid> { orphanInputId };
        var outputIds = new HashSet<Guid> { pinB.PinId };

        var result = _analyzer.Analyze(
            new[] { conn }, inputIds, outputIds);

        result.Paths.Count.ShouldBe(0);
    }

    [Fact]
    public void Analyze_PathLabelsContainPinNames()
    {
        var pinA = CreatePin("laserIn");
        var pinB = CreatePin("detectorOut");
        var conn = CreateConnection(pinA, pinB, 1.0);

        var result = _analyzer.Analyze(
            new[] { conn },
            new HashSet<Guid> { pinA.PinId },
            new HashSet<Guid> { pinB.PinId });

        result.Paths[0].PathLabel.ShouldContain("laserIn");
        result.Paths[0].PathLabel.ShouldContain("detectorOut");
    }

    private static PhysicalPin CreatePin(string name)
    {
        var component = CreateMinimalComponent();
        return new PhysicalPin
        {
            Name = name,
            ParentComponent = component,
            OffsetXMicrometers = 0,
            OffsetYMicrometers = 0,
            AngleDegrees = 0
        };
    }

    private static WaveguideConnection CreateConnection(
        PhysicalPin start, PhysicalPin end, double lossDb)
    {
        var conn = new WaveguideConnection
        {
            StartPin = start,
            EndPin = end
        };
        PathLossEntryTests.SetTotalLossDb(conn, lossDb);
        return conn;
    }

    private static Component CreateMinimalComponent()
    {
        var parts = new Part[1, 1];
        parts[0, 0] = new Part(new List<Pin>());

        return new Component(
            laserWaveLengthToSMatrixMap: new Dictionary<int, CAP_Core.LightCalculation.SMatrix>(),
            sliders: new List<Slider>(),
            nazcaFunctionName: "test",
            nazcaFunctionParams: "",
            parts: parts,
            typeNumber: 0,
            identifier: "test",
            rotationCounterClock: DiscreteRotation.R0
        );
    }
}
