using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Export.Contracts;
using Moq;

namespace UnitTests.Export;

internal sealed class ExportWorkflowLifetimeFixture : IAsyncDisposable
{
    internal static readonly TimeSpan Guard = TimeSpan.FromSeconds(5);
    internal string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), $"export-workflow-{Guid.NewGuid():N}");
    internal string ScriptPath => Path.Combine(DirectoryPath, "layout.py");
    internal DesignCanvasViewModel Canvas { get; } = new();
    internal HeldRunner Runner { get; } = new();
    internal Mock<IUrlLauncher> Launcher { get; } = new();

    internal ExportWorkflowLifetimeFixture(bool nazca = false)
    {
        Directory.CreateDirectory(DirectoryPath);
        var component = TestComponentFactory.CreateBasicComponent();
        component.Identifier = "C1";
        component.GdsFactoryFunction = nazca ? null : "straight";
        component.NazcaFunctionName = nazca ? "demofab.strt" : string.Empty;
        Canvas.AddComponent(component, "export component");
    }

    internal static async Task FinishAsync(Task running)
    {
        try { await running.WaitAsync(Guard); }
        catch (OperationCanceledException) when (running.IsCompleted) { }
    }

    public async ValueTask DisposeAsync()
    {
        Runner.Release.TrySetResult();
        await Canvas.Document.RetireAsync();
        Canvas.DisposeDocumentBindings();
        Directory.Delete(DirectoryPath, true);
    }

    internal sealed class HeldRunner : IGdsExportSession
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Canceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal CancellationToken Token { get; private set; }
        internal int Calls { get; private set; }
        internal string? LastScript { get; private set; }
        internal bool FailAsMissingPackage { get; set; }

        public Task<GdsExportResult> ExportToGdsAsync(string path, bool generate) => RunAsync(path, default);
        public Task<GdsExportResult> ExportToGdsAsync(string path, bool generate, CancellationToken token) => RunAsync(path, token);
        private async Task<GdsExportResult> RunAsync(string path, CancellationToken token)
        {
            Calls++;
            LastScript = path;
            Token = token;
            using var registration = token.Register(() => Canceled.TrySetResult());
            Started.TrySetResult();
            // Deliberately finish after cancellation so late publication and actual drain are observable.
            await Release.Task;
            if (!FailAsMissingPackage)
                await File.WriteAllTextAsync(Path.ChangeExtension(path, ".gds"), "controlled accepted artifact");
            return new GdsExportResult
            {
                ScriptPath = path, GdsPath = FailAsMissingPackage ? null : Path.ChangeExtension(path, ".gds"),
                Success = !FailAsMissingPackage, Status = "finished accepted export",
                ErrorMessage = FailAsMissingPackage ? "No module named 'gdsfactory'" : null
            };
        }
        public void SetCustomPythonPath(string? path) { }
        public string GetCurrentPythonPath() => "controlled-python";
        public Task<PythonEnvironmentInfo> CheckPythonEnvironmentAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new PythonEnvironmentInfo { PythonAvailable = true, NazcaAvailable = true });
    }

    internal sealed class Picker(string path, bool hold = false) : IFileDialogService
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<string?> ShowSaveFileDialogAsync(string title, string defaultExtension, string filters)
        {
            Started.TrySetResult();
            if (hold) await Release.Task;
            return path;
        }
        public Task<string?> ShowOpenFileDialogAsync(string title, string filters) => Task.FromResult<string?>(null);
    }
}
