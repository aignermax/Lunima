using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Connections;
using CAP_Core.Routing;
using Shouldly;
using UnitTests.Helpers;
using Xunit;

namespace UnitTests.UI.Flows;

/// <summary>
/// User story: restyle a selected connection in the properties panel, then press Ctrl+Z while
/// focus is still in the panel — on the style dropdown or in a text field with nothing of its
/// own to undo. The design edit must be undone, and the dropdown must show the restored style.
/// </summary>
[Trait("Category", "UiFlows")]
// Boots the real MainWindow through the input pipeline — too heavy for local default runs.
[Trait("Category", "Slow")]
[Collection("LocalizationSingleton")]
public class UiFlowConnectionUndoFocusTests
{
    [AvaloniaFact]
    public void CtrlZ_WithStyleDropdownFocused_UndoesAndRefreshesTheDropdown()
    {
        using var host = new UiFlowTestHost();
        var connection = AddSelectedConnection(host);
        var combo = StyleComboBox(host);
        combo.Focus();

        combo.SelectedItem = WaveguideType.Bend;
        UiInput.RunJobs();
        connection.Connection.Type.ShouldBe(WaveguideType.Bend);

        UiInput.PressKey(host.Window, Key.Z, RawInputModifiers.Control);

        connection.Connection.Type.ShouldBe(WaveguideType.Auto);
        host.Vm.BottomPanel.ConnectionRouting.SelectedStyle.ShouldBe(WaveguideType.Auto,
            "a stale dropdown entry makes re-picking that style a silent no-op");
        combo.SelectedItem = WaveguideType.Bend;
        UiInput.RunJobs();
        connection.Connection.Type.ShouldBe(WaveguideType.Bend, "re-picking the undone style applies it again");
    }

    [AvaloniaFact]
    public void CtrlZ_WithUntouchedTextFieldFocused_UndoesTheDesignEdit()
    {
        using var host = new UiFlowTestHost();
        var connection = AddSelectedConnection(host);
        host.Vm.BottomPanel.ConnectionRouting.SelectedStyle = WaveguideType.Cobra;
        var textBox = UiInput.Descendants<TextBox>(host.Window).First(t => t.IsEffectivelyVisible);
        textBox.Focus();
        UiInput.RunJobs();
        textBox.CanUndo.ShouldBeFalse();

        UiInput.PressKey(host.Window, Key.Z, RawInputModifiers.Control);

        connection.Connection.Type.ShouldBe(WaveguideType.Auto);
        UiInput.PressKey(host.Window, Key.Y, RawInputModifiers.Control);
        connection.Connection.Type.ShouldBe(WaveguideType.Cobra);
    }

    private static WaveguideConnectionViewModel AddSelectedConnection(UiFlowTestHost host)
    {
        var vm = host.Vm;
        var start = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        var end = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        end.PhysicalX = 400;
        vm.Canvas.AddComponent(start);
        vm.Canvas.AddComponent(end);
        var startPin = start.PhysicalPins.First(p => p.Name == "out");
        var endPin = end.PhysicalPins.First(p => p.Name == "in");
        var (sx, sy) = startPin.GetAbsolutePosition();
        var (ex, ey) = endPin.GetAbsolutePosition();
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(sx, sy, ex, ey, 0));
        var connection = vm.Canvas.ConnectPinsWithCachedRoute(startPin, endPin, path)!;
        connection.IsSelected = true;
        vm.CanvasInteraction.SelectedWaveguideConnection = connection;
        UiInput.RunJobs();
        return connection;
    }

    private static ComboBox StyleComboBox(UiFlowTestHost host) =>
        UiInput.Descendants<ComboBox>(host.Window)
            .Single(c => ReferenceEquals(c.ItemsSource, host.Vm.BottomPanel.ConnectionRouting.RoutingStyles));
}
