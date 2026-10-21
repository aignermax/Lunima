using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Components.Core;
using CAP_Core.Components.Creation;
using CAP_Core.Components.PinKinds;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.ComponentSettings.InstanceOverride;
using CAP_Core.Components.Connections;
using CAP_Core.Routing;
using CAP_DataAccess.Components.ComponentDraftMapper;
using Shouldly;
using Xunit;

namespace UnitTests.Components;

public class VerticalPortRoutingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GratingFiberPort_CannotBecomePlanarWaveguideEndpoint(bool reverse)
    {
        var component = Grating();
        component.PhysicalPins.Count.ShouldBe(2);
        var fiber = component.PhysicalPins.Single(p => p.Name == "o2");
        fiber.LogicalPin.ShouldNotBeNull();
        var waveguide = component.PhysicalPins.Single(p => p.Name == "o1");
        var manager = new WaveguideConnectionManager(new WaveguideRouter());
        Should.Throw<InvalidOperationException>(() => manager.AddConnectionDeferred(
            reverse ? fiber : waveguide, reverse ? waveguide : fiber));
        manager.Connections.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Grating Coupler Elliptical")]
    [InlineData("Grating Coupler Rectangular")]
    public void Grating_RetainsFiberSimulationTransfer_AndRoutableEdge(string templateName)
    {
        var component = Grating(templateName);
        var edge = component.PhysicalPins.Single(p => p.Name == "o1");
        var fiber = component.PhysicalPins.Single(p => p.Name == "o2");
        edge.IsPlanarRoutable.ShouldBeTrue();
        fiber.PortType.ShouldBe("vertical_te");
        fiber.IsPlanarRoutable.ShouldBeFalse();
        component.WaveLengthToSMatrixMap[1550].GetNonNullValues()[
            (fiber.LogicalPin.IDInFlow, edge.LogicalPin.IDOutFlow)].Magnitude.ShouldBeGreaterThan(0);
        var manager = new WaveguideConnectionManager(new WaveguideRouter());
        manager.AddConnectionDeferred(edge, Grating().PhysicalPins.Single(p => p.Name == "o1"));
        manager.Connections.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData("vertical_te")]
    [InlineData("vertical_tm")]
    public void VerticalPorts_AreRejectedByEveryManagerEntryPoint(string portType)
    {
        var fiber = new PhysicalPin { Name = "fiber", PortType = portType };
        var edge = new PhysicalPin { Name = "edge" };
        PinKindHelper.AreKindsCompatible(fiber, edge).ShouldBeTrue(); // Still optical.
        PinKindHelper.ArePlanarRoutingCompatible(fiber, edge).ShouldBeFalse();
        var manager = new WaveguideConnectionManager(new WaveguideRouter());
        Should.Throw<InvalidOperationException>(() => manager.AddConnection(fiber, edge));
        Should.Throw<InvalidOperationException>(() => manager.AddConnectionWithCachedRoute(edge, fiber, new RoutedPath()));
        Should.Throw<InvalidOperationException>(() => manager.AddExistingConnection(new WaveguideConnection { StartPin = edge, EndPin = fiber }));
        manager.Connections.ShouldBeEmpty();
    }

    [Fact]
    public void FiberRole_SurvivesClonePrefabAndInstanceOverride()
    {
        var component = Grating();
        var clone = (Component)component.Clone();
        clone.PhysicalPins.Single(p => p.Name == "o2").IsPlanarRoutable.ShouldBeFalse();
        var group = new ComponentGroup("Fiber");
        group.AddChild(component);
        var restored = GroupTemplateSerializer.Deserialize(GroupTemplateSerializer.Serialize(group));
        restored!.ChildComponents[0].PhysicalPins.Single(p => p.Name == "o2").IsPlanarRoutable.ShouldBeFalse();
        var data = OverridePinMapper.CaptureAsPinData(clone.PhysicalPins).Select(p => p.Clone()).ToList();
        OverridePinMapper.ApplyPinsToComponent(clone, data);
        clone.PhysicalPins.Single(p => p.Name == "o2").IsPlanarRoutable.ShouldBeFalse();
    }

    [Fact]
    public void LegacyInstanceOverride_InheritsFiberRoleFromCurrentTemplate()
    {
        var component = Grating();
        var legacyPins = OverridePinMapper.CaptureAsPinData(component.PhysicalPins);
        foreach (var pin in legacyPins) pin.PortType = null;
        OverridePinMapper.ApplyPinsToComponent(component, legacyPins);
        component.PhysicalPins.Single(p => p.Name == "o2").IsPlanarRoutable.ShouldBeFalse();
    }

    [Fact]
    public async Task InvalidCanvasRequest_PreservesExistingValidConnection()
    {
        var a = Grating();
        var b = Grating();
        var edge = a.PhysicalPins.Single(p => p.Name == "o1");
        var fiber = a.PhysicalPins.Single(p => p.Name == "o2");
        var canvas = new DesignCanvasViewModel();
        var valid = canvas.ConnectPins(edge, b.PhysicalPins.Single(p => p.Name == "o1"));
        canvas.ConnectPins(edge, fiber).ShouldBeNull();
        (await canvas.ConnectPinsAsync(fiber, edge)).ShouldBeNull();
        canvas.ConnectPinsWithCachedRoute(edge, fiber, new RoutedPath()).ShouldBeNull();
        canvas.Connections.Single().ShouldBeSameAs(valid);
    }

    private static Component Grating(string templateName = "Grating Coupler Elliptical")
    {
        var pdk = new PdkLoader().LoadFromFile(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
            "PDKs", "cornerstone-sin-pdk.json"));
        return ComponentTemplates.CreateFromTemplate(PdkTemplateConverter.ConvertToTemplate(
            pdk.Components.Single(c => c.Name == templateName), pdk.Name,
            pdk.NazcaModuleName, process: pdk.Process), 0, 0);
    }
}
