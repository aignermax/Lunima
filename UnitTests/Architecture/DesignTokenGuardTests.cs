using System.Text.Json;
using System.Text.RegularExpressions;
using Shouldly;

namespace UnitTests.Architecture;

/// <summary>
/// Keeps the views on the design system (<c>CAP.Avalonia/Styles</c>): no new hard-coded hex
/// colors and no font sizes below the 11 px floor in AXAML outside the Styles folder.
/// </summary>
/// <remarks>
/// Existing views are migrated file by file, so the remaining literals are recorded in
/// <c>DesignTokenBaseline.json</c> as a ratchet: a file may never exceed its recorded count,
/// a new file starts at zero, and when a migration lowers a count the baseline must be
/// lowered with it so the gain cannot be lost again.
/// </remarks>
public class DesignTokenGuardTests
{
    private const string BaselineFileName = "DesignTokenBaseline.json";

    private static readonly Regex HexColorLiteral = new("\"#[0-9A-Fa-f]{3,8}\"", RegexOptions.Compiled);
    private static readonly Regex SubFloorFontSize = new("FontSize=\"(?:[0-9]|10)(?:\\.[0-9]+)?\"", RegexOptions.Compiled);

    [Fact]
    public void Views_DoNotAddHardCodedColorsOrTinyFonts()
    {
        var root = FindRepoRoot();
        var baseline = LoadBaseline(root);
        var violations = new List<string>();

        foreach (var (path, counts) in ScanViews(root))
        {
            baseline.TryGetValue(path, out var allowed);
            allowed ??= new LiteralCounts(0, 0);
            if (counts.HexColors > allowed.HexColors)
                violations.Add($"{path}: {counts.HexColors} hex colors (allowed {allowed.HexColors}) — use a Brush.* token");
            if (counts.SmallFonts > allowed.SmallFonts)
                violations.Add($"{path}: {counts.SmallFonts} font sizes below 11 (allowed {allowed.SmallFonts}) — use a FontSize.* token or a TextBlock class");
        }

        violations.ShouldBeEmpty(string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Baseline_IsTight_SoMigrationsCannotRegress()
    {
        var root = FindRepoRoot();
        var baseline = LoadBaseline(root);
        var actual = ScanViews(root).ToDictionary(e => e.Path, e => e.Counts);
        var stale = new List<string>();

        foreach (var (path, allowed) in baseline)
        {
            actual.TryGetValue(path, out var counts);
            counts ??= new LiteralCounts(0, 0);
            if (counts.HexColors < allowed.HexColors || counts.SmallFonts < allowed.SmallFonts)
                stale.Add($"{path}: now {counts.HexColors}/{counts.SmallFonts}, baseline {allowed.HexColors}/{allowed.SmallFonts}");
        }

        stale.ShouldBeEmpty($"Lower these entries in UnitTests/Architecture/{BaselineFileName} (remove a file once both are 0):"
            + Environment.NewLine + string.Join(Environment.NewLine, stale));
    }

    private static IEnumerable<(string Path, LiteralCounts Counts)> ScanViews(string root)
    {
        var avaloniaRoot = Path.Combine(root, "CAP.Avalonia");
        var stylesDir = Path.Combine(avaloniaRoot, "Styles") + Path.DirectorySeparatorChar;
        foreach (var file in Directory.EnumerateFiles(avaloniaRoot, "*.axaml", SearchOption.AllDirectories))
        {
            if (file.StartsWith(stylesDir, StringComparison.Ordinal) || IsBuildOutput(file))
                continue;
            var text = File.ReadAllText(file);
            var counts = new LiteralCounts(HexColorLiteral.Matches(text).Count, SubFloorFontSize.Matches(text).Count);
            if (counts.HexColors > 0 || counts.SmallFonts > 0)
                yield return (Path.GetRelativePath(root, file).Replace('\\', '/'), counts);
        }
    }

    private static Dictionary<string, LiteralCounts> LoadBaseline(string root)
    {
        var path = Path.Combine(root, "UnitTests", "Architecture", BaselineFileName);
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<Dictionary<string, LiteralCounts>>(json) ?? new();
    }

    private static bool IsBuildOutput(string path)
    {
        var sep = Path.DirectorySeparatorChar;
        return path.Contains($"{sep}obj{sep}") || path.Contains($"{sep}bin{sep}");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var gitPath = Path.Combine(dir.FullName, ".git");
            if (Directory.Exists(gitPath) || File.Exists(gitPath))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root (.git directory or file).");
    }

    /// <summary>Remaining literal counts recorded for one view file.</summary>
    public sealed record LiteralCounts(int HexColors, int SmallFonts);
}
