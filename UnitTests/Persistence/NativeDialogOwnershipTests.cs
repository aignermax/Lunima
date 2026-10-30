using Avalonia.Headless.XUnit;
using CAP.Avalonia.Services;
using CAP_DataAccess.Persistence.NativeDesign;
using Moq;
using Shouldly;
using UnitTests.Helpers;

namespace UnitTests.Persistence;

public sealed class NativeDialogOwnershipTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SavePicker_CannotWriteAReplacementDocument(bool retire)
    {
        var store = new Mock<INativeDesignFileStore>();
        store.Setup(files => files.WriteAsync(It.IsAny<string>(), It.IsAny<DesignFileData>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        await using var main = MainViewModelTestHelper.CreateMainViewModel(designFiles: store.Object);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var selected = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var picker = new Mock<IFileDialogService>();
        picker.Setup(service => service.ShowSaveFileDialogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(() => { started.TrySetResult(); return selected.Task; });
        main.FileOperations.FileDialogService = picker.Object;
        main.FileOperations.HasUnsavedChanges = true;
        var saving = main.FileOperations.SaveDesignAsCommand.ExecuteAsync(null);
        Task retirement = Task.CompletedTask;
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (retire)
            {
                retirement = main.Canvas.Document.RetireAsync();
                retirement.IsCompleted.ShouldBeFalse("the file dialog still owns work until it actually closes");
            }
            else main.Canvas.Document.ReplaceDocument();
            selected.TrySetResult("must-not-write.lun");
            await saving.WaitAsync(TimeSpan.FromSeconds(5));
            await retirement.WaitAsync(TimeSpan.FromSeconds(5));
            store.Invocations.ShouldBeEmpty();
            main.FileOperations.CurrentFilePath.ShouldBeNull();
            main.FileOperations.HasUnsavedChanges.ShouldBeTrue();
        }
        finally
        {
            selected.TrySetResult(null);
            await saving.WaitAsync(TimeSpan.FromSeconds(5));
            await retirement.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
}
