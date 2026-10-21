using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CAP.Avalonia.Controls;
using CAP_Core.Components.Core;
using Shouldly;
using UnitTests.UI.Showcase;
using Xunit;

namespace UnitTests.UI;

/// <summary>Real key routing preserves text editing while enabling canvas selection.</summary>
[Collection("LocalizationSingleton")]
public class SelectAllInputTests
{
    [AvaloniaFact]
    public void ControlA_SelectsCanvasButDoesNotStealTextSelection()
    {
        var vm = ShowcaseCircuit.CreateStagedViewModel();
        vm.Canvas.AddComponent(new ComponentGroup { Identifier = "First" });
        vm.Canvas.AddComponent(new ComponentGroup { Identifier = "Second" });
        var window = ShowcaseCircuit.BootMainWindow(vm);
        try
        {
            var canvas = window.GetVisualDescendants().OfType<DesignCanvas>().Single();
            canvas.Focus();
            window.KeyPress(Key.A, RawInputModifiers.Control);
            window.KeyRelease(Key.A, RawInputModifiers.Control);
            vm.Canvas.Selection.SelectedComponents.Count.ShouldBe(2);

            vm.Canvas.SelectedComponent = null;
            vm.Canvas.Selection.ClearSelection();
            var input = new TextBox { Text = "Search components" };
            window.Content = input;
            Dispatcher.UIThread.RunJobs();
            input.Focus();
            window.KeyPress(Key.A, RawInputModifiers.Control);
            window.KeyRelease(Key.A, RawInputModifiers.Control);
            input.SelectedText.ShouldBe("Search components");
            vm.Canvas.Selection.SelectedComponents.ShouldBeEmpty();
        }
        finally { window.Close(); }
    }
}
