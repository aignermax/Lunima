using System.IO;
using System.Linq;
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
/// the old value. This test catches that regression by scanning all DI registration
/// files in the CAP.Avalonia project.
/// </summary>
public class ExportViewModelLifetimeTests
{
    /// <summary>
    /// Reads all DI registration files (App.axaml.cs and the DI extension folder)
    /// and concatenates their content for lifetime scanning.
    /// </summary>
    private static string AllDiRegistrations
    {
        get
        {
            var root = FindRepoRoot();
            var appDi = Path.Combine(root, "CAP.Avalonia", "App.axaml.cs");
            var diFolder = Path.Combine(root, "CAP.Avalonia", "DI");

            var parts = new System.Collections.Generic.List<string>();
            if (File.Exists(appDi))
                parts.Add(File.ReadAllText(appDi));

            if (Directory.Exists(diFolder))
            {
                foreach (var f in Directory.GetFiles(diFolder, "*.cs", SearchOption.TopDirectoryOnly))
                    parts.Add(File.ReadAllText(f));
            }

            return string.Concat(parts);
        }
    }

    [Fact]
    public void VerilogAExportViewModel_IsRegisteredAsSingleton()
    {
        var content = AllDiRegistrations;
        content.ShouldContain("AddSingleton<VerilogAExportViewModel>");
        content.ShouldNotContain("AddTransient<VerilogAExportViewModel>");
    }

    [Fact]
    public void PhotonTorchExportViewModel_IsRegisteredAsSingleton()
    {
        var content = AllDiRegistrations;
        content.ShouldContain("AddSingleton<PhotonTorchExportViewModel>");
        content.ShouldNotContain("AddTransient<PhotonTorchExportViewModel>");
    }

    [Fact]
    public void GdsExportViewModel_IsRegisteredAsSingleton()
    {
        var content = AllDiRegistrations;
        content.ShouldContain("AddSingleton<GdsExportViewModel>");
        content.ShouldNotContain("AddTransient<GdsExportViewModel>");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "CAP.Avalonia", "App.axaml.cs")))
            dir = dir.Parent;

        return dir?.FullName ?? throw new InvalidOperationException("Could not locate repository root");
    }
}
