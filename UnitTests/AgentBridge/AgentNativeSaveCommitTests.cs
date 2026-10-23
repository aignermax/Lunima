using System.Text.Json;
using Avalonia.Headless.XUnit;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.AgentBridge.Tools;
using CAP.Avalonia.Services.AgentBridge.Tools.Session;
using CAP.Avalonia.Services.AiTools;
using CAP_DataAccess.Persistence.NativeDesign;
using Moq;
using Shouldly;
using UnitTests.Helpers;

namespace UnitTests.AgentBridge;

public sealed class AgentNativeSaveCommitTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AcceptedWriteDrains_ButRevokedToolCannotPublishFileState(bool cancelCaller)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new Mock<INativeDesignFileStore>();
        store.Setup(files => files.WriteAsync(It.IsAny<string>(), It.IsAny<DesignFileData>(), It.IsAny<CancellationToken>()))
            .Returns(() => { entered.TrySetResult(); return release.Task; });
        await using var main = MainViewModelTestHelper.CreateMainViewModel(designFiles: store.Object);
        main.FileOperations.HasUnsavedChanges = true;
        var session = new AgentDesignSession(main.Canvas, new CommandManager(), () => main.FileOperations, new ExampleDesignsService());
        var tool = new SaveDesignTool(session);
        var catalog = new AgentToolCatalog([tool], new AiToolRegistry([]), new InlineUiDispatcher());
        using var caller = new CancellationTokenSource();
        var running = catalog.InvokeAsync(tool.Name, JsonSerializer.SerializeToElement(new
        {
            path = Path.Combine(Path.GetTempPath(), $"lunima-write-permission-{Guid.NewGuid():N}.lun")
        }), caller.Token);
        Task closing = Task.CompletedTask;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (cancelCaller) await caller.CancelAsync();
            else closing = catalog.CloseAdmissionAsync();
            running.IsCompleted.ShouldBeFalse();
            if (!cancelCaller) closing.IsCompleted.ShouldBeFalse();
            release.TrySetResult();
            try { await running.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (OperationCanceledException) { }
            await closing.WaitAsync(TimeSpan.FromSeconds(5));
            main.FileOperations.CurrentFilePath.ShouldBeNull();
            main.FileOperations.HasUnsavedChanges.ShouldBeTrue();
            store.Verify(files => files.WriteAsync(It.IsAny<string>(), It.IsAny<DesignFileData>(), It.IsAny<CancellationToken>()), Times.Once);
        }
        finally
        {
            release.TrySetResult();
            try { await running.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (OperationCanceledException) { }
            await catalog.CloseAdmissionAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
}
