using System.IO;
using System.Linq;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using Moq;
using Shouldly;
using Xunit;

namespace UnitTests.ViewModels;

/// <summary>
/// Verifies the Fabrication Process ViewModel loads a process into editable
/// collections and imports one from a PDK folder via the file dialog.
/// </summary>
public class ProcessManagementViewModelTests
{
    [Fact]
    public void Load_PopulatesCollectionsAndSetsHasProcess()
    {
        var vm = new ProcessManagementViewModel(Mock.Of<IFileDialogService>());
        var process = new ProcessDefinition
        {
            Name = "P1",
            Layers = { new ProcessLayer { Name = "WG", Layer = 12 } },
            Xsections = { new ProcessXsection { Name = "E1700", WidthUm = 2 } },
        };

        vm.Load(process);

        vm.ProcessName.ShouldBe("P1");
        vm.HasProcess.ShouldBeTrue();
        vm.Layers.Count.ShouldBe(1);
        vm.Xsections.Single().Name.ShouldBe("E1700");
        vm.ToProcess().Layers.Single().Layer.ShouldBe(12);
    }

    [Fact]
    public async Task ImportFromPdk_ReadsCsvTablesFromSelectedFolder()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pmvm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "table_layers.csv"),
                "layer_name,layer,datatype,field,description\nWAVEGUIDE,12,0,Light,Passive WG\n");
            File.WriteAllText(Path.Combine(dir, "table_xsections.csv"),
                "origin,xsection,xsection_foundry,stub,description\nfab,E1700,E1700,E1700Stub,Optical\n");

            var dialog = new Mock<IFileDialogService>();
            dialog.Setup(d => d.ShowOpenFileDialogAsync(It.IsAny<string>(), It.IsAny<string>()))
                  .ReturnsAsync(Path.Combine(dir, "table_layers.csv"));

            var vm = new ProcessManagementViewModel(dialog.Object);
            await vm.ImportFromPdkCommand.ExecuteAsync(null);

            vm.HasProcess.ShouldBeTrue();
            vm.Layers.Single().Name.ShouldBe("WAVEGUIDE");
            vm.Xsections.Single().Name.ShouldBe("E1700");
            vm.StatusText.ShouldContain("Imported");
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task ImportFromPdk_Cancelled_LeavesProcessUnloaded()
    {
        var dialog = new Mock<IFileDialogService>();
        dialog.Setup(d => d.ShowOpenFileDialogAsync(It.IsAny<string>(), It.IsAny<string>()))
              .ReturnsAsync((string?)null);

        var vm = new ProcessManagementViewModel(dialog.Object);
        await vm.ImportFromPdkCommand.ExecuteAsync(null);

        vm.HasProcess.ShouldBeFalse();
    }
}
