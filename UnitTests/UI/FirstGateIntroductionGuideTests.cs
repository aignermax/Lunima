using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using CAP.Avalonia.Controls.TourAnchor;
using CAP.Avalonia.ViewModels.Analysis.LogicAnalysis;
using CAP.Avalonia.ViewModels.Analysis.LogicAnalysis.MakeGate;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Onboarding.FirstStepsTutorial;
using CAP.Avalonia.Views.Panels;
using UnitTests.UI.Flows;

namespace UnitTests.UI;

public class FirstGateIntroductionGuideTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Introduction_DemonstratesStartWithoutMovingCard_AndStopsOnClickOrPause(bool pause)
    {
        var tour = new FirstGateTourViewModel(new DesignCanvasViewModel(), new MakeGateViewModel(), new LogicPanelViewModel());
        var panel = new FirstGateTourPanel { DataContext = tour };
        var overlay = new TourAnchorOverlay { Card = panel, Demonstrate = true, DataContext = tour };
        overlay.Bind(TourAnchorOverlay.TargetNameProperty, new Binding(nameof(tour.CurrentTargetName)));
        overlay.Bind(Visual.IsVisibleProperty, new Binding(nameof(tour.IsActive)));
        var window = new Window { Width = 800, Height = 600, Content = overlay };
        try
        {
            tour.Start();
            window.Show();
            Settle();
            Assert.Equal("FirstGateStartButton", tour.CurrentTargetName);
            var button = panel.FindControl<Button>("FirstGateStartButton")!;
            var pointer = overlay.FindControl<Control>("GuidePointer")!;
            var host = overlay.FindControl<ContentControl>("CardHost")!;
            var root = overlay.FindControl<global::Avalonia.Controls.Canvas>("RootCanvas")!;
            Assert.True(pointer.IsVisible);
            Assert.True(pointer.ZIndex > host.ZIndex, "The guide must render above its card.");
            var position = host.Bounds;
            Settle();
            Assert.Equal(position, host.Bounds);
            var pointerTarget = (Point?)typeof(TourAnchorOverlay).GetField("_pointerTarget", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(overlay);
            Assert.Equal(button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), root), pointerTarget);
            if (pause) tour.Skip();
            else UiInput.Click(window, button);
            Settle();
            Assert.False(pointer.IsVisible);
            Assert.Equal(pause ? 0 : 1, tour.CurrentStepIndex);
        }
        finally { window.Close(); tour.Skip(); }

        void Settle()
        {
            for (var i = 0; i < 5; i++)
            {
                window.UpdateLayout();
                UiInput.RunJobs();
                typeof(TourAnchorOverlay).GetMethod("RefreshAnchor", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(overlay, null);
            }
        }
    }
}
