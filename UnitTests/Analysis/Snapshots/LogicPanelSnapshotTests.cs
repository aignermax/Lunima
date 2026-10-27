using Avalonia.Headless.XUnit;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Analysis.LogicAnalysis;
using CAP.Avalonia.ViewModels.Canvas;
using Shouldly;

namespace UnitTests.Analysis.Snapshots;

public sealed class LogicPanelSnapshotTests
{
    [AvaloniaFact]
    public async Task RealAssembler_CapturesBeforeWorkerAndPanelPublishesOriginalPhysics()
    {
        var canvas = new DesignCanvasViewModel();
        var group = LogicSnapshotFixture.Gate("gate");
        canvas.Components.Add(new ComponentViewModel(group));
        var assembler = new LogicSnapshotFixture.HeldAssembler();
        var provider = new BuiltLogicNetworkProvider();
        await using var panel = new LogicPanelViewModel(builtNetworkProvider: provider, assembler: assembler);
        panel.Configure(canvas);
        var running = panel.BuildNetworkCommand.ExecuteAsync(null);
        try
        {
            try
            {
                await assembler.Barrier.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                // Deliberately bypass edit notifications: publication leases alone cannot isolate worker input.
                LogicSnapshotFixture.RemovePhysics(group);
            }
            finally
            {
                assembler.Barrier.Release.TrySetResult();
                await running.WaitAsync(TimeSpan.FromSeconds(10));
            }
            panel.HasNetwork.ShouldBeTrue(panel.StatusText);
            provider.Network.ShouldNotBeNull();
            provider.Network.Evaluate(new Dictionary<string, bool>
                { ["gate.a"] = true, ["gate.b"] = false })["gate.y"].ShouldBeTrue();
        }
        finally
        {
            await panel.StopAsync();
            await canvas.Document.RetireAsync();
            canvas.DisposeDocumentBindings();
        }
    }
}
