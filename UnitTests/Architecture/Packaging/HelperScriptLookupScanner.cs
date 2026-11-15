using System.Text.RegularExpressions;

namespace UnitTests.Architecture.Packaging;

/// <summary>
/// A path under <c>scripts/</c> that production code resolves beside the binary.
/// </summary>
/// <param name="ScriptPath">Bundle-relative path with forward slashes, e.g. <c>scripts/mode_solve.py</c>;
/// <c>null</c> when the lookup is built from a non-literal argument and cannot be checked.</param>
/// <param name="Location">Repository-relative source file and line, e.g. <c>CAP.Avalonia/DI/X.cs:27</c>.</param>
internal sealed record HelperScriptLookup(string? ScriptPath, string Location);

/// <summary>
/// Finds the helper scripts production code looks up under <c>scripts/</c>: the arguments of
/// <c>PythonResolution.FindScript(…)</c>, literal <c>"scripts", "…"</c> path segments and
/// <c>"scripts/…"</c> literals. Comment lines are ignored so documentation can name a script freely.
/// </summary>
internal static class HelperScriptLookupScanner
{
    /// <summary>Production projects whose sources are scanned, relative to the repository root.</summary>
    public static readonly string[] ProductionRoots = { "CAP.Avalonia", "Connect-A-Pic-Core", "CAP-DataAccess", "CAP.Desktop" };

    private const string ScriptsFolder = "scripts";
    private const string StringLiteralPattern = "\"[^\"\\r\\n]+\"";

    private static readonly Regex FindScriptCall = new(@"PythonResolution\.FindScript\((?<arguments>[^)]*)\)");
    private static readonly Regex ScriptsSegments = new($"\"{ScriptsFolder}\"(?<segments>(?:\\s*,\\s*{StringLiteralPattern})+)");
    private static readonly Regex ScriptsPathLiteral = new($"\"{ScriptsFolder}[\\\\/](?<path>[^\"\\r\\n]+)\"");
    private static readonly Regex StringLiteral = new("\"(?<value>[^\"\\r\\n]+)\"");
    private static readonly Regex LiteralArgumentList = new($"^\\s*{StringLiteralPattern}(?:\\s*,\\s*{StringLiteralPattern})*\\s*$");

    /// <summary>Every helper-script lookup in the production sources under <paramref name="repositoryRoot"/>.</summary>
    public static IReadOnlyList<HelperScriptLookup> Scan(string repositoryRoot) =>
        ProductionRoots
            .Select(project => Path.Combine(repositoryRoot, project))
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            .Where(file => !IsBuildOutput(repositoryRoot, file))
            .OrderBy(file => file, StringComparer.Ordinal)
            .SelectMany(file => ScanSource(File.ReadAllText(file), RelativePath(repositoryRoot, file)))
            .ToList();

    /// <summary>The lookups in one source text; <paramref name="sourceName"/> prefixes each location.</summary>
    public static IEnumerable<HelperScriptLookup> ScanSource(string source, string sourceName)
    {
        var code = WithoutCommentLines(source);
        foreach (Match call in FindScriptCall.Matches(code))
        {
            var arguments = call.Groups["arguments"].Value;
            var path = LiteralArgumentList.IsMatch(arguments) ? UnderScripts(LiteralValues(arguments)) : null;
            yield return new HelperScriptLookup(path, Location(sourceName, code, call.Index));
        }

        foreach (Match segments in ScriptsSegments.Matches(code))
            yield return new HelperScriptLookup(
                UnderScripts(LiteralValues(segments.Groups["segments"].Value)), Location(sourceName, code, segments.Index));

        foreach (Match literal in ScriptsPathLiteral.Matches(code))
            yield return new HelperScriptLookup(
                UnderScripts(literal.Groups["path"].Value.Split('/', '\\')), Location(sourceName, code, literal.Index));
    }

    private static string WithoutCommentLines(string source) =>
        string.Join('\n', source.Split('\n').Select(line => line.TrimStart().StartsWith("//", StringComparison.Ordinal) ? "" : line));

    private static IEnumerable<string> LiteralValues(string text) =>
        StringLiteral.Matches(text).Select(match => match.Groups["value"].Value);

    private static string UnderScripts(IEnumerable<string> segments) =>
        string.Join('/', new[] { ScriptsFolder }.Concat(segments));

    private static string Location(string sourceName, string code, int index) =>
        $"{sourceName}:{code.AsSpan(0, index).Count('\n') + 1}";

    private static bool IsBuildOutput(string repositoryRoot, string file) =>
        RelativePath(repositoryRoot, file).Split('/').Any(segment => segment is "bin" or "obj");

    private static string RelativePath(string repositoryRoot, string file) =>
        Path.GetRelativePath(repositoryRoot, file).Replace('\\', '/');
}
