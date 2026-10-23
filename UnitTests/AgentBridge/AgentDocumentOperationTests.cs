using Avalonia.Headless.XUnit;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.AgentBridge.Tools;
using CAP.Avalonia.Services.AiTools;
using CAP.Avalonia.ViewModels.Canvas;
using Shouldly;

namespace UnitTests.AgentBridge;

public sealed class AgentDocumentOperationTests
{
    [AvaloniaTheory]
    [InlineData("replace")]
    [InlineData("scope")]
    [InlineData("edit")]
    [InlineData("close")]
    [InlineData("cancel")]
    public async Task RevokedEdit_CannotContinueAfterAnAwait(string revoke)
    {
        var canvas = new DesignCanvasViewModel();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Exception? rejected = null;
        var mutations = 0;
        var tool = new ContextLifecycleProbeTool(async context =>
        {
            await using var edit = new AgentDocumentOperation(canvas, context);
            entered.TrySetResult();
            await release.Task;
            try { edit.Commit(() => mutations++); }
            catch (OperationCanceledException error) { rejected = error; }
        });
        var catalog = new AgentToolCatalog([tool], new AiToolRegistry([]), new InlineUiDispatcher());
        using var caller = new CancellationTokenSource();
        var running = catalog.InvokeAsync(tool.Name, AgentBridgeTestStack.Parse("{}"), caller.Token);
        Task closing = Task.CompletedTask;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            switch (revoke)
            {
                case "replace": canvas.Document.ReplaceDocument(); break;
                case "scope": canvas.Document.ChangeScope(Guid.NewGuid()); break;
                case "edit": canvas.Document.Invalidate(); break;
                case "close": closing = catalog.CloseAdmissionAsync(); break;
                case "cancel": await caller.CancelAsync(); break;
            }
            release.TrySetResult();
            if (revoke is "close" or "cancel")
                await Should.ThrowAsync<OperationCanceledException>(() => running.WaitAsync(TimeSpan.FromSeconds(5)));
            else
                await running.WaitAsync(TimeSpan.FromSeconds(5));
            await closing.WaitAsync(TimeSpan.FromSeconds(5));
            mutations.ShouldBe(0);
            rejected.ShouldBeOfType<OperationCanceledException>();
        }
        finally
        {
            release.TrySetResult();
            try { await running.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (OperationCanceledException) { }
            await catalog.CloseAdmissionAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await canvas.Document.RetireAsync().WaitAsync(TimeSpan.FromSeconds(5));
            canvas.DisposeDocumentBindings();
        }
    }

    [AvaloniaFact]
    public void OwnCommandNotifications_AdvanceTheLease_ButOtherEditsRevokeIt()
    {
        var canvas = new DesignCanvasViewModel();
        try
        {
            using var edit = new AgentDocumentOperation(canvas);
            using var peer = canvas.Document.CaptureOperation();
            edit.Commit(() => canvas.AddComponent(TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins()));
            peer.IsCurrent.ShouldBeFalse();
            edit.EnsureCurrent();
            edit.Commit(() => canvas.AddComponent(TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins()));
            canvas.Components.Count.ShouldBe(2);
            canvas.Document.Invalidate();
            Should.Throw<OperationCanceledException>(edit.EnsureCurrent);
        }
        finally { canvas.DisposeDocumentBindings(); }
    }

    [AvaloniaFact]
    public void DisposedEdit_CannotMutateOrReportSuccess()
    {
        var canvas = new DesignCanvasViewModel();
        try
        {
            var edit = new AgentDocumentOperation(canvas);
            edit.Dispose();
            Should.Throw<OperationCanceledException>(() => edit.Commit(() => throw new InvalidOperationException("must not execute")));
            Should.Throw<OperationCanceledException>(edit.EnsureCurrent);
        }
        finally { canvas.DisposeDocumentBindings(); }
    }
}
