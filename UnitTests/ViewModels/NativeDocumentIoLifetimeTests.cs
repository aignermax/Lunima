using Avalonia.Headless.XUnit;
using CAP_DataAccess.Persistence.NativeDesign;
using Shouldly;
using UnitTests.Helpers;

namespace UnitTests.ViewModels;

public sealed class NativeDocumentIoLifetimeTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OlderSave_DoesNotClearNewEditsOrReplaceAnotherDocumentsPath(bool loadAnother)
    {
        var store = new PendingStore();
        await using var main = MainViewModelTestHelper.CreateMainViewModel(designFiles: store);
        main.FileOperations.CurrentFilePath = "original.lun";
        var saving = main.FileOperations.SaveDesignToPathAsync("captured.lun");
        try
        {
            await store.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (loadAnother)
            {
                main.FileOperations.HasUnsavedChanges = false;
                (await main.FileOperations.LoadDesignFromPathAsync("other.lun")).ShouldBeTrue();
            }
            else main.Canvas.AddComponent(TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins());
            store.WriteRelease.TrySetResult();
            (await saving).ShouldBeTrue("the captured file was written successfully");
            // The written file is the current file; the edit made during the write keeps it dirty.
            main.FileOperations.CurrentFilePath.ShouldBe(loadAnother ? "other.lun" : "captured.lun");
            main.FileOperations.HasUnsavedChanges.ShouldBe(!loadAnother);
        }
        finally
        {
            store.WriteRelease.TrySetResult();
            await saving.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [AvaloniaFact]
    public async Task Retirement_DrainsAcceptedSaveAndPreventsLatePresentationChanges()
    {
        var store = new PendingStore();
        await using var main = MainViewModelTestHelper.CreateMainViewModel(designFiles: store);
        main.FileOperations.CurrentFilePath = "original.lun";
        var saving = main.FileOperations.SaveDesignToPathAsync("captured.lun");
        Task stopping = Task.CompletedTask;
        try
        {
            await store.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            stopping = main.StopAsync();
            stopping.IsCompleted.ShouldBeFalse("file I/O is accepted document work and must drain");
            store.WriteRelease.TrySetResult();
            await Task.WhenAll(saving, stopping).WaitAsync(TimeSpan.FromSeconds(10));
            main.FileOperations.CurrentFilePath.ShouldBe("original.lun");
        }
        finally
        {
            store.WriteRelease.TrySetResult();
            await Task.WhenAll(saving, stopping).WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [AvaloniaFact]
    public async Task PendingRead_CannotOverwriteEditsMadeAfterItStarted()
    {
        var store = new PendingStore { DelayRead = true };
        await using var main = MainViewModelTestHelper.CreateMainViewModel(designFiles: store);
        var opening = main.FileOperations.LoadDesignFromPathAsync("older.lun");
        try
        {
            await store.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var added = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
            main.Canvas.AddComponent(added);
            store.ReadRelease.TrySetResult();
            (await opening).ShouldBeFalse();
            main.Canvas.Components.ShouldContain(view => ReferenceEquals(view.Component, added));
            main.FileOperations.CurrentFilePath.ShouldBeNull();
            main.FileOperations.HasUnsavedChanges.ShouldBeTrue();
        }
        finally
        {
            store.ReadRelease.TrySetResult();
            await opening.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    private sealed class PendingStore : INativeDesignFileStore
    {
        public bool DelayRead { get; init; }
        public TaskCompletionSource WriteStarted { get; } = Signal();
        public TaskCompletionSource WriteRelease { get; } = Signal();
        public TaskCompletionSource ReadStarted { get; } = Signal();
        public TaskCompletionSource ReadRelease { get; } = Signal();
        public bool Exists(string filePath) => true;
        public async Task<string> ReadAsync(string filePath, CancellationToken cancellationToken = default)
        {
            ReadStarted.TrySetResult();
            if (DelayRead) await ReadRelease.Task;
            return "{\"FormatVersion\":\"2.0\",\"Components\":[],\"Connections\":[]}";
        }
        public async Task WriteAsync(string filePath, DesignFileData data, CancellationToken cancellationToken = default)
        {
            WriteStarted.TrySetResult();
            await WriteRelease.Task;
        }
        private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
