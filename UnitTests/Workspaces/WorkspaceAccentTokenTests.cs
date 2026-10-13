using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using CAP.Avalonia.Behaviors;
using CAP.Avalonia.ViewModels.Workspaces;
using Shouldly;
using Xunit;

namespace UnitTests.Workspaces;

/// <summary>
/// The workspace hues in Styles/Tokens.axaml: every workspace defines every role in both the
/// dark and the light theme, Run's text stays readable on its hue (WCAG AA), the canvas tint
/// stays faint, and <see cref="WorkspaceAccentBehavior"/> publishes the active set.
/// </summary>
public class WorkspaceAccentTokenTests
{
    private const double MinimumTextContrast = 4.5;
    private const byte MaximumCanvasTintAlpha = 0x14;
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static Dictionary<string, Color> ThemeTokens(string theme)
    {
        var doc = XDocument.Load(Path.Combine(FindRepoRoot(), "CAP.Avalonia", "Styles", "Tokens.axaml"));
        var dictionary = doc.Descendants()
            .Single(e => e.Name.LocalName == "ResourceDictionary" && (string?)e.Attribute(Xaml + "Key") == theme);
        return dictionary.Elements()
            .Where(e => e.Name.LocalName == "SolidColorBrush")
            .ToDictionary(e => (string)e.Attribute(Xaml + "Key")!, e => Color.Parse((string)e.Attribute("Color")!));
    }

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void EveryWorkspace_DefinesEveryRole_InBothThemes(string theme)
    {
        var tokens = ThemeTokens(theme);
        foreach (var workspace in Enum.GetValues<Workspace>())
        foreach (var role in WorkspaceAccentBehavior.Roles)
            tokens.ShouldContainKey(WorkspaceAccentBehavior.TokenKey(workspace, role), $"{theme}: {workspace}.{role}");
        tokens.ShouldContainKey("Brush.Workspace.KeyHint");
    }

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void RunText_IsReadable_AndCanvasTintStaysFaint(string theme)
    {
        var tokens = ThemeTokens(theme);
        foreach (var workspace in Enum.GetValues<Workspace>())
        {
            var accent = tokens[WorkspaceAccentBehavior.TokenKey(workspace, "Accent")];
            var onAccent = tokens[WorkspaceAccentBehavior.TokenKey(workspace, "OnAccent")];
            Contrast(accent, onAccent).ShouldBeGreaterThanOrEqualTo(MinimumTextContrast, $"{theme} {workspace} Run text");
            tokens[WorkspaceAccentBehavior.TokenKey(workspace, "CanvasTint")].A
                .ShouldBeLessThanOrEqualTo(MaximumCanvasTintAlpha, $"{theme} {workspace} tint must not wash out waveguides");
        }
    }

    [Fact]
    public void WorkspaceHues_AreDistinct()
    {
        var accents = Enum.GetValues<Workspace>()
            .Select(w => ThemeTokens("Dark")[WorkspaceAccentBehavior.TokenKey(w, "Accent")])
            .ToList();
        accents.Distinct().Count().ShouldBe(accents.Count);
    }

    [AvaloniaFact]
    public void Behavior_PublishesTheActiveWorkspaceHue_ForTheThemeVariant()
    {
        var host = new ThemeVariantScope { RequestedThemeVariant = ThemeVariant.Dark };
        var root = new Avalonia.Controls.Grid();
        host.Child = root;
        var window = new Window { Content = host };
        window.Show();

        WorkspaceAccentBehavior.SetWorkspace(root, Workspace.Optics);
        CurrentAccent(root).ShouldBe(ThemeTokens("Dark")["Brush.Workspace.Optics.Accent"]);

        WorkspaceAccentBehavior.SetWorkspace(root, Workspace.Logic);
        CurrentAccent(root).ShouldBe(ThemeTokens("Dark")["Brush.Workspace.Logic.Accent"]);

        host.RequestedThemeVariant = ThemeVariant.Light;
        CurrentAccent(root).ShouldBe(ThemeTokens("Light")["Brush.Workspace.Logic.Accent"]);
        window.Close();
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData(" TRUE ", true)]
    [InlineData("0", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ReduceMotionVariable_IsParsedTheSameOnEveryOs(string? value, bool expected)
    {
        MotionPreferences.IsSet(value).ShouldBe(expected);
    }

    private static Color CurrentAccent(Control root) =>
        ((ISolidColorBrush)root.Resources[WorkspaceAccentBehavior.CurrentKey("Accent")]!).Color;

    private static double Contrast(Color a, Color b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(Color c)
    {
        static double Channel(byte v)
        {
            double s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "CAP.Avalonia", "App.axaml.cs")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not locate repository root");
    }
}
