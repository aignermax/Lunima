using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Export;
using CAP.Avalonia.ViewModels.PdkResolution;
using CAP_Core.Components.ComponentHelpers;
using CAP_Core.Export;
using CAP_Core.Export.PdkResolution;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using UnitTests.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Shouldly;

namespace UnitTests.Architecture;

public sealed class ExportInfrastructureBoundaryTests
{
    [Fact]
    public async Task ExportCapabilities_ResolveTheConfiguredInfrastructureOwners()
    {
        var services = new ServiceCollection();
        CAP.Avalonia.App.ConfigureServices(services);
        var resolver = new PdkFunctionResolutionService("unused-python", "unused-script");
        var writer = new VerilogAFileWriter();
        services.AddSingleton(resolver);
        services.AddSingleton(writer);
        await using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IPdkFunctionResolver>().ShouldBeSameAs(resolver);
        provider.GetRequiredService<IVerilogAExportWriter>().ShouldBeSameAs(writer);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Resolution_UsesOrderedInterfaceRequests_AndWaitsBeforePublishingRows(bool succeeds)
    {
        var catalog = new Mock<IBundledPdkCatalog>();
        catalog.Setup(value => value.ReadInventory()).Returns(new BundledPdkInventory("virtual", ["virtual.json"]));
        var reader = new Mock<IPdkReader>();
        reader.Setup(value => value.LoadFromFileForEditing("virtual.json")).Returns(new PdkDraft
        {
            Name = "Mixed backends", Components =
            [
                new() { Name = "Nazca cell", NazcaFunction = "demo_pdk.straight" },
                new() { Name = "GF cell", GdsFactoryFunction = "cspdk.sin300.bend" }
            ]
        });
        var resolver = new HeldResolver();
        var vm = new PdkResolutionCheckViewModel(catalog.Object, reader.Object, resolver);
        var running = vm.RunCheckCommand.ExecuteAsync(null);
        try
        {
            await resolver.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            running.IsCompleted.ShouldBeFalse();
            vm.IsRunning.ShouldBeTrue();
            vm.Pdks.ShouldBeEmpty();
            resolver.Entries!.Select(value => (value.Name, value.Module, value.Function, value.Backend)).ShouldBe(new[]
            {
                ("Nazca cell", "demo", "straight", "nazca"),
                ("GF cell", "cspdk.sin300", "bend", "gdsfactory")
            });
            resolver.Finish.TrySetResult(succeeds ? new PdkResolutionReport
            {
                Success = true, Results =
                [
                    new() { Status = PdkResolutionStatus.Ok, Message = "callable" },
                    new() { Status = PdkResolutionStatus.Warning, Message = "static cell" }
                ]
            } : PdkResolutionReport.Fail("configured backend unavailable"));
            await running.WaitAsync(TimeSpan.FromSeconds(5));
            var group = vm.Pdks.ShouldHaveSingleItem();
            vm.IsRunning.ShouldBeFalse();
            vm.HasFailures.ShouldBeTrue();
            if (succeeds)
            {
                group.Rows.Select(value => value.ComponentName).ShouldBe(new[] { "Nazca cell", "GF cell" });
                group.Rows.Select(value => value.Status).ShouldBe(new[] { PdkResolutionStatus.Ok, PdkResolutionStatus.Warning });
                group.Rows.Select(value => value.Message).ShouldBe(new[] { "callable", "static cell" });
            }
            else
            {
                group.Error.ShouldBe("configured backend unavailable");
                group.Rows.ShouldBeEmpty();
            }
        }
        finally
        {
            resolver.Finish.TrySetResult(PdkResolutionReport.Fail("test cleanup"));
            await running.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task VerilogExport_UsesConfiguredWriter_AndPublishesOnlyAfterTheWriteCompletes(bool succeeds)
    {
        var root = Path.Combine(Path.GetTempPath(), $"lunima-writer-port-{Guid.NewGuid():N}");
        var canvas = new DesignCanvasViewModel();
        canvas.AddComponent(TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins(), "Waveguide");
        var writer = new HeldWriter();
        var launcher = new Mock<IUrlLauncher>(MockBehavior.Strict);
        var directory = Path.Combine(root, "MyCircuit");
        launcher.Setup(value => value.OpenFileOrDirectory(directory));
        var dialogs = new Mock<IFileDialogService>();
        dialogs.Setup(value => value.ShowSaveFileDialogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(Path.Combine(root, "MyCircuit.va"));
        var vm = new VerilogAExportViewModel(new VerilogAExporter(), writer, canvas, launcher.Object)
            { FileDialogService = dialogs.Object, WavelengthNm = 1310, IncludeTestBench = false };
        var running = vm.ExportAsync();
        try
        {
            await writer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            writer.Directory.ShouldBe(directory);
            writer.Result!.Success.ShouldBeTrue();
            writer.Result.CircuitName.ShouldBe("MyCircuit");
            writer.Result.ComponentFiles.ShouldNotBeEmpty();
            writer.Result.SpiceTestBench.ShouldBeNullOrEmpty();
            vm.IsExporting.ShouldBeTrue();
            vm.LastExportSucceeded.ShouldBeFalse();
            vm.LastOutputDirectory.ShouldBeEmpty();
            vm.LastFileCount.ShouldBe(0);
            launcher.Verify(value => value.OpenFileOrDirectory(It.IsAny<string>()), Times.Never);
            if (succeeds) writer.Finish.TrySetResult(null);
            else writer.Finish.TrySetException(new IOException("controlled write failure"));
            await running.WaitAsync(TimeSpan.FromSeconds(5));
            vm.IsExporting.ShouldBeFalse();
            vm.LastExportSucceeded.ShouldBe(succeeds);
            if (succeeds)
            {
                vm.LastFileCount.ShouldBe(writer.Result.TotalFileCount);
                vm.LastOutputDirectory.ShouldBe(directory);
                Directory.CreateDirectory(directory);
                vm.OpenOutputDirectory();
                launcher.Verify(value => value.OpenFileOrDirectory(directory), Times.Once);
            }
            else
            {
                vm.LastOutputDirectory.ShouldBeEmpty();
                vm.LastFileCount.ShouldBe(0);
                vm.StatusText.ShouldContain("controlled write failure");
            }
        }
        finally
        {
            writer.Finish.TrySetResult(null);
            await running.WaitAsync(TimeSpan.FromSeconds(5));
            canvas.DisposeDocumentBindings();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private sealed class HeldResolver : IPdkFunctionResolver
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<PdkResolutionReport> Finish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal IReadOnlyList<PdkResolutionEntry>? Entries { get; private set; }
        public Task<PdkResolutionReport> ResolveAsync(IReadOnlyList<PdkResolutionEntry> entries, CancellationToken cancellationToken = default)
        {
            Entries = entries;
            Entered.TrySetResult();
            return Finish.Task;
        }
    }

    private sealed class HeldWriter : IVerilogAExportWriter
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<string?> Finish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal VerilogAExportResult? Result { get; private set; }
        internal string? Directory { get; private set; }
        public Task<string?> WriteAsync(VerilogAExportResult result, string outputDirectory)
        {
            Result = result;
            Directory = outputDirectory;
            Entered.TrySetResult();
            return Finish.Task;
        }
    }
}
