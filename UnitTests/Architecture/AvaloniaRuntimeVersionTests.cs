using System.Xml.Linq;
using Shouldly;
using Xunit;

namespace UnitTests.Architecture;

/// <summary>The headless regression host must use the same UI runtime as every shipping host.</summary>
public class AvaloniaRuntimeVersionTests
{
    private static readonly HashSet<string> RuntimePackages = new(StringComparer.Ordinal)
    {
        "Avalonia", "Avalonia.Skia", "Avalonia.Themes.Fluent", "Avalonia.Fonts.Inter",
        "Avalonia.Desktop", "Avalonia.Diagnostics", "Avalonia.Browser", "Avalonia.Headless.XUnit"
    };

    [Fact]
    public void ShippingAndHeadlessProjects_ResolveOneAvaloniaRuntimeVersion()
    {
        var root = FindRoot();
        var versions = RuntimeReferences(root).Select(reference => ResolveVersion(root, reference.Version));
        versions.Distinct().Count().ShouldBe(1,
            "UI regressions must run against the Avalonia version used by the released desktop");
    }

    [Fact]
    public void RuntimePackageDeclarations_UseTheSharedVersionProperty()
    {
        var references = RuntimeReferences(FindRoot()).ToArray();
        references.Length.ShouldBeGreaterThan(8);
        foreach (var reference in references)
            reference.Version.ShouldBe("$(AvaloniaVersion)", $"{reference.Project}: {reference.Package}");
    }

    private static IEnumerable<(string Project, string Package, string Version)> RuntimeReferences(string root)
    {
        foreach (var relative in new[] { "CAP.Avalonia/CAP.Avalonia.csproj", "CAP.Desktop/CAP.Desktop.csproj",
                     "CAP.Browser/CAP.Browser.csproj", "UnitTests/UnitTests.csproj" })
        {
            var project = XDocument.Load(Path.Combine(root, relative));
            foreach (var item in project.Descendants("PackageReference"))
            {
                var package = (string?)item.Attribute("Include") ?? "";
                if (RuntimePackages.Contains(package))
                    yield return (relative, package, (string?)item.Attribute("Version") ?? "");
            }
        }
    }

    private static string ResolveVersion(string root, string declaration)
    {
        if (declaration != "$(AvaloniaVersion)") return declaration;
        var version = XDocument.Load(Path.Combine(root, "Directory.Build.props"))
            .Descendants("AvaloniaVersion").Single().Value;
        version.ShouldNotBeNullOrWhiteSpace();
        return version;
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
