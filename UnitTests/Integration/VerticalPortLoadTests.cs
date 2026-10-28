using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Export;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core;
using CAP_Core.Export;
using CAP_Core.Routing;
using Moq;
using Shouldly;
using Xunit;

namespace UnitTests.Integration;

public class VerticalPortLoadTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyFiberWaveguide_IsReportedWhileComponentsAndSimulationPortsLoad(bool withCachedPath)
    {
        var path = Path.Combine(Path.GetTempPath(), $"fiber-load-{Guid.NewGuid():N}.lun");
        try
        {
            var library = new ObservableCollection<ComponentTemplate>(TestPdkLoader.LoadAllTemplates());
            var (save, canvas, _) = Setup(library);
            var template = library.Single(t => t.Name == "Grating Coupler Elliptical");
            var first = ComponentTemplates.CreateFromTemplate(template, 0, 0);
            var second = ComponentTemplates.CreateFromTemplate(template, 200, 0);
            canvas.AddComponent(first, template.Name);
            canvas.AddComponent(second, template.Name);
            var connection = canvas.ConnectPins(first.PhysicalPins.Single(p => p.Name == "o1"), second.PhysicalPins.Single(p => p.Name == "o1"));
            if (withCachedPath)
            {
                var cached = new RoutedPath();
                cached.Segments.Add(new StraightSegment(0, 22.097, 200, 22.097, 0));
                connection!.Connection.RestoreCachedPath(cached);
            }
            var dialog = new Mock<IFileDialogService>();
            dialog.Setup(d => d.ShowSaveFileDialogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(path);
            save.FileDialogService = dialog.Object;
            await save.SaveDesignAsCommand.ExecuteAsync(null);
            var data = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
            var wire = data["Connections"]![0]!;
            wire["StartPinName"] = "o2";
            if (!withCachedPath) wire["CachedSegments"] = null;
            await File.WriteAllTextAsync(path, data.ToJsonString());

            var (load, loaded, errors) = Setup(library);
            string? status = null;
            load.UpdateStatus = value => status = value;
            (await load.LoadDesignFromPathAsync(path)).ShouldBeTrue();
            loaded.Components.Count.ShouldBe(2);
            loaded.Connections.ShouldBeEmpty();
            loaded.Components.Single(c => c.Component.Identifier == first.Identifier).Component
                .PhysicalPins.Single(p => p.Name == "o2").LogicalPin.ShouldNotBeNull();
            status.ShouldNotBeNull();
            status.ShouldContain("1");
            var warning = errors.Entries.ShouldHaveSingleItem().Message;
            warning.ShouldContain("vertical fiber");
            warning.ShouldContain($"{first.Identifier}.o2");
            warning.ShouldContain($"{second.Identifier}.o1");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static (FileOperationsViewModel, DesignCanvasViewModel, ErrorConsoleService) Setup(
        ObservableCollection<ComponentTemplate> library)
    {
        var canvas = new DesignCanvasViewModel();
        var errors = new ErrorConsoleService();
        var files = new FileOperationsViewModel(Moq.Mock.Of<CAP.Avalonia.Services.IUrlLauncher>(),new CAP_DataAccess.Persistence.NativeDesign.NativeDesignFileStore(),canvas, new CommandManager(), new SimpleNazcaExporter(),
            new SaxExporter(), library, new GdsExportViewModel(new GdsExportService(), new global::CAP_Core.Export.PythonDiscoveryService()),
            new PhotonTorchExportViewModel(Moq.Mock.Of<CAP.Avalonia.Services.IUrlLauncher>(),new PhotonTorchExporter(), canvas), null!, errorConsole: errors);
        return (files, canvas, errors);
    }
}
