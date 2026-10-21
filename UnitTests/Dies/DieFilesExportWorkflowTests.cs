using System.Collections.ObjectModel;
using System.Globalization;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Export;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core.Export;
using Shouldly;
using Xunit;

namespace UnitTests.Dies;

public class DieFilesExportWorkflowTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Batch_PreservesCollidingDieNames_AndReportsActualGdsResults(bool ready)
    {
        var directory = Path.Combine(Path.GetTempPath(), "die-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var canvas = new DesignCanvasViewModel();
            canvas.AddComponent(DieTestFixtures.Die("Die A", 0, 0, 100, 100));
            canvas.AddComponent(DieTestFixtures.Die("Die-A", 200, 0, 100, 100));
            var service = new BatchService(ready);
            var export = new GdsExportViewModel(service) { GenerateGdsEnabled = true };
            var dialogs = new Dialogs(Path.Combine(directory, "chip.py"));
            var operations = new FileOperationsViewModel(canvas, new CommandManager(),
                new SimpleNazcaExporter(), new SaxExporter(), new ObservableCollection<ComponentTemplate>(),
                export, new PhotonTorchExportViewModel(new PhotonTorchExporter(), canvas), null!);
            operations.FileDialogService = dialogs;
            operations.MessageBoxService = dialogs;
            string status = "";
            operations.UpdateStatus = value => status = value;

            await operations.ExportDieFilesCommand.ExecuteAsync(null);

            Directory.GetFiles(directory, "*.py").Length.ShouldBe(2);
            if (ready)
            {
                Directory.GetFiles(directory, "*.gds").Length.ShouldBe(1);
                status.ShouldContain(string.Format(CultureInfo.CurrentCulture, LocalizationService.Instance.Translate("Dies.Export.Summary"), 2, 1));
                status.ShouldContain(string.Format(CultureInfo.CurrentCulture, LocalizationService.Instance.Translate("Dies.Export.Failures"), 1, "chip_Die_A_2.py: fixture failure"));
            }
            else
            {
                dialogs.Choices.ShouldBe(1);
                status.ShouldContain(LocalizationService.Instance.Translate("Dies.Export.GdsSkipped"));
            }
        }
        finally { Directory.Delete(directory, true); }
    }

    private sealed class BatchService(bool ready) : GdsExportService
    {
        private int _exports;
        protected override Task<PythonEnvironmentInfo> ProbeEnvironmentAsync() => Task.FromResult(
            new PythonEnvironmentInfo { PythonAvailable = ready, NazcaAvailable = ready });
        public override async Task<ExportResult> ExportToGdsAsync(string scriptPath, bool generateGds)
        {
            if (++_exports == 2) return new ExportResult { Success = false, ErrorMessage = "fixture failure" };
            var path = Path.ChangeExtension(scriptPath, ".gds");
            await File.WriteAllTextAsync(path, "fixture GDS");
            return new ExportResult { Success = true, GdsPath = path };
        }
    }

    private sealed class Dialogs(string path) : IFileDialogService, IMessageBoxService
    {
        public int Choices { get; private set; }
        public Task<string?> ShowSaveFileDialogAsync(string title, string defaultExtension, string filters) => Task.FromResult<string?>(path);
        public Task<string?> ShowOpenFileDialogAsync(string title, string filters) => Task.FromResult<string?>(null);
        public Task<SavePromptResult> ShowSavePromptAsync(string message, string title) => Task.FromResult(SavePromptResult.Cancel);
        public Task<int> ShowChoicePromptAsync(string message, string title, IReadOnlyList<string> buttonLabels)
        { Choices++; return Task.FromResult(2); }
    }
}
