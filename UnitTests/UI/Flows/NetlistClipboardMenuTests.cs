using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Export.Formats;
using Shouldly;

namespace UnitTests.UI.Flows;

/// <summary>Clipboard export closes its real menu only after the asynchronous copy succeeds.</summary>
[Collection("LocalizationSingleton")]
[Trait("Category", "Slow")]
public class NetlistClipboardMenuTests
{
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ClipboardEntry_WaitsForCopy_AndClosesOnlyOnSuccess(bool succeeds)
    {
        using var host = new UiFlowTestHost();
        host.Vm.Canvas.AddComponent(TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins(), "Waveguide");
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int copies = 0;
        host.Vm.RightPanel.Netlist.CopyToClipboard = text =>
        {
            text.ShouldContain("instances:");
            copies++;
            return completion.Task;
        };
        var open = UiInput.Descendants<Button>(host.Window).Single(b =>
            Equals(ToolTip.GetTip(b), LocalizationService.Instance.Translate("Toolbar.ExportTip")));
        UiInput.Click(host.Window, open);
        var flyout = (Flyout)open.Flyout!;
        var content = (Control)flyout.Content!;
        var button = content.GetLogicalDescendants().OfType<Button>()
            .Single(b => b.DataContext is NetlistClipboardExportFormat);
        var entry = (NetlistClipboardExportFormat)button.DataContext!;
        button.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        UiInput.RunJobs();
        copies.ShouldBe(1);
        flyout.IsOpen.ShouldBeTrue("copy is still pending");
        if (succeeds)
            completion.SetResult();
        else
            completion.SetException(new InvalidOperationException("clipboard refused"));
        await entry.ExportCommand.ExecutionTask!;
        UiInput.RunJobs();
        copies.ShouldBe(1);
        flyout.IsOpen.ShouldBe(!succeeds);
        if (succeeds)
            host.Vm.StatusText.ShouldBe(LocalizationService.Instance.Translate("Netlist.Copied"));
        else
        {
            host.Vm.StatusText.ShouldContain("clipboard refused");
            host.Vm.StatusText.ShouldNotBe(LocalizationService.Instance.Translate("Netlist.Copied"));
        }
        if (Environment.GetEnvironmentVariable("UI_SHOT_DIR") is { Length: > 0 } directory)
        {
            var output = Path.Combine(directory, "netlist-clipboard");
            Directory.CreateDirectory(output);
            ScreenshotArtifacts.SettleAnimations();
            using var frame = host.Window.CaptureRenderedFrame();
            frame.ShouldNotBeNull();
            ScreenshotArtifacts.SavePng(frame!, Path.Combine(output, succeeds ? "copied-menu-closed.png" : "failed-menu-open.png"));
        }
    }
}
