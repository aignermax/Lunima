using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.Input;
using CAP.Avalonia.ViewModels.Analysis.LogicAnalysis.Waveform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CAP.Avalonia.Controls;
using CAP.Avalonia.Controls.Rendering;
using CAP.Avalonia.Views.Panels;
using Shouldly;
using UnitTests.Helpers;
using UnitTests.Integration;
using Xunit;

namespace UnitTests.UI;

public class LogicWaveformInteractionTests : IClassFixture<LogicGateFullAdderExampleTests.FullAdderFixture>
{
    private readonly LogicGateFullAdderExampleTests.FullAdderFixture _adder;
    public LogicWaveformInteractionTests(LogicGateFullAdderExampleTests.FullAdderFixture adder) => _adder = adder;

    [AvaloniaFact]
    public async Task HoverIdentifiesLane_AndClearsWhenLeaving()
    {
        var (window, strip, _) = await Open();
        try
        {
            var lane = strip.Waveform!.Lanes[1];
            window.MouseMove(Position(strip, 120, LogicWaveformRenderer.LaneBandTop(1) + 10));
            ToolTip.GetTip(strip).ShouldBe(lane.SignalName);
            strip.HoveredLaneIndex.ShouldBe(1);
            var folder = Path.Combine(AppContext.BaseDirectory, "artifacts", "waveform-interaction");
            Directory.CreateDirectory(folder);
            using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height));
            bitmap.Render(window);
            bitmap.Save(Path.Combine(folder, "hover.png"));
            window.MouseMove(new Point(window.Width - 2, window.Height - 2));
            ToolTip.GetTip(strip).ShouldBeNull();
            strip.HoveredLaneIndex.ShouldBe(-1);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(0.5)]
    [InlineData(1)]
    public async Task ClickSeeksNearestEvent_StopsPlayback_AndRepeatedClickKeepsSelection(double fraction)
    {
        var (window, strip, logic) = await Open();
        try
        {
            var model = strip.Waveform!;
            var time = model.StartTimePicoseconds + fraction * (model.EndTimePicoseconds - model.StartTimePicoseconds);
            var expected = logic.TimelineEvents.MinBy(row => Math.Abs(row.Event.TimePicoseconds - time));
            logic.TogglePlaybackCommand.Execute(null);
            logic.IsPlaying.ShouldBeTrue();
            Dispatcher.UIThread.RunJobs();
            strip.BringIntoView();
            Dispatcher.UIThread.RunJobs();
            var point = Position(strip, LogicWaveformRenderer.MapX(fraction, strip.Bounds.Width), 26);
            Click(window, point);
            logic.SelectedTimelineEvent.ShouldBeSameAs(expected);
            logic.IsPlaying.ShouldBeFalse();
            strip.Waveform!.CursorXFraction.ShouldNotBeNull();
            Click(window, point);
            logic.SelectedTimelineEvent.ShouldBeSameAs(expected);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task LabelAndHeaderClicksDoNotSeek()
    {
        var (window, strip, logic) = await Open();
        try
        {
            Click(window, Position(strip, 10, 26));
            Click(window, Position(strip, 120, 5));
            logic.SelectedTimelineEvent.ShouldBeNull();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void EmptyOrNarrowStripDoesNotSeek_AndClearingModelClearsHover()
    {
        var calls = 0;
        var strip = new LogicWaveformStrip
        {
            Waveform = new LogicWaveformModel(
                new[] { new LogicWaveformLane("C1", false, Array.Empty<LogicWaveformEdge>(), false) },
                Array.Empty<LogicWaveformClockDivider>(), null, 0, 10),
            SeekCommand = new RelayCommand<double>(_ => calls++),
            Width = 80,
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top,
        };
        var window = new Window { Width = 300, Height = 150, Content = strip };
        ToolTip.SetServiceEnabled(window, false);
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Click(window, new Point(70, 26));
            calls.ShouldBe(0);
            strip.HoveredLaneIndex.ShouldBe(0);
            strip.Waveform = null;
            strip.HoveredLaneIndex.ShouldBe(-1);
            ToolTip.GetTip(strip).ShouldBeNull();
            Click(window, new Point(70, 26));
            calls.ShouldBe(0);
        }
        finally { window.Close(); }
    }

    private async Task<(Window, LogicWaveformStrip, CAP.Avalonia.ViewModels.Analysis.LogicAnalysis.LogicPanelViewModel)> Open()
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        var logic = vm.RightPanel.Logic;
        logic.Configure(_adder.Canvas);
        await logic.BuildNetworkCommand.ExecuteAsync(null);
        logic.Inputs.Single(input => input.PinName == "A").IsOn = true;
        logic.HasWaveform.ShouldBeTrue();
        var panel = new LogicPanel { DataContext = vm };
        var window = new Window { Width = 1400, Height = 700, Content = panel };
        ToolTip.SetServiceEnabled(window, false);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var strip = panel.GetVisualDescendants().OfType<LogicWaveformStrip>().Single();
        strip.BringIntoView();
        Dispatcher.UIThread.RunJobs();
        return (window, strip, logic);
    }

    private static Point Position(Control control, double x, double y) =>
        control.TranslatePoint(new Point(x, y), TopLevel.GetTopLevel(control)!)!.Value;

    private static void Click(Window window, Point point)
    {
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }
}
