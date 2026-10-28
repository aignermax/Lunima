using System.Collections.Generic;

namespace Lunima.ArchitectureAnalyzers;

/// <summary>Reviewed package assembly identities. Registration declares ownership, never universal permission.</summary>
internal static class RepositoryExternalAssemblies
{
    internal static IEnumerable<KeyValuePair<string, string>> Entries()
    {
        yield return new("Avalonia", RepositoryModules.UiFramework);
        yield return new("Avalonia.Base", RepositoryModules.UiFramework);
        yield return new("Avalonia.Browser", RepositoryModules.UiFramework);
        yield return new("Avalonia.Controls", RepositoryModules.UiFramework);
        yield return new("Avalonia.DesignerSupport", RepositoryModules.UiFramework);
        yield return new("Avalonia.Diagnostics", RepositoryModules.UiFramework);
        yield return new("Avalonia.Desktop", RepositoryModules.UiFramework);
        yield return new("Avalonia.Dialogs", RepositoryModules.UiFramework);
        yield return new("Avalonia.Fonts.Inter", RepositoryModules.UiFramework);
        yield return new("Avalonia.FreeDesktop", RepositoryModules.UiFramework);
        yield return new("Avalonia.Markup", RepositoryModules.UiFramework);
        yield return new("Avalonia.Markup.Xaml", RepositoryModules.UiFramework);
        yield return new("Avalonia.Metal", RepositoryModules.UiFramework);
        yield return new("Avalonia.MicroCom", RepositoryModules.UiFramework);
        yield return new("Avalonia.Native", RepositoryModules.UiFramework);
        yield return new("Avalonia.OpenGL", RepositoryModules.UiFramework);
        yield return new("Avalonia.Remote.Protocol", RepositoryModules.UiFramework);
        yield return new("Avalonia.Skia", RepositoryModules.UiFramework);
        yield return new("Avalonia.Themes.Fluent", RepositoryModules.UiFramework);
        yield return new("Avalonia.Vulkan", RepositoryModules.UiFramework);
        yield return new("Avalonia.Win32", RepositoryModules.UiFramework);
        yield return new("Avalonia.Win32.Automation", RepositoryModules.UiFramework);
        yield return new("Avalonia.X11", RepositoryModules.UiFramework);
        yield return new("AvaloniaEdit", RepositoryModules.UiFramework);
        yield return new("AvaloniaEdit.TextMate", RepositoryModules.UiFramework);
        yield return new("CommunityToolkit.Mvvm", RepositoryModules.Mvvm);
        yield return new("ExtendedNumerics.BigDecimal", RepositoryModules.Numerics);
        yield return new("HarfBuzzSharp", RepositoryModules.UiFramework);
        yield return new("MathNet.Numerics", RepositoryModules.Numerics);
        yield return new("MicroCom.Runtime", RepositoryModules.UiFramework);
        yield return new("Microsoft.Extensions.DependencyInjection", RepositoryModules.DependencyInjection);
        yield return new("Microsoft.Extensions.DependencyInjection.Abstractions", RepositoryModules.DependencyInjection);
        yield return new("Microsoft.Extensions.Logging.Abstractions", RepositoryModules.Logging);
        yield return new("NCalc.Core", RepositoryModules.Expressions);
        yield return new("NCalc.Domain", RepositoryModules.Expressions);
        yield return new("NCalc.Parser", RepositoryModules.Expressions);
        yield return new("NCalc.Sync", RepositoryModules.Expressions);
        yield return new("Onigwrap", RepositoryModules.UiFramework);
        yield return new("OxyPlot", RepositoryModules.UiFramework);
        yield return new("OxyPlot.Avalonia", RepositoryModules.UiFramework);
        yield return new("Parlot", RepositoryModules.Expressions);
        yield return new("SkiaSharp", RepositoryModules.UiFramework);
        yield return new("System.Linq.Dynamic.Core", RepositoryModules.Expressions);
        yield return new("TextMateSharp", RepositoryModules.UiFramework);
        yield return new("TextMateSharp.Grammars", RepositoryModules.UiFramework);
        yield return new("Tmds.DBus.Protocol", RepositoryModules.UiFramework);
        yield return new("YamlDotNet", RepositoryModules.Serialization);
    }
}
