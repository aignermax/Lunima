using System.Xml.Linq;

namespace UnitTests.Architecture.Packaging;

/// <summary>
/// The files a project file publishes beside the binary: every unconditional <c>Content</c> item
/// with a <c>CopyToPublishDirectory</c>, expanded against the file system the way MSBuild expands
/// <c>Include</c> and placed at its <c>Link</c> (bundle-relative paths with forward slashes).
/// Glob shapes MSBuild supports beyond <c>dir\*.ext</c> and <c>dir\**\*.ext</c> throw
/// <see cref="NotSupportedException"/>, so the guard fails loudly instead of misjudging them.
/// </summary>
internal sealed class DesktopContentManifest
{
    private const string NeverCopy = "Never";
    private const string RecursiveWildcard = "**";
    private const char BundleSeparator = '/';

    private readonly HashSet<string> _shippedFiles;

    private DesktopContentManifest(HashSet<string> shippedFiles) => _shippedFiles = shippedFiles;

    /// <summary>Every published file, bundle-relative.</summary>
    public IReadOnlyCollection<string> ShippedFiles => _shippedFiles;

    /// <summary>Reads the <c>Content</c> items of <paramref name="projectFile"/>.</summary>
    public static DesktopContentManifest Load(string projectFile)
    {
        var projectDirectory = Path.GetDirectoryName(Path.GetFullPath(projectFile))!;
        var shipped = XDocument.Load(projectFile).Descendants()
            .Where(element => element.Name.LocalName == "Content" && IsPublishedEverywhere(element))
            .SelectMany(item => Destinations(item, projectDirectory))
            .ToHashSet(StringComparer.Ordinal);
        return new DesktopContentManifest(shipped);
    }

    /// <summary>Whether the bundle has <paramref name="bundlePath"/> as a file or as a non-empty folder.</summary>
    public bool Ships(string bundlePath) =>
        _shippedFiles.Contains(bundlePath) ||
        _shippedFiles.Any(file => file.StartsWith(bundlePath + BundleSeparator, StringComparison.Ordinal));

    private static bool IsPublishedEverywhere(XElement item)
    {
        var copy = Metadata(item, "CopyToPublishDirectory");
        return item.Attribute("Include") != null &&
               item.AncestorsAndSelf().All(element => element.Attribute("Condition") == null) &&
               !string.IsNullOrEmpty(copy) && copy != NeverCopy;
    }

    private static IEnumerable<string> Destinations(XElement item, string projectDirectory)
    {
        if (item.Attribute("Exclude") != null)
            throw new NotSupportedException($"Content Exclude is not modelled by this guard: {item}");

        var link = Metadata(item, "Link");
        return item.Attribute("Include")!.Value
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .SelectMany(pattern => Expand(projectDirectory, pattern))
            .Select(match => Destination(link, projectDirectory, match.File, match.RecursiveDirectory));
    }

    private static IEnumerable<(string File, string RecursiveDirectory)> Expand(string projectDirectory, string pattern)
    {
        var segments = pattern.Split('\\', '/');
        var wildcardIndex = Array.FindIndex(segments, segment => segment.Contains('*') || segment.Contains('?'));
        if (wildcardIndex < 0)
        {
            var file = Path.GetFullPath(Path.Combine(projectDirectory, Path.Combine(segments)));
            return File.Exists(file) ? new[] { (file, "") } : Array.Empty<(string, string)>();
        }

        var baseDirectory = Path.GetFullPath(Path.Combine(projectDirectory, Path.Combine(segments[..wildcardIndex])));
        var search = segments[wildcardIndex..] switch
        {
            [var name] when name != RecursiveWildcard => (Name: name, Option: SearchOption.TopDirectoryOnly),
            [RecursiveWildcard, var name] => (Name: name, Option: SearchOption.AllDirectories),
            _ => throw new NotSupportedException($"Content glob '{pattern}' is not modelled by this guard."),
        };
        if (!Directory.Exists(baseDirectory))
            return Array.Empty<(string, string)>();
        return Directory.EnumerateFiles(baseDirectory, search.Name, search.Option)
            .Select(file => (file, RecursiveDirectory(baseDirectory, file)));
    }

    private static string RecursiveDirectory(string baseDirectory, string file)
    {
        var relative = Path.GetRelativePath(baseDirectory, Path.GetDirectoryName(file)!);
        return relative == "." ? "" : relative + Path.DirectorySeparatorChar;
    }

    private static string Destination(string? link, string projectDirectory, string file, string recursiveDirectory)
    {
        var target = link == null
            ? DefaultTargetPath(projectDirectory, file)
            : link.Replace("%(RecursiveDir)", recursiveDirectory)
                .Replace("%(Filename)", Path.GetFileNameWithoutExtension(file))
                .Replace("%(Extension)", Path.GetExtension(file));
        if (target.Contains("%("))
            throw new NotSupportedException($"Content Link '{link}' uses metadata this guard does not model.");
        return target.Replace('\\', BundleSeparator).Replace(Path.DirectorySeparatorChar, BundleSeparator);
    }

    /// <summary>MSBuild's AssignTargetPath without a Link: the project-relative path, or just the name outside the project.</summary>
    private static string DefaultTargetPath(string projectDirectory, string file)
    {
        var relative = Path.GetRelativePath(projectDirectory, file);
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)
            ? Path.GetFileName(file)
            : relative;
    }

    private static string? Metadata(XElement item, string name) =>
        item.Attribute(name)?.Value ?? item.Elements().FirstOrDefault(child => child.Name.LocalName == name)?.Value;
}
