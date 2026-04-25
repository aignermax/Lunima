using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Export;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core;
using CAP_Core.Export;
using Shouldly;
using System.Collections.ObjectModel;
using Xunit;

namespace UnitTests.Export;

/// <summary>
/// Integration tests for the dialog-gated export commands added in issue #509.
/// Verifies that <see cref="FileOperationsViewModel.ExportVerilogAWithDialogCommand"/>
/// and <see cref="FileOperationsViewModel.ExportPhotonTorchWithDialogCommand"/>
/// respect the dialog delegate result — skipping the export on cancel and
/// proceeding on confirm.
/// </summary>
public class ExportDialogIntegrationTests
{
    private static FileOperationsViewModel CreateFileOperationsVm(
        out VerilogAExportViewModel verilogAVm,
        out PhotonTorchExportViewModel photonTorchVm)
    {
        var canvas = new DesignCanvasViewModel();
        var commandManager = new CommandManager();
        var nazcaExporter = new SimpleNazcaExporter();
        var picWaveExporter = new PicWaveExporter();
        var componentLibrary = new ObservableCollection<ComponentTemplate>();
        var gdsVm = new GdsExportViewModel(new GdsExportService());
        var photonTorchExporter = new PhotonTorchExporter();

        photonTorchVm = new PhotonTorchExportViewModel(photonTorchExporter, canvas);
        verilogAVm = new VerilogAExportViewModel(new VerilogAExporter(), new VerilogAFileWriter(), canvas);

        return new FileOperationsViewModel(
            canvas,
            commandManager,
            nazcaExporter,
            picWaveExporter,
            componentLibrary,
            gdsVm,
            photonTorchVm,
            verilogAVm);
    }

    [Fact]
    public async Task ExportVerilogAWithDialog_WhenDialogCancelled_SkipsExport()
    {
        // Arrange
        var vm = CreateFileOperationsVm(out var verilogAVm, out _);
        vm.ShowVerilogAExportDialogAsync = () => Task.FromResult(false);

        // Act
        await vm.ExportVerilogAWithDialogCommand.ExecuteAsync(null);

        // Assert: StatusText stays empty because ExportAsync was never called
        verilogAVm.StatusText.ShouldBeEmpty();
    }

    [Fact]
    public async Task ExportVerilogAWithDialog_WhenDialogConfirmed_CallsExport()
    {
        // Arrange
        var vm = CreateFileOperationsVm(out var verilogAVm, out _);
        vm.ShowVerilogAExportDialogAsync = () => Task.FromResult(true);

        // Act
        await vm.ExportVerilogAWithDialogCommand.ExecuteAsync(null);

        // Assert: ExportAsync ran — it set StatusText to "No components to export."
        // because the canvas is empty (no components added in this test).
        verilogAVm.StatusText.ShouldBe("No components to export.");
    }

    [Fact]
    public async Task ExportVerilogAWithDialog_WhenNoDelegateWired_FallsBackToDirectExport()
    {
        // Arrange: no dialog delegate set
        var vm = CreateFileOperationsVm(out var verilogAVm, out _);

        // Act
        await vm.ExportVerilogAWithDialogCommand.ExecuteAsync(null);

        // Assert: export ran directly (no dialog blocking it)
        verilogAVm.StatusText.ShouldBe("No components to export.");
    }

    [Fact]
    public async Task ExportPhotonTorchWithDialog_WhenDialogCancelled_SkipsExport()
    {
        // Arrange
        var vm = CreateFileOperationsVm(out _, out var photonTorchVm);
        vm.ShowPhotonTorchExportDialogAsync = () => Task.FromResult(false);

        // Act
        await vm.ExportPhotonTorchWithDialogCommand.ExecuteAsync(null);

        // Assert: LastExportStatus stays empty — ExportAsync was never called
        photonTorchVm.LastExportStatus.ShouldBeEmpty();
    }

    [Fact]
    public async Task ExportPhotonTorchWithDialog_WhenDialogConfirmed_CallsExport()
    {
        // Arrange: wire dialog to confirm, no FileDialogService (so export returns early
        // with "Export not available" — still proves ExportAsync was called).
        var vm = CreateFileOperationsVm(out _, out var photonTorchVm);
        vm.ShowPhotonTorchExportDialogAsync = () => Task.FromResult(true);
        // photonTorchVm.FileDialogService is null → ExportAsync returns "Export not available"

        // Act
        await vm.ExportPhotonTorchWithDialogCommand.ExecuteAsync(null);

        // Assert: ExportAsync ran and set LastExportStatus
        photonTorchVm.LastExportStatus.ShouldBe("Export not available");
    }

    [Fact]
    public async Task ExportPhotonTorchWithDialog_WhenNoDelegateWired_FallsBackToDirectExport()
    {
        // Arrange: no dialog delegate, no file dialog service
        var vm = CreateFileOperationsVm(out _, out var photonTorchVm);

        // Act
        await vm.ExportPhotonTorchWithDialogCommand.ExecuteAsync(null);

        // Assert: export ran directly, LastExportStatus reflects it
        photonTorchVm.LastExportStatus.ShouldBe("Export not available");
    }
}
