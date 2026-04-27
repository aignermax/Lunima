using System.IO;
using Shouldly;

namespace UnitTests.Architecture;

/// <summary>
/// Pins the DI-lifetime contract for export ViewModels. Both
/// <c>VerilogAExportViewModel</c> and <c>PhotonTorchExportViewModel</c> must
/// be registered as Singleton so that <c>FileOperations.{VerilogA,PhotonTorch}Export</c>
/// and the dialog DataContext (set in <c>Views.Dialogs.ExportDialogWiring.Wire</c>)
/// resolve to the same instance. If either is accidentally flipped to Transient,
/// the dialog edits state that the export command never sees — the user changes
/// wavelength in the dialog, hits Export, and the underlying export VM still has
/// the old value. This test catches that regression by scanning the App DI
/// registration directly.
/// </summary>
public class ExportViewModelLifetimeTests
{
    private static readonly string AppDiFile =
        Path.Combine(FindRepoRoot(), "CAP.Avalonia", "App.axaml.cs");

    [Fact]
    public void VerilogAExportViewModel_IsRegisteredAsSingleton()
    {
        var content = File.ReadAllText(AppDiFile);

        content.ShouldContain("AddSingleton<VerilogAExportViewModel>");
        content.ShouldNotContain("AddTransient<VerilogAExportViewModel>");
    }

    [Fact]
    public void PhotonTorchExportViewModel_IsRegisteredAsSingleton()
    {
        var content = File.ReadAllText(AppDiFile);

        content.ShouldContain("AddSingleton<PhotonTorchExportViewModel>");
        content.ShouldNotContain("AddTransient<PhotonTorchExportViewModel>");
    }

    [Fact]
    public void GdsExportViewModel_IsRegisteredAsSingleton()
    {
        var content = File.ReadAllText(AppDiFile);

        content.ShouldContain("AddSingleton<GdsExportViewModel>");
        content.ShouldNotContain("AddTransient<GdsExportViewModel>");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "CAP.Avalonia", "App.axaml.cs")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("Could not locate repository root");
    }
}
