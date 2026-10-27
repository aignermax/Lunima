using System.Text.Json;
using Avalonia.Headless.XUnit;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.AgentBridge.Tools;
using CAP.Avalonia.Services.AgentBridge.Tools.Session;
using CAP.Avalonia.Services.AiTools;
using CAP_DataAccess.Persistence.NativeDesign;
using Shouldly;
using UnitTests.Helpers;

namespace UnitTests.AgentBridge;

public sealed class AgentNativeLoadCommitTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClosingAdmissionOrCancellingCaller_RevokesActualLoadCommitAndDrainsRead(bool cancelCaller)
    {
        var store = new PendingRead();
        await using var main = MainViewModelTestHelper.CreateMainViewModel(designFiles: store);
        var original = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        main.Canvas.AddComponent(original);
        main.FileOperations.HasUnsavedChanges = false;
        var examples = new ExampleDesignsService(new CAP.Avalonia.Services.FileExampleCatalog());
        var example = examples.GetExamples().First();
        var session = new AgentDesignSession(main.Canvas, new CommandManager(), () => main.FileOperations, examples);
        var tool = new LoadExampleTool(session);
        var catalog = new AgentToolCatalog([tool], new AiToolRegistry([]), new InlineUiDispatcher());
        using var caller = new CancellationTokenSource();
        var running = catalog.InvokeAsync(tool.Name,
            JsonSerializer.SerializeToElement(new { name = Path.GetFileName(example.FilePath) }), caller.Token);
        Task closing = Task.CompletedTask;
        try
        {
            await store.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (cancelCaller) await caller.CancelAsync();
            else closing = catalog.CloseAdmissionAsync();
            running.IsCompleted.ShouldBeFalse("the accepted file reader still owns its cleanup");
            if (!cancelCaller) closing.IsCompleted.ShouldBeFalse();
            store.Release.TrySetResult();
            try { await running.WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (OperationCanceledException) { }
            await closing.WaitAsync(TimeSpan.FromSeconds(10));

            main.Canvas.Components.Select(view => view.Component).ShouldBe(new[] { original });
            main.FileOperations.CurrentFilePath.ShouldBeNull();
            main.FileOperations.HasUnsavedChanges.ShouldBeFalse();
            session.LoadedExamplePath.ShouldBeNull();
        }
        finally
        {
            store.Release.TrySetResult();
            try { await running.WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (OperationCanceledException) { }
            await catalog.CloseAdmissionAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    private sealed class PendingRead : INativeDesignFileStore
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Exists(string path) => true;
        public async Task<string> ReadAsync(string path, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Release.Task;
            return "{\"FormatVersion\":\"2.0\",\"Components\":[],\"Connections\":[]}";
        }
        public Task WriteAsync(string path, DesignFileData data, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("This load must not save a document.");
    }
}
