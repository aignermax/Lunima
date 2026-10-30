using Avalonia.Headless.XUnit;
using CAP.Avalonia.ViewModels.Analysis;
using CAP.Avalonia.ViewModels.Canvas;
using Shouldly;

namespace UnitTests.Analysis.Lifetime;

public sealed class LayoutCompressionOwnershipTests
{
    [AvaloniaFact]
    public async Task ReconfigureDuringPreparation_DoesNotCompressTheReplacementCanvas()
    {
        var first = Scene(500);
        var replacement = Scene(900);
        var panel = new CompressLayoutViewModel();
        panel.Configure(first);
        var changed = false;
        panel.PropertyChanged += (_, change) =>
        {
            if (changed || change.PropertyName != nameof(panel.StatusText) || !panel.IsCompressing) return;
            changed = true;
            panel.Configure(replacement);
        };
        try
        {
            await panel.CompressLayoutCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(10));
            changed.ShouldBeTrue();
            replacement.Components[0].Component.PhysicalX.ShouldBe(900);
            panel.ResultText.ShouldBeEmpty();
        }
        finally
        {
            await first.Document.RetireAsync();
            first.DisposeDocumentBindings();
            await replacement.Document.RetireAsync();
            replacement.DisposeDocumentBindings();
        }
    }

    private static DesignCanvasViewModel Scene(double x)
    {
        var canvas = new DesignCanvasViewModel();
        var component = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        component.PhysicalX = x;
        component.PhysicalY = 300;
        canvas.Components.Add(new ComponentViewModel(component));
        return canvas;
    }
}
