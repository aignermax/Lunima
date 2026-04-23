using CAP.Avalonia.ViewModels.Export;
using CAP.Avalonia.ViewModels.Export.Formats;
using CommunityToolkit.Mvvm.Input;
using Moq;
using Shouldly;

namespace UnitTests.Export;

/// <summary>
/// Tests for <see cref="ExportMenuViewModel"/> and <see cref="IExportFormat"/> adapters.
/// </summary>
public class ExportMenuViewModelTests
{
    /// <summary>Minimal stub for IExportFormat used in registry tests.</summary>
    private sealed class StubFormat : IExportFormat
    {
        public string Name { get; }
        public string Icon => "🔧";
        public string Description => "Stub format";
        public string Background => "#3d3d3d";
        public IAsyncRelayCommand ExportCommand { get; }

        public StubFormat(string name)
        {
            Name = name;
            ExportCommand = new AsyncRelayCommand(() => Task.CompletedTask);
        }
    }

    [Fact]
    public void Constructor_StoresFormatsInOrder()
    {
        var f1 = new StubFormat("Alpha");
        var f2 = new StubFormat("Beta");
        var f3 = new StubFormat("Gamma");

        var vm = new ExportMenuViewModel(new[] { f1, f2, f3 });

        vm.Formats.Count.ShouldBe(3);
        vm.Formats[0].Name.ShouldBe("Alpha");
        vm.Formats[1].Name.ShouldBe("Beta");
        vm.Formats[2].Name.ShouldBe("Gamma");
    }

    [Fact]
    public void Constructor_AcceptsEmptyCollection()
    {
        var vm = new ExportMenuViewModel(Array.Empty<IExportFormat>());
        vm.Formats.ShouldBeEmpty();
    }

    [Fact]
    public void NazcaExportFormat_HasCorrectMetadata()
    {
        var cmd = new AsyncRelayCommand(() => Task.CompletedTask);
        var format = new NazcaExportFormat(cmd);

        format.Name.ShouldBe("Nazca Python");
        format.Icon.ShouldNotBeNullOrEmpty();
        format.Description.ShouldNotBeNullOrEmpty();
        format.Background.ShouldNotBeNullOrEmpty();
        format.ExportCommand.ShouldNotBeNull();
    }

    [Fact]
    public void PicWaveExportFormat_HasCorrectMetadata()
    {
        var cmd = new AsyncRelayCommand(() => Task.CompletedTask);
        var format = new PicWaveExportFormat(cmd);

        format.Name.ShouldBe("PICWave");
        format.Icon.ShouldNotBeNullOrEmpty();
        format.Description.ShouldNotBeNullOrEmpty();
        format.ExportCommand.ShouldNotBeNull();
    }

    [Fact]
    public void PhotonTorchExportFormat_HasCorrectMetadata()
    {
        var vm = FormatAdapterTests.CreatePhonTorchExportVm();
        var format = new PhotonTorchExportFormat(vm);

        format.Name.ShouldBe("PhotonTorch");
        format.Icon.ShouldNotBeNullOrEmpty();
        format.Description.ShouldNotBeNullOrEmpty();
        format.ExportCommand.ShouldNotBeNull();
    }

    [Fact]
    public async Task PhotonTorchExportFormat_WithNoDialog_FallsBackToDirectExport()
    {
        var vm = FormatAdapterTests.CreatePhonTorchExportVm();
        var format = new PhotonTorchExportFormat(vm);
        // No ShowOptionsDialogAsync set — should not throw
        await format.ExportCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task PhotonTorchExportFormat_InvokesDialogCallback()
    {
        var vm = FormatAdapterTests.CreatePhonTorchExportVm();
        var format = new PhotonTorchExportFormat(vm);

        var dialogInvoked = false;
        format.ShowOptionsDialogAsync = () =>
        {
            dialogInvoked = true;
            return Task.CompletedTask;
        };

        await format.ExportCommand.ExecuteAsync(null);

        dialogInvoked.ShouldBeTrue();
    }

    [Fact]
    public async Task VerilogAExportFormat_InvokesDialogCallback()
    {
        var verilogAVm = FormatAdapterTests.CreateVerilogAExportVm();
        var format = new VerilogAExportFormat(verilogAVm);

        var dialogInvoked = false;
        format.ShowOptionsDialogAsync = () =>
        {
            dialogInvoked = true;
            return Task.CompletedTask;
        };

        await format.ExportCommand.ExecuteAsync(null);

        dialogInvoked.ShouldBeTrue();
    }

    [Fact]
    public void VerilogAExportFormat_ExposesOptionsViewModel()
    {
        var verilogAVm = FormatAdapterTests.CreateVerilogAExportVm();
        var format = new VerilogAExportFormat(verilogAVm);

        format.OptionsViewModel.ShouldBeSameAs(verilogAVm);
    }

    [Fact]
    public async Task GdsExportFormat_InvokesDialogCallback()
    {
        var gdsVm = FormatAdapterTests.CreateGdsExportVm();
        var format = new GdsExportFormat(gdsVm);

        var dialogInvoked = false;
        format.ShowOptionsDialogAsync = () =>
        {
            dialogInvoked = true;
            return Task.CompletedTask;
        };

        await format.ExportCommand.ExecuteAsync(null);

        dialogInvoked.ShouldBeTrue();
    }

    [Fact]
    public void ExportMenu_ContainsAllFiveFormats()
    {
        var cmd = new AsyncRelayCommand(() => Task.CompletedTask);
        var photonTorchVm = FormatAdapterTests.CreatePhonTorchExportVm();
        var gdsVm = FormatAdapterTests.CreateGdsExportVm();
        var verilogAVm = FormatAdapterTests.CreateVerilogAExportVm();

        var formats = new IExportFormat[]
        {
            new NazcaExportFormat(cmd),
            new PicWaveExportFormat(cmd),
            new PhotonTorchExportFormat(photonTorchVm),
            new GdsExportFormat(gdsVm),
            new VerilogAExportFormat(verilogAVm),
        };

        var menu = new ExportMenuViewModel(formats);

        menu.Formats.Count.ShouldBe(5);
        menu.Formats.Select(f => f.Name).ShouldContain("Nazca Python");
        menu.Formats.Select(f => f.Name).ShouldContain("PICWave");
        menu.Formats.Select(f => f.Name).ShouldContain("PhotonTorch");
        menu.Formats.Select(f => f.Name).ShouldContain("GDS Layout");
        menu.Formats.Select(f => f.Name).ShouldContain("Verilog-A / SPICE");
    }
}

/// <summary>
/// Factory helpers shared across format adapter tests.
/// </summary>
internal static class FormatAdapterTests
{
    internal static CAP.Avalonia.ViewModels.Export.PhotonTorchExportViewModel CreatePhonTorchExportVm()
    {
        var canvasMock = new Mock<CAP.Avalonia.ViewModels.Canvas.DesignCanvasViewModel>(MockBehavior.Loose);
        return new CAP.Avalonia.ViewModels.Export.PhotonTorchExportViewModel(
            new CAP_Core.Export.PhotonTorchExporter(),
            canvasMock.Object);
    }

    internal static CAP.Avalonia.ViewModels.Export.GdsExportViewModel CreateGdsExportVm()
    {
        var serviceMock = new Mock<CAP_Core.Export.GdsExportService>(MockBehavior.Loose);
        return new CAP.Avalonia.ViewModels.Export.GdsExportViewModel(serviceMock.Object);
    }

    internal static CAP.Avalonia.ViewModels.Export.VerilogAExportViewModel CreateVerilogAExportVm()
    {
        var exporterMock = new Mock<CAP_Core.Export.VerilogAExporter>(MockBehavior.Loose);
        var writerMock = new Mock<CAP.Avalonia.Services.VerilogAFileWriter>(MockBehavior.Loose);
        var canvasMock = new Mock<CAP.Avalonia.ViewModels.Canvas.DesignCanvasViewModel>(MockBehavior.Loose);
        return new CAP.Avalonia.ViewModels.Export.VerilogAExportViewModel(
            exporterMock.Object,
            writerMock.Object,
            canvasMock.Object);
    }
}
