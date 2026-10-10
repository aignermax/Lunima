using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Threading;
using CAP.Avalonia.Controls;
using Shouldly;

namespace UnitTests.UI.DesignSystem;

/// <summary>
/// Checks the generated icon set and renders a gallery of the design system (type scale,
/// button variants, chips, icons) so the PR shows what views build from.
/// </summary>
public class DesignSystemGalleryTests
{
    /// <summary>Lucide icons are authored in a 24 unit box; anything outside means the SVG
    /// converter mis-translated a relative path command.</summary>
    private const double IconBoxTolerance = 0.6;

    /// <summary>ControlHeight in Styles/Tokens.axaml.</summary>
    private const double UniformControlHeight = 28;

    [AvaloniaFact]
    public void EveryIcon_IsNonEmpty_AndFitsTheIconBox()
    {
        var icons = LoadIcons();
        icons.Count.ShouldBeGreaterThan(100);

        foreach (var (key, geometry) in icons)
        {
            var bounds = geometry.Bounds;
            (bounds.Width > 0 || bounds.Height > 0).ShouldBeTrue($"{key} is empty");
            bounds.Left.ShouldBeGreaterThanOrEqualTo(-IconBoxTolerance, key);
            bounds.Top.ShouldBeGreaterThanOrEqualTo(-IconBoxTolerance, key);
            bounds.Right.ShouldBeLessThanOrEqualTo(24 + IconBoxTolerance, key);
            bounds.Bottom.ShouldBeLessThanOrEqualTo(24 + IconBoxTolerance, key);
        }
    }

    [AvaloniaFact]
    public void Buttons_Inputs_AndComboBoxes_ShareOneRowHeight()
    {
        var controls = new Control[]
        {
            Button("Primary", "primary"), Button("Secondary", null), Button("Ghost", "ghost"),
            new TextBox { Width = 120, Text = "text" }, new ComboBox { Width = 100, ItemsSource = new[] { "CW" }, SelectedIndex = 0 },
            new NumericUpDown { Width = 100, Value = 1 },
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Top };
        foreach (var control in controls)
        {
            control.VerticalAlignment = VerticalAlignment.Top;
            row.Children.Add(control);
        }
        var window = new Window { Width = 800, Height = 100, Content = row };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        foreach (var control in controls)
            control.Bounds.Height.ShouldBe(UniformControlHeight, 0.5, control.GetType().Name);
        window.Close();
    }

    [Trait("Category", "UiScreenshots")]
    [AvaloniaFact]
    public void CaptureDesignSystemGallery()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("UI_SHOT_DIR")))
            return;
        var outputDir = Path.Combine(Environment.GetEnvironmentVariable("UI_SHOT_DIR")!, "ui-redesign");
        Directory.CreateDirectory(outputDir);

        var window = new Window { Width = 1100, Height = 760, Content = BuildGallery() };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        using (var bitmap = window.CaptureRenderedFrame())
        {
            bitmap.ShouldNotBeNull();
            ScreenshotArtifacts.SavePng(bitmap!, Path.Combine(outputDir, "00-design-system.png"));
        }
        window.Close();
    }

    private static Control BuildGallery()
    {
        var root = new StackPanel { Margin = new Thickness(32), Spacing = 24 };
        root.Children.Add(TypeScale());
        root.Children.Add(Buttons());
        root.Children.Add(Chips());
        root.Children.Add(IconGrid());
        return new ScrollViewer { Content = root };
    }

    private static Control TypeScale()
    {
        var panel = new StackPanel { Spacing = 4 };
        foreach (var (cls, text) in new[]
        {
            ("display", "Lunima"), ("headline", "Photonic IC Design"), ("title", "Design checks"),
            ("subtitle", "Waveguide routing"), ("body", "Body text for descriptions and values."),
            ("caption", "Caption: secondary information"), ("label", "SECTION LABEL"),
        })
        {
            var block = new TextBlock { Text = text };
            block.Classes.Add(cls);
            panel.Children.Add(block);
        }
        return panel;
    }

    private static Control Buttons()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(Button("Run simulation", "primary"));
        row.Children.Add(Button("Export GDS", null));
        row.Children.Add(Button("Cancel", "ghost"));
        row.Children.Add(Button("Delete", "danger"));
        row.Children.Add(new TextBox { Width = 160, Watermark = "Search components…" });
        row.Children.Add(new ComboBox { Width = 120, ItemsSource = new[] { "CW", "CCW" }, SelectedIndex = 0 });
        row.Children.Add(new CheckBox { Content = "Snap to grid", IsChecked = true });
        foreach (var (icon, isChecked) in new[] { ("Icon.MousePointer2", true), ("Icon.Spline", false), ("Icon.Scissors", false) })
            row.Children.Add(new ToggleButton { Classes = { "toolbar" }, IsChecked = isChecked, Content = IconOf(icon) });
        return row;
    }

    private static Button Button(string text, string? cls)
    {
        var button = new Button { Content = text };
        if (cls != null)
            button.Classes.Add(cls);
        return button;
    }

    private static Control Chips()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var (cls, text) in new[] { ("success", "Routed"), ("warning", "2 DRC hints"), ("danger", "3 blocked"), ("info", "Simulating"), ("accent", "Selected") })
        {
            var chip = new Border { Child = new TextBlock { Text = text } };
            chip.Classes.Add("chip");
            chip.Classes.Add(cls);
            row.Children.Add(chip);
        }
        return row;
    }

    private static Control IconGrid()
    {
        var grid = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var (key, _) in LoadIcons())
        {
            var cell = new Border { Width = 40, Height = 40, Child = IconOf(key) };
            ToolTip.SetTip(cell, key);
            grid.Children.Add(cell);
        }
        return grid;
    }

    private static Icon IconOf(string key) => new()
    {
        Data = (Geometry)LoadIcons().First(i => i.Key == key).Geometry,
        Size = 18,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static List<(string Key, Geometry Geometry)> LoadIcons()
    {
        var include = new ResourceInclude(new Uri("avares://CAP.Avalonia/"))
        {
            Source = new Uri("avares://CAP.Avalonia/Styles/Icons.axaml"),
        };
        var dictionary = (ResourceDictionary)include.Loaded;
        return dictionary.Keys.OfType<string>()
            .OrderBy(k => k, StringComparer.Ordinal)
            .Select(k => (k, (Geometry)dictionary[k]!))
            .ToList();
    }
}
