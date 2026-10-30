using System.Reflection;
using System.Runtime.CompilerServices;
using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Diagnostics;
using CAP.Avalonia.ViewModels.Onboarding.FirstStepsTutorial;
using CAP.Avalonia.ViewModels.Panels;
using CAP.Avalonia.ViewModels.Analysis;
using CAP.Avalonia.ViewModels.Analysis.AnalysisOutput;
using CAP.Avalonia.ViewModels.Analysis.CircuitOptimization;
using CAP.Avalonia.ViewModels.Analysis.EyeDiagram;
using CAP.Avalonia.ViewModels.Analysis.MonteCarloAnalysis;
using CAP.Avalonia.ViewModels.Analysis.WavelengthSpectrum;
using UnitTests.Integration;

namespace UnitTests.Onboarding.FirstStepsTutorial;

public class ConnectChipletsReceiverLifecycleTests
{
    [Fact]
    public async Task SkippedTour_ReleasesTrackerWhileReceiverAndCanvasRemainAlive()
    {
        var canvas = new DesignCanvasViewModel();
        await TwoChipletsExampleLoader.LoadAsync(canvas, ConnectChipletsTourViewModel.ExampleFileName);
        var tracker = StartAndSkip(canvas);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(tracker.IsAlive, "Skip must remove the receiver PropertyChanged subscription, not just the tour's tracker event handler");
        GC.KeepAlive(canvas);
    }

    [Fact]
    public async Task RestartTracking_RecordsFreshBaselineAndObservesReceiverOnce()
    {
        var canvas = new DesignCanvasViewModel();
        await TwoChipletsExampleLoader.LoadAsync(canvas, ConnectChipletsTourViewModel.ExampleFileName);
        var tracker = new ConnectChipletsReceiverTracker(canvas, new CommandManager());
        tracker.ResetBaseline();
        var receiver = tracker.Receiver!;
        var notifications = 0;
        tracker.ReceiverPositionChanged += (_, _) => notifications++;
        tracker.StopTracking();
        receiver.X += 3;
        Assert.Equal(0, notifications);

        tracker.ResetBaseline();
        tracker.ResetBaseline();
        Assert.Equal(0, tracker.DisplacementFromStart());
        receiver.X += 2;
        Assert.Equal(1, notifications);
        Assert.Equal(2, tracker.DisplacementFromStart(), 6);
        tracker.StopTracking();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference StartAndSkip(DesignCanvasViewModel canvas)
    {
        var commands = new CommandManager();
        var dock = new AnalysisDockViewModel(new TimeDomainViewModel(), new EyeDiagramViewModel(),
            new WavelengthSpectrumViewModel(new CAP_Core.Analysis.MeasuredSpectrum.MeasuredSpectrumFileSource()), new AnalysisOutputPanelViewModel(),
            new MonteCarloViewModel(), new CircuitOptimizationViewModel(commands));
        var tour = new ConnectChipletsTourViewModel(canvas, new DesignValidationViewModel(), dock, commands);
        tour.Start();
        var field = typeof(ConnectChipletsTourViewModel).GetField("_receiverTracker", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var tracker = (ConnectChipletsReceiverTracker)field.GetValue(tour)!;
        Assert.NotNull(tracker.Receiver);
        var reference = new WeakReference(tracker);
        tour.Skip();
        Assert.Null(tour.CurrentCanvasPoint);
        Assert.Null(tour.CurrentCanvasDestinationPoint);
        return reference;
    }
}
