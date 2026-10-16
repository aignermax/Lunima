using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.Views.Panels;
using CAP.Avalonia.Views.Panels.LogicTab;
using Shouldly;
using UnitTests.Helpers;
using UnitTests.Integration;
using Xunit;

namespace UnitTests.UI;

[Collection("LocalizationSingleton")]
public class LogicWarningSummaryTests : IClassFixture<LogicGateFullAdderExampleTests.FullAdderFixture>
{
    private readonly LogicGateFullAdderExampleTests.FullAdderFixture _adder;
    public LogicWarningSummaryTests(LogicGateFullAdderExampleTests.FullAdderFixture adder) => _adder = adder;

    [AvaloniaTheory]
    [InlineData("en", 500)]
    [InlineData("de", 500)]
    [InlineData("de", 220)]
    public async Task WarningSummaryStartsCompact_ActualClickRevealsEveryReport(string language, int height)
    {
        var previous = LocalizationService.Instance.ActiveLanguageCode;
        LocalizationService.Instance.SetLanguage(language);
        Window? window = null;
        try
        {
            var vm = MainViewModelTestHelper.CreateMainViewModel();
            var logic = vm.RightPanel.Logic;
            logic.Configure(_adder.Canvas);
            await logic.BuildNetworkCommand.ExecuteAsync(null);
            logic.HasFanOutWarnings.ShouldBeTrue();
            logic.Inputs.Single(input => input.PinName == "A").IsOn = true;
            var originalWarnings = logic.FanOutWarnings.ToArray();
            window = new Window { Width = 1400, Height = height, Content = new LogicPanel { DataContext = vm } };
            ToolTip.SetServiceEnabled(window, false);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var column = window.GetVisualDescendants().OfType<LogicFanOutColumn>().Single();
            var expander = column.GetVisualDescendants().OfType<Expander>().SingleOrDefault();
            expander.ShouldNotBeNull("long warning reports must be behind explicit details");
            expander.IsExpanded.ShouldBeFalse();
            column.Bounds.Height.ShouldBeLessThan(150);
            var count = column.FindControl<TextBlock>("WarningReportCount").ShouldNotBeNull();
            count.Text.ShouldBe(originalWarnings.Length.ToString());
            Save(window, language + "-" + height + "-collapsed");
            var header = expander.GetVisualDescendants().OfType<ToggleButton>().First();
            Click(window, header);
            await Task.Delay(250);
            Dispatcher.UIThread.RunJobs();
            expander.IsExpanded.ShouldBeTrue();
            foreach (var warning in originalWarnings)
            {
                column.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == warning.WarningText && text.IsEffectivelyVisible).ShouldBeTrue();
                foreach (var verdict in warning.VerdictLines)
                    column.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == verdict && text.IsEffectivelyVisible).ShouldBeTrue();
            }
            var scroll = expander.GetVisualDescendants().OfType<ScrollViewer>().Single();
            scroll.Offset = new Vector(0, scroll.Extent.Height);
            Dispatcher.UIThread.RunJobs();
            var note = column.GetVisualDescendants().OfType<TextBlock>()
                .Single(text => text.Text == LocalizationService.Instance.Translate("LogicPanel.FanOutWarning.PhysicsNote"));
            var noteBottom = note.TranslatePoint(new Point(0, note.Bounds.Height), window)!.Value.Y;
            noteBottom.ShouldBeLessThanOrEqualTo(window.Bounds.Height, "the final report line must be reachable even in a short dock");
            logic.FanOutWarnings.ShouldBe(originalWarnings);
            Save(window, language + "-" + height + "-expanded");
            Click(window, header);
            await Task.Delay(250);
            Dispatcher.UIThread.RunJobs();
            expander.IsExpanded.ShouldBeFalse();
            column.Bounds.Height.ShouldBeLessThan(150);
        }
        finally
        {
            window?.Close();
            LocalizationService.Instance.SetLanguage(previous);
        }
    }

    private static void Click(Window window, Control control)
    {
        Dispatcher.UIThread.RunJobs();
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Save(Window window, string name)
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "artifacts", "warning-summary");
        Directory.CreateDirectory(folder);
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height));
        bitmap.Render(window);
        bitmap.Save(Path.Combine(folder, name + ".png"));
    }
}
