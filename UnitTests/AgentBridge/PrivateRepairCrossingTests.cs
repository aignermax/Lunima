using Avalonia.Headless.XUnit;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.AgentBridge.Tools;
using CAP.Avalonia.Services.AgentBridge.Tools.Repair.Suggest;
using CAP.Avalonia.ViewModels.Canvas.CrossingInsertion;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Routing.CrossingInsertion;
using CAP_Core.Routing.Snapshots;
using Shouldly;

namespace UnitTests.AgentBridge;

public sealed class PrivateRepairCrossingTests
{
    [AvaloniaFact]
    public async Task ManualCrossingSupply_OwnsPhysicsAndProcessRulesAfterLiveTemplateChanges()
    {
        var fixture = new RepairTrialFixture();
        try
        {
            var template = TestPdkLoader.LoadFromPdk("siepic-ebeam-pdk.json")
                .Single(value => value.NazcaFunctionName == CrossingComponentInstance.CrossingNazcaFunctionName);
            var expected = ComponentTemplates.CreateFromTemplate(template, 0, 0);
            var loss = ThroughLoss(expected);
            var name = template.Name;
            var width = template.WidthMicrometers;
            var allowProvider = true;
            fixture.Canvas.Router.ConnectionProcessFloorProvider = (_, _) => allowProvider
                ? 37 : throw new InvalidOperationException("Live process rules were read after capture.");
            var session = new AgentDesignSession(fixture.Canvas, fixture.Commands, () => null, new ExampleDesignsService(new CAP.Avalonia.Services.FileExampleCatalog()), () => [template]);
            var captured = RepairCrossingTemplate.Capture(session).ShouldNotBeNull();
            var input = RoutingInputSnapshot.Capture(fixture.Canvas.Components.Select(view => view.Component).ToArray(),
                fixture.Canvas.Router, fixture.Canvas.ConnectionManager);
            allowProvider = false;
            template.Name = "changed";
            template.WidthMicrometers = 900;
            template.CreateSMatrix = _ => throw new InvalidOperationException("Live matrix factory.");
            template.CreateSMatrixWithSliders = (_, _) => throw new InvalidOperationException("Live slider factory.");
            template.CreateWavelengthSMatrixMap = _ => throw new InvalidOperationException("Live wavelength factory.");
            await Task.Yield();
            await using var permission = new RepairTrialPermission(session, null);
            await using var first = await RepairWorkbench.CreateAsync(new CAP.Avalonia.Services.ExampleDesignsService(new CAP.Avalonia.Services.FileExampleCatalog()), input, captured, permission, null, (0, 0, 500, 300));
            await using var second = await RepairWorkbench.CreateAsync(new CAP.Avalonia.Services.ExampleDesignsService(new CAP.Avalonia.Services.FileExampleCatalog()), input, captured, permission, null, (0, 0, 500, 300));
            var one = CrossingComponentInstance.CreateFromTemplates(first.Session.Templates).ShouldNotBeNull();
            var two = CrossingComponentInstance.CreateFromTemplates(second.Session.Templates).ShouldNotBeNull();
            one.TemplateName.ShouldBe(name);
            one.Component.WidthMicrometers.ShouldBe(width);
            ThroughLoss(one.Component).ShouldBe(loss, 1e-10);
            ThroughLoss(two.Component).ShouldBe(loss, 1e-10);
            one.Component.WaveLengthToSMatrixMap[1550].ShouldNotBeSameAs(two.Component.WaveLengthToSMatrixMap[1550]);
            one.Component.PhysicalPins.Select(pin => pin.PinId).Intersect(two.Component.PhysicalPins.Select(pin => pin.PinId)).ShouldBeEmpty();
            var terminal = first.Session.Canvas.Components[0].Component.PhysicalPins[0];
            first.Session.Canvas.Router.ResolveProcessFloorFor(terminal, one.Component.PhysicalPins[0]).ShouldBe(37);
            one.Component.WaveLengthToSMatrixMap[1550].SMat.Clear();
            ThroughLoss(two.Component).ShouldBe(loss, 1e-10);
        }
        finally { await fixture.FinishAsync(Task.CompletedTask); }
    }
    private static double ThroughLoss(CAP_Core.Components.Core.Component component)
    {
        var loss = new CrossingInserter().GetCrossingThroughLossDb(component);
        loss.ShouldNotBeNull();
        return loss.Value;
    }
}
