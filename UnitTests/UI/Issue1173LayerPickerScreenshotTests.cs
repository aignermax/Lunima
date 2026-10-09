using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services.GdsImport;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.GdsImport;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.Views.Dialogs;
using CAP_DataAccess.Import.Gds.LayerPicker;
using Shouldly;
using UnitTests.Helpers;
using UnitTests.Import.Gds;
using UnitTests.Services.GdsImport;
using Xunit;

namespace UnitTests.UI;

/// <summary>
/// Visual walkthrough for issue #1173 (click-to-assign layer types on GDS
/// import): the picker entry button in the import dialog, the picker window
/// before/after clicking geometry, and the layer fields after an assignment.
/// Writes PNGs + manifest.json to <c>artifacts/ui-screenshots/issue-1173/</c>.
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class Issue1173LayerPickerScreenshotTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "lunima-1173-shot-" + Guid.NewGuid().ToString("N"));
    private readonly GdsDesignScopeTestHost _host = new();

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
        _host.Dispose();
    }

    /// <summary>Foundry-style file: waveguide boundary + port labels in a child
    /// cell, an optical route path on (37,0) and a metal pad on (12,0).</summary>
    private static byte[] FoundryStyleLibrary() => GdsTestWriter.Create()
        .StandardPrologue()
        .BeginCell("TOP")
            .SRef("wg", 0, 0)
            .Path(37, 0, 500, 0, (0, 2000), (50000, 2000))
            .Boundary(12, 0, (20000, 8000), (30000, 8000), (30000, 14000), (20000, 14000), (20000, 8000))
        .EndCell()
        .BeginCell("wg")
            .Boundary(1, 0, (0, 1750), (10000, 1750), (10000, 2250), (0, 2250), (0, 1750))
            .Text(56, 0, "opt_in", 0, 2000)
            .Text(56, 0, "opt_out", 10000, 2000)
        .EndCell()
        .EndLibrary()
        .ToArray();

    [AvaloniaFact]
    public async Task CaptureLayerPickerWalkthrough()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("UI_SHOT_DIR")))
            return; // opt-in: heavy headless render, only on explicit request (see UiScreenshotTests)
        var outputDir = ResolveOutputDirectory();
        Directory.CreateDirectory(outputDir);

        Directory.CreateDirectory(_root);
        var gdsPath = Path.Combine(_root, "walkthrough.gds");
        File.WriteAllBytes(gdsPath, FoundryStyleLibrary());
        var executor = new GdsPlacementExecutor(
            new DesignCanvasViewModel(), new CommandManager(), () => new List<ComponentTemplate>());
        var vm = new GdsImportDialogViewModel(gdsPath, _host.CreateService(), executor);
        await vm.StartAnalysisAsync();
        vm.HasError.ShouldBeFalse(vm.ErrorText);

        // 01 — the import dialog's layer-assignment section with the new entry button.
        var dialog = new GdsImportDialog { DataContext = vm };
        dialog.Show();
        Dispatcher.UIThread.RunJobs();
        dialog.FindControl<Expander>("LayerAssignmentExpander").ShouldNotBeNull().IsExpanded = true;
        Dispatcher.UIThread.RunJobs();
        CaptureWindow(dialog, Path.Combine(outputDir, "01-import-dialog-picker-button.png"), close: false);

        var picker = vm.CreateLayerPicker().ShouldNotBeNull();
        var window = new GdsLayerPickerWindow { DataContext = picker };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // 02 — picker opens: color-coded geometry, legend, no selection yet.
        CaptureWindow(window, Path.Combine(outputDir, "02-picker-initial.png"), close: false);

        // 03 — user clicks the long route drawn on the unknown layer (37,0):
        // the layer highlights, the others dim, the type buttons enable.
        picker.PickAt(25, 2, 0.05);
        picker.SelectedRow.ShouldNotBeNull().Pair.ShouldBe(new GdsLayerPair(37, 0));
        Dispatcher.UIThread.RunJobs();
        CaptureWindow(window, Path.Combine(outputDir, "03-picker-layer-selected.png"), close: false);

        // 04 — user presses "Waveguide": the legend shows the assignment.
        picker.AssignWaveguideCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        CaptureWindow(window, Path.Combine(outputDir, "04-picker-assigned.png"), close: true);

        // 05 — back in the import dialog the waveguide field carries the pair.
        vm.WaveguideLayersText.ShouldContain("37,0");
        Dispatcher.UIThread.RunJobs();
        CaptureWindow(dialog, Path.Combine(outputDir, "05-dialog-fields-updated.png"), close: true);

        WriteManifest(outputDir);
    }

    private static void CaptureWindow(Window window, string path, bool close)
    {
        var bitmap = window.CaptureRenderedFrame();
        if (close)
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
        bitmap.ShouldNotBeNull($"render miss for {Path.GetFileName(path)}");
        using (bitmap)
            ScreenshotArtifacts.SavePng(bitmap!, path).Length.ShouldBeGreaterThan(0);
    }

    private static void WriteManifest(string outputDir)
    {
        const string manifest = """
        [
          {"file": "01-import-dialog-picker-button.png", "caption": "The import dialog's layer-assignment section gains an 'Assign layer types by clicking the geometry…' button above the number fields."},
          {"file": "02-picker-initial.png", "caption": "The picker opens with the top cell's geometry color-coded per layer, a legend below, and disabled type buttons until something is clicked."},
          {"file": "03-picker-layer-selected.png", "caption": "Clicking the long route on the unknown foundry layer selects (37,0): it highlights while the other layers dim, and the type buttons enable."},
          {"file": "04-picker-assigned.png", "caption": "Pressing 'Waveguide' assigns the selected layer; the legend row flips from '—' to 'waveguide'."},
          {"file": "05-dialog-fields-updated.png", "caption": "Back in the import dialog the waveguide layer field already carries 37,0 — no layer numbers were typed or guessed."}
        ]
        """;
        ScreenshotArtifacts.WriteText(Path.Combine(outputDir, "manifest.json"), manifest);
    }

    /// <summary>Repo-root <c>artifacts/ui-screenshots/issue-1173</c> (or <c>UI_SHOT_DIR/issue-1173</c>).</summary>
    private static string ResolveOutputDirectory()
    {
        var envDir = Environment.GetEnvironmentVariable("UI_SHOT_DIR");
        if (!string.IsNullOrEmpty(envDir))
            return Path.Combine(envDir, "issue-1173");

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (dir.GetFiles("*.sln").Length > 0)
                return Path.Combine(dir.FullName, "artifacts", "ui-screenshots", "issue-1173");
            dir = dir.Parent;
        }
        return Path.Combine(AppContext.BaseDirectory, "artifacts", "ui-screenshots", "issue-1173");
    }
}
