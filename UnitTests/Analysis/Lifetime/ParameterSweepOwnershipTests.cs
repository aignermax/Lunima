using Avalonia.Headless.XUnit;
using CAP.Avalonia.ViewModels.Analysis;
using CAP.Avalonia.ViewModels.Canvas;
using Shouldly;

namespace UnitTests.Analysis.Lifetime;

public sealed class ParameterSweepOwnershipTests
{
    [AvaloniaFact]
    public async Task Sweep_DoesNotWriteTrialValuesIntoTheLiveDocument()
    {
        var canvas = new DesignCanvasViewModel();
        var component = TestComponentHelper.CreateComponentWithSlider(initialValue: 0.75);
        var target = new ComponentViewModel(component);
        canvas.Components.Add(target);
        await using var panel = new ParameterSweepViewModel();
        panel.ConfigureForComponent(target, canvas);
        panel.StepCount = 3;
        component.GetSlider(0)!.Value = 0.75;
        var observed = new List<double>();
        component.GetSlider(0)!.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == "Value") observed.Add(component.GetSlider(0)!.Value);
        };
        try
        {
            await panel.RunSweepCommand.ExecuteAsync(null);
            observed.ShouldBeEmpty("trial settings belong to the private calculation, not the user's component");
            component.GetSlider(0)!.Value.ShouldBe(0.75);
        }
        finally
        {
            await canvas.Document.RetireAsync();
            canvas.DisposeDocumentBindings();
        }
    }

    [AvaloniaFact]
    public async Task ReconfigureDuringPreparation_DoesNotPublishTheOldSweepIntoTheNewPanel()
    {
        var canvas = new DesignCanvasViewModel();
        var target = new ComponentViewModel(TestComponentHelper.CreateComponentWithSlider());
        canvas.Components.Add(target);
        await using var panel = new ParameterSweepViewModel();
        panel.ConfigureForComponent(target, canvas);
        panel.StepCount = 3;
        var changed = false;
        panel.PropertyChanged += (_, change) =>
        {
            if (!changed && change.PropertyName == nameof(panel.StatusText) && panel.IsSweeping)
            {
                changed = true;
                panel.ConfigureForComponent(null, canvas);
            }
        };
        try
        {
            await panel.RunSweepCommand.ExecuteAsync(null);
            changed.ShouldBeTrue();
            panel.ResultText.ShouldBeEmpty();
            panel.StatusText.ShouldBeEmpty("the new selection owns its empty state");
            panel.IsSweeping.ShouldBeFalse();
        }
        finally
        {
            await canvas.Document.RetireAsync();
            canvas.DisposeDocumentBindings();
        }
    }
}
