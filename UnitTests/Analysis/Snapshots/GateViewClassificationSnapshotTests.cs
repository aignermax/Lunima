using CAP_Core.Analysis.LogicAnalysis.GateView;
using CAP_Core.Grid.FormulaReading;
using Shouldly;

namespace UnitTests.Analysis.Snapshots;

public sealed class GateViewClassificationSnapshotTests
{
    [Fact]
    public async Task FormulaEdit_WithSameLegacySignatureDoesNotReuseOldSymbol()
    {
        var group = LogicSnapshotFixture.Gate("gate");
        var child = group.ChildComponents.Single();
        var matrix = child.WaveLengthToSMatrixMap.Values.Single();
        var output = group.ExternalPins.Single(pin => pin.Name == "y").InternalPin.LogicalPin!.IDOutFlow;
        void SetFormula(string expression)
        {
            matrix.SetValues(new(), reset: true);
            matrix.NonLinearConnections.Clear();
            foreach (var pin in group.ExternalPins.Where(pin => pin.Name is "a" or "b"))
                matrix.NonLinearConnections[(pin.InternalPin.LogicalPin!.IDInFlow, output)] =
                    MathExpressionReader.ConvertToDelegate(expression, new Dictionary<string, Guid>(), new Dictionary<string, Guid>());
        }
        SetFormula("ToComplex(0.3, 0)");
        var classifier = new GateFunctionClassifier();
        var first = GateViewSnapshotFixture.Graph(group);
        await classifier.ClassifyAsync(first, 1550);
        first.Nodes.Single().Kind.ShouldBe(GateSymbolKind.And);
        var legacyKey = GateStructureSignature.Of(group, 1550);
        SetFormula("ToComplex(0.7, 0)");
        GateStructureSignature.Of(group, 1550).ShouldBe(legacyKey);
        var changed = GateViewSnapshotFixture.Graph(group);
        await classifier.ClassifyAsync(changed, 1550);
        changed.Nodes.Single().Kind.ShouldBe(GateSymbolKind.Or);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FirstGateHeld_LaterGatePhysicsAndRolesWereAlreadyCaptured(bool changeRoles)
    {
        var first = LogicSnapshotFixture.Gate("first");
        var later = LogicSnapshotFixture.Gate("later");
        later.TruthTablePinAssignment!.Threshold = 0.75;
        var graph = GateViewSnapshotFixture.Graph(first, later);
        var extractor = new GateViewSnapshotFixture.CountingExtractor(holdFirst: true);
        var running = new GateFunctionClassifier(extractor).ClassifyAsync(graph, 1550);
        try
        {
            await extractor.Barrier.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (changeRoles)
                later.TruthTablePinAssignment.InputPinNames.Clear();
            else
                LogicSnapshotFixture.RemovePhysics(later);
        }
        finally { await GateViewSnapshotFixture.FinishAsync(extractor, running); }

        graph.NodesById["first"].Kind.ShouldBe(GateSymbolKind.Or);
        graph.NodesById["later"].Kind.ShouldBe(GateSymbolKind.And,
            "every gate input belongs to the same caller capture, before the first solve awaits");
    }

    [Fact]
    public async Task NumericMatrixEdit_WithSameLegacySignatureDoesNotReuseOldSymbol()
    {
        var group = LogicSnapshotFixture.Gate("gate");
        var classifier = new GateFunctionClassifier();
        var first = GateViewSnapshotFixture.Graph(group);
        await classifier.ClassifyAsync(first, 1550);
        first.Nodes.Single().Kind.ShouldBe(GateSymbolKind.Or);
        var legacyKey = GateStructureSignature.Of(group, 1550);
        foreach (var child in group.ChildComponents)
            foreach (var matrix in child.WaveLengthToSMatrixMap.Values)
                matrix.SetValues(new(), reset: true);
        GateStructureSignature.Of(group, 1550).ShouldBe(legacyKey);

        var changed = GateViewSnapshotFixture.Graph(group);
        await classifier.ClassifyAsync(changed, 1550);
        changed.Nodes.Single().Kind.ShouldBe(GateSymbolKind.Unknown,
            "zero transmission cannot reuse the old OR classification");
    }

    [Fact]
    public async Task ThresholdsOnOppositeSidesOfPower_AreNotRoundedIntoOneCacheEntry()
    {
        var group = LogicSnapshotFixture.Gate("gate");
        group.TruthTablePinAssignment!.Threshold = 0.49999;
        var classifier = new GateFunctionClassifier();
        var first = GateViewSnapshotFixture.Graph(group);
        await classifier.ClassifyAsync(first, 1550);
        first.Nodes.Single().Kind.ShouldBe(GateSymbolKind.Or);
        var legacyKey = GateStructureSignature.Of(group, 1550);
        group.TruthTablePinAssignment.Threshold = 0.50001;
        GateStructureSignature.Of(group, 1550).ShouldBe(legacyKey);

        var changed = GateViewSnapshotFixture.Graph(group);
        await classifier.ClassifyAsync(changed, 1550);
        changed.Nodes.Single().Kind.ShouldBe(GateSymbolKind.And);
    }

    [Fact]
    public async Task EquivalentCellsWithDifferentFlowIds_ReuseClassificationAcrossRebuilds()
    {
        var first = LogicSnapshotFixture.Gate("first");
        var copy = first.DeepCopy();
        copy.GroupName = "copy";
        copy.ExternalPins[0].InternalPin.LogicalPin!.IDInFlow
            .ShouldNotBe(first.ExternalPins[0].InternalPin.LogicalPin!.IDInFlow);
        var extractor = new GateViewSnapshotFixture.CountingExtractor();
        var classifier = new GateFunctionClassifier(extractor);
        var graph = GateViewSnapshotFixture.Graph(first, copy);
        await classifier.ClassifyAsync(graph, 1550);
        await classifier.ClassifyAsync(GateViewSnapshotFixture.Graph(first, copy), 1550);

        graph.Nodes.ShouldAllBe(node => node.Kind == GateSymbolKind.Or);
        extractor.Calls.ShouldBe(1, "equivalent cells and unchanged rebuilds share the captured physics cache");
        classifier.CachedStructureCount.ShouldBe(1);
    }

    [Fact]
    public async Task RegisterAndIncompleteGate_KeepStructuralFallback()
    {
        var register = LogicSnapshotFixture.Gate("register");
        register.TruthTablePinAssignment!.IsRegister = true;
        var incomplete = LogicSnapshotFixture.Gate("incomplete");
        LogicSnapshotFixture.RemovePhysics(register);
        LogicSnapshotFixture.RemovePhysics(incomplete);
        var graph = GateViewSnapshotFixture.Graph(register, incomplete);
        await new GateFunctionClassifier().ClassifyAsync(graph, 1550);
        graph.NodesById["register"].Kind.ShouldBe(GateSymbolKind.Register);
        graph.NodesById["incomplete"].Kind.ShouldBe(GateSymbolKind.Unknown);
    }

    [Fact]
    public async Task RepeatedPhysicsEdits_BoundRetainedIdentitiesAndKeepWarmResultReusable()
    {
        var group = LogicSnapshotFixture.Gate("gate");
        var extractor = new GateViewSnapshotFixture.CountingExtractor();
        var classifier = new GateFunctionClassifier(extractor);
        for (var revision = 1; revision <= 260; revision++)
        {
            group.TruthTablePinAssignment!.Threshold = revision / 1000d;
            await classifier.ClassifyAsync(GateViewSnapshotFixture.Graph(group), 1550);
        }
        classifier.CachedStructureCount.ShouldBeLessThanOrEqualTo(256);
        var calls = extractor.Calls;
        var warm = GateViewSnapshotFixture.Graph(group);
        await classifier.ClassifyAsync(warm, 1550);
        extractor.Calls.ShouldBe(calls);
        warm.Nodes.Single().Kind.ShouldBe(GateSymbolKind.Or);
    }

    [Fact]
    public async Task Classification_DoesNotMaterializeOrReplaceLiveGroupPins()
    {
        var group = global::UnitTests.Analysis.LogicAnalysis.LogicGateFixtureFactory.CreateCombinerGroup();
        group.TruthTablePinAssignment = new()
        {
            InputPinNames = ["a", "b"], OutputPinNames = ["y"], Threshold = 0.25
        };
        group.PhysicalPins.ShouldBeEmpty();
        var graph = GateViewSnapshotFixture.Graph(group);
        await new GateFunctionClassifier().ClassifyAsync(graph, 1550);
        graph.Nodes.Single().Kind.ShouldBe(GateSymbolKind.Or);
        group.PhysicalPins.ShouldBeEmpty("GateView inspection must not mutate connectable design pins");
    }
}
