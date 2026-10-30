using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Shouldly;
using Xunit;

namespace UnitTests.Styles;

/// <summary>
/// Hover on buttons, list items and combo items is an immediate background swap: a brush
/// transition fades in from Transparent (#00FFFFFF) and flashes light grey.
/// </summary>
public class HoverTransitionTests
{
    private static void ShouldNotAnimate(Control root, string what)
    {
        var presenters = root.GetVisualDescendants().OfType<ContentPresenter>()
            .Where(p => p.Name == "PART_ContentPresenter").ToList();
        presenters.ShouldNotBeEmpty(what);
        foreach (var presenter in presenters)
            (presenter.Transitions?.Count ?? 0).ShouldBe(0, what);
    }

    /// <summary>Buttons, ListBoxItems and ComboBoxItems carry no brush transition.</summary>
    [AvaloniaFact]
    public void ButtonListAndComboItems_HaveNoHoverTransitions()
    {
        var button = new Button { Content = "x" };
        var list = new ListBox { ItemsSource = new[] { "a", "b" } };
        var combo = new ComboBox { ItemsSource = new[] { "a", "b" }, SelectedIndex = 0 };
        var window = new Window { Content = new StackPanel { Children = { button, list, combo } } };
        window.Show();
        window.UpdateLayout();

        ShouldNotAnimate(button, "Button");
        ShouldNotAnimate(list, "ListBoxItem");

        var item = new ComboBoxItem { Content = "c" };
        var host = new Window { Content = item };
        host.Show();
        host.UpdateLayout();
        ShouldNotAnimate(item, "ComboBoxItem");
        host.Close();
        window.Close();
    }
}
