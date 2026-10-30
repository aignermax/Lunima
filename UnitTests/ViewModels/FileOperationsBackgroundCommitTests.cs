using System.Collections.ObjectModel;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Export;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core.Export;
using CAP_DataAccess.Persistence.NativeDesign;
using Moq;
using Shouldly;

namespace UnitTests.ViewModels;

/// <summary>
/// A background commit (e.g. a finished routing pass) while a file dialog or write is pending
/// must not silently drop the user's Save, Save-on-close or the saved file path.
/// </summary>
public sealed class FileOperationsBackgroundCommitTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private readonly DesignCanvasViewModel _canvas = new();
    private readonly GatedDesignFileStore _store = new();
    private readonly FileOperationsViewModel _fileOps;
    private readonly Mock<IFileDialogService> _fileDialog = new();
    private readonly Mock<IMessageBoxService> _messageBox = new();
    private readonly TaskCompletionSource<string?> _pickedPath = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"background-commit-{Guid.NewGuid():N}.lun");

    public FileOperationsBackgroundCommitTests()
    {
        var urlLauncher = Mock.Of<IUrlLauncher>();
        _fileOps = new FileOperationsViewModel(urlLauncher, _store, _canvas, new CommandManager(),
            new SimpleNazcaExporter(), new SaxExporter(), new ObservableCollection<ComponentTemplate>(),
            new GdsExportViewModel(new GdsExportService(), new PythonDiscoveryService()),
            new PhotonTorchExportViewModel(urlLauncher, new PhotonTorchExporter(), _canvas), null!);
        _fileDialog.Setup(d => d.ShowSaveFileDialogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(_pickedPath.Task);
        _fileOps.FileDialogService = _fileDialog.Object;
        _fileOps.MessageBoxService = _messageBox.Object;
    }

    public void Dispose()
    {
        _fileOps.Dispose();
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Fact]
    public async Task Save_BackgroundCommitWhileDialogOpen_StillWritesAndMarksClean()
    {
        _fileOps.HasUnsavedChanges = true;
        var saving = _fileOps.SaveDesignCommand.ExecuteAsync(null);

        CommitBackgroundResult();
        _pickedPath.SetResult(_path);
        await saving.WaitAsync(Timeout);

        File.Exists(_path).ShouldBeTrue();
        _fileOps.CurrentFilePath.ShouldBe(_path);
        _fileOps.HasUnsavedChanges.ShouldBeFalse();
    }

    [Fact]
    public async Task Save_BackgroundCommitDuringWrite_KeepsPathButStaysDirty()
    {
        _store.HoldWrites();
        _fileOps.HasUnsavedChanges = true;
        _pickedPath.SetResult(_path);
        var saving = _fileOps.SaveDesignCommand.ExecuteAsync(null);
        await _store.WriteStarted.WaitAsync(Timeout);

        CommitBackgroundResult();
        _store.ReleaseWrites();
        await saving.WaitAsync(Timeout);

        File.Exists(_path).ShouldBeTrue();
        _fileOps.CurrentFilePath.ShouldBe(_path);
        _fileOps.HasUnsavedChanges.ShouldBeTrue("the commit landed after the captured state was written");
    }

    [Fact]
    public async Task ConfirmClose_SaveChosenAndBackgroundCommitWhileDialogOpen_AllowsClose()
    {
        _fileOps.HasUnsavedChanges = true;
        _messageBox.Setup(m => m.ShowSavePromptAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(SavePromptResult.Save);
        var closing = _fileOps.ConfirmCloseAsync();

        CommitBackgroundResult();
        _pickedPath.SetResult(_path);

        (await closing.WaitAsync(Timeout)).ShouldBeTrue();
        File.Exists(_path).ShouldBeTrue();
    }

    [Fact]
    public async Task Save_DocumentReplacedWhileDialogOpen_DoesNotWrite()
    {
        var saving = _fileOps.SaveDesignCommand.ExecuteAsync(null);

        _canvas.Document.ReplaceDocument();
        _pickedPath.SetResult(_path);
        await saving.WaitAsync(Timeout);

        File.Exists(_path).ShouldBeFalse();
        _fileOps.CurrentFilePath.ShouldBeNull();
    }

    /// <summary>Publishes a worker result exactly like the routing orchestrator does.</summary>
    private void CommitBackgroundResult()
    {
        using var worker = _canvas.Document.CaptureOperation();
        worker.TryCommitAndAdvance(() => { }).ShouldBeTrue();
    }

    private sealed class GatedDesignFileStore : INativeDesignFileStore
    {
        private readonly NativeDesignFileStore _inner = new();
        private readonly TaskCompletionSource _writeStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource? _gate;

        public Task WriteStarted => _writeStarted.Task;

        public void HoldWrites() => _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void ReleaseWrites() => _gate?.TrySetResult();

        public bool Exists(string filePath) => _inner.Exists(filePath);

        public Task<string> ReadAsync(string filePath, CancellationToken cancellationToken = default) =>
            _inner.ReadAsync(filePath, cancellationToken);

        public async Task WriteAsync(string filePath, DesignFileData data, CancellationToken cancellationToken = default)
        {
            _writeStarted.TrySetResult();
            if (_gate != null) await _gate.Task;
            await _inner.WriteAsync(filePath, data, cancellationToken);
        }
    }
}
