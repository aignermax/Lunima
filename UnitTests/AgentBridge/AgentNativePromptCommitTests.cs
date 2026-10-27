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

public sealed class AgentNativePromptCommitTests
{
    [AvaloniaTheory]
    [InlineData("close")]
    [InlineData("cancel")]
    [InlineData("replace")]
    public async Task OldSavePrompt_CannotSaveOrReplaceTheCurrentDocument(string revoke)
    {
        var files = new Mock<INativeDesignFileStore>();
        files.Setup(store => store.WriteAsync(It.IsAny<string>(), It.IsAny<DesignFileData>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        await using var main = MainViewModelTestHelper.CreateMainViewModel(designFiles: files.Object);
        var picker = new Mock<IFileDialogService>();
        picker.Setup(service => service.ShowSaveFileDialogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("must-not-save.lun");
        main.FileOperations.FileDialogService = picker.Object;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var answer = new TaskCompletionSource<SavePromptResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var messages = new Mock<IMessageBoxService>();
        messages.Setup(service => service.ShowSavePromptAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(() => { entered.TrySetResult(); return answer.Task; });
        main.FileOperations.MessageBoxService = messages.Object;
        main.FileOperations.HasUnsavedChanges = true;
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
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (revoke == "close") closing = catalog.CloseAdmissionAsync();
            if (revoke == "cancel") await caller.CancelAsync();
            if (revoke == "replace") main.Canvas.Document.ReplaceDocument();
            running.IsCompleted.ShouldBeFalse("an accepted dialog must finish its actual cleanup");
            answer.TrySetResult(SavePromptResult.Save);
            try { await running.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (OperationCanceledException) { }
            await closing.WaitAsync(TimeSpan.FromSeconds(5));
            picker.Invocations.ShouldBeEmpty("a stale prompt cannot open another dialog");
            files.Invocations.ShouldBeEmpty("a stale prompt cannot start any file operation");
            main.FileOperations.CurrentFilePath.ShouldBeNull();
            main.FileOperations.HasUnsavedChanges.ShouldBeTrue();
            session.LoadedExamplePath.ShouldBeNull();
        }
        finally
        {
            answer.TrySetResult(SavePromptResult.Cancel);
            try { await running.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (OperationCanceledException) { }
            await catalog.CloseAdmissionAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
}
