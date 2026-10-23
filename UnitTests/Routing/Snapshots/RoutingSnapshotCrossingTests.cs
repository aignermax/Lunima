using CAP_Core.Components.Core;
using CAP_Core.Routing.Snapshots;
using Shouldly;
using UnitTests.Routing.CrossingInsertion;

namespace UnitTests.Routing.Snapshots;

public sealed class RoutingSnapshotCrossingTests
{
    [Fact]
    public void ExistingRegistry_PreservesUnsplitIdentityAnchorsAndSettingsWithoutCallingMaintenanceFactory()
    {
        var allowFactory = true;
        var fixture = CrossingTestCircuit.Build(0.5, () => allowFactory
            ? CrossingTestCircuit.CreateCrossingComponent()
            : throw new InvalidOperationException("Maintenance must not resolve an insertion prototype."));
        var record = fixture.Service.Records.Single();
        record.OriginalA.TargetLengthMicrometers = 1234;
        record.OriginalA.PropagationLossDbPerCm = 6.5;
        var originalId = record.OriginalA.Id;
        var anchor = record.AnchorPositions[0];
        fixture.Manager.CrossingInsertion = null;
        fixture.Manager.CrossingMaintenance = fixture.Service;
        allowFactory = false;
        var snapshot = RoutingInputSnapshot.Capture(Components(fixture), fixture.Router, fixture.Manager);
        record.AnchorPositions.Clear();
        record.OriginalA.PropagationLossDbPerCm = 99;
        fixture.Service.Reset();

        var graph = snapshot.CreatePrivateGraph();
        graph.Manager.CrossingInsertion.ShouldBeNull();
        var service = graph.Manager.CrossingMaintenance.ShouldNotBeNull();
        var copy = service.Records.Single();
        copy.OriginalA.Id.ShouldBe(originalId);
        copy.OriginalA.TargetLengthMicrometers.ShouldBe(1234);
        copy.OriginalA.PropagationLossDbPerCm.ShouldBe(6.5);
        copy.AnchorPositions[0].ShouldBe(anchor);
        copy.CrossingComponent.ShouldNotBeSameAs(record.CrossingComponent);
        copy.SubConnectionsA.All(graph.Manager.Connections.Contains).ShouldBeTrue();
        service.ResolveToOriginal(copy.SubConnectionsA[0]).ShouldBeSameAs(copy.OriginalA);
        snapshot.CreatePrivateGraph().Manager.CrossingService!.Records.Single().OriginalA.ShouldNotBeSameAs(copy.OriginalA);
    }

    [Fact]
    public void CapturedPrototype_CreatesFreshCompleteComponentsWithoutCallingLiveFactoryOnWorker()
    {
        var allowed = true;
        var calls = 0;
        Component Factory()
        {
            if (!allowed) throw new InvalidOperationException("The worker consulted the live PDK factory.");
            calls++;
            var component = CrossingTestCircuit.CreateCrossingComponent();
            component.TemplateName = "User crossing";
            component.NazcaModuleName = "user_pdk";
            component.NazcaFunctionParameters = "width=0.47";
            component.TypeNumber = 73;
            return component;
        }
        var fixture = CrossingTestCircuit.Build(0.0001, Factory);
        var beforeCapture = calls;
        var snapshot = RoutingInputSnapshot.Capture(Components(fixture), fixture.Router, fixture.Manager);
        calls.ShouldBe(beforeCapture + 1, "capture resolves one prototype on the owning thread");
        allowed = false;
        var factory = snapshot.CreatePrivateGraph().Manager.CrossingInsertion!.CrossingComponentFactory;
        var first = factory().ShouldNotBeNull();
        var second = factory().ShouldNotBeNull();
        first.Id.ShouldNotBe(second.Id);
        first.PhysicalPins.Select(pin => pin.PinId).Intersect(second.PhysicalPins.Select(pin => pin.PinId)).ShouldBeEmpty();
        first.PhysicalPins.Select(pin => pin.LogicalPin.IDInFlow)
            .Intersect(second.PhysicalPins.Select(pin => pin.LogicalPin.IDInFlow)).ShouldBeEmpty();
        first.TemplateName.ShouldBe("User crossing");
        first.NazcaModuleName.ShouldBe("user_pdk");
        first.NazcaFunctionParameters.ShouldBe("width=0.47");
        first.TypeNumber.ShouldBe(73);
        first.GetAllPins().Count().ShouldBe(4);
        var west = first.PhysicalPins.Single(pin => pin.Name == "port 1").LogicalPin;
        var east = first.PhysicalPins.Single(pin => pin.Name == "port 2").LogicalPin;
        var matrix = first.WaveLengthToSMatrixMap[1550];
        matrix.GetNonNullValues()[(west.IDInFlow, east.IDOutFlow)].Magnitude
            .ShouldBe(CrossingTestCircuit.ThroughMagnitude, 1e-12);
    }

    [Fact]
    public async Task PrivateInsertion_RecordsUndoAndNeverInvokesEditorCallbacks()
    {
        var fixture = CrossingTestCircuit.Build(0.0001);
        fixture.Service.Records.ShouldBeEmpty();
        foreach (var wire in fixture.Manager.Connections) wire.ExcessBendLossDbPer90Deg = 0.5;
        fixture.Manager.DefaultExcessBendLossDbPer90Deg = 0.5;
        var originalIds = fixture.Manager.Connections.Select(wire => wire.Id).ToArray();
        fixture.Service.ComponentAdded = _ => throw new InvalidOperationException("Live component-added callback.");
        fixture.Service.ComponentRemoved = _ => throw new InvalidOperationException("Live component-removed callback.");
        fixture.Service.ChangeRecorded = _ => throw new InvalidOperationException("Live undo callback.");
        var graph = RoutingInputSnapshot.Capture(Components(fixture), fixture.Router, fixture.Manager).CreatePrivateGraph();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        await Task.Run(() => graph.Route(stop.Token)).WaitAsync(TimeSpan.FromSeconds(10));
        stop.Token.ThrowIfCancellationRequested();

        graph.Manager.CrossingInsertion!.Records.Count.ShouldBe(1);
        graph.Components.Count(component => component.IsInsertedCrossing).ShouldBe(1);
        graph.CrossingChanges.Count.ShouldBe(1);
        fixture.Manager.Connections.Select(wire => wire.Id).ShouldBe(originalIds);
        fixture.Service.Records.ShouldBeEmpty();
        graph.CrossingChanges.Single().Undo(graph.Manager).ShouldBeTrue();
        graph.Manager.Connections.Select(wire => wire.Id).Order().ShouldBe(originalIds.Order());
        graph.Components.Any(component => component.IsInsertedCrossing).ShouldBeFalse();
    }

    [Fact]
    public async Task MaintenanceOnly_MovedAnchorDissolvesPrivatelyAndKeepsLiveCrossingUntouched()
    {
        var fixture = CrossingTestCircuit.Build(0.5);
        var crossing = fixture.Service.Records.Single().CrossingComponent;
        fixture.Manager.CrossingInsertion = null;
        fixture.Manager.CrossingMaintenance = fixture.Service;
        fixture.ALeft.Component.PhysicalY += 40;
        fixture.Service.ComponentRemoved = _ => throw new InvalidOperationException("Live removal callback.");
        var graph = RoutingInputSnapshot.Capture(Components(fixture), fixture.Router, fixture.Manager).CreatePrivateGraph();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        await Task.Run(() => graph.Route(stop.Token)).WaitAsync(TimeSpan.FromSeconds(10));
        stop.Token.ThrowIfCancellationRequested();

        graph.Manager.CrossingMaintenance!.Records.ShouldBeEmpty();
        graph.Components.Any(component => component.Id == crossing.Id).ShouldBeFalse();
        graph.Manager.Connections.Count.ShouldBe(2);
        graph.CrossingChanges.ShouldNotBeEmpty();
        fixture.Service.Records.Single().CrossingComponent.ShouldBeSameAs(crossing);
        fixture.Manager.Connections.Count.ShouldBe(4);
    }

    [Fact]
    public async Task QueuedRerouteInPlace_DoesNotDissolveItsUnrecordedCrossingChain()
    {
        var fixture = CrossingTestCircuit.Build(0.5);
        var record = fixture.Service.Records.Single();
        fixture.Service.ReleaseRecord(record.CrossingComponent).ShouldBeSameAs(record);
        fixture.Manager.RerouteInPlace(record.SubConnectionsA[0]);
        fixture.Manager.CrossingInsertion = null;
        fixture.Manager.CrossingMaintenance = fixture.Service;
        var graph = RoutingInputSnapshot.Capture(Components(fixture), fixture.Router, fixture.Manager).CreatePrivateGraph();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        await Task.Run(() => graph.Route(stop.Token)).WaitAsync(TimeSpan.FromSeconds(10));
        stop.Token.ThrowIfCancellationRequested();

        graph.Components.Single(component => component.IsInsertedCrossing).Id.ShouldBe(record.CrossingComponent.Id);
        graph.Manager.Connections.Count.ShouldBe(4);
        graph.CrossingChanges.ShouldBeEmpty();
        fixture.Manager.Connections.Single(wire => wire.Id == record.SubConnectionsA[0].Id).RoutedPath.ShouldBeNull();
    }

    private static Component[] Components(CrossingTestCircuit.CrossLayout fixture) =>
        new[] { fixture.ALeft.Component, fixture.ARight.Component, fixture.BTop.Component, fixture.BBottom.Component }
            .Concat(fixture.AddedCrossings).ToArray();
}
