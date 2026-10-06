using System.Globalization;
using System.Text.RegularExpressions;

namespace CAP.Avalonia.Services.OpenEblCheck;

/// <summary>
/// Parses the stdout of the vendored openEBL check scripts into typed entries.
/// Both scripts print their error count as the last stdout line.
/// </summary>
internal static class OpenEblReportParser
{
    private static readonly Regex DieSizeErrorLine = new(
        @"^Error: Bounding box of selected layers \(([\d.]+) um x ([\d.]+) um\) exceeds allowed size",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex BoundingBoxLine = new(
        @"^Bounding box of selected layers is ([\d.]+) um x ([\d.]+) um",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex VerificationCategoryLine = new(
        @"^category (.+): (\d+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Parses the submission-check output. The error count is -1 when the last
    /// line is not a bare integer (script crashed mid-run — never a silent pass).
    /// </summary>
    public static (int ErrorCount, List<OpenEblCheckError> Errors, OpenEblDieBoundingBox? BoundingBox)
        ParseSubmissionOutput(string output)
    {
        var errors = new List<OpenEblCheckError>();
        OpenEblDieBoundingBox? boundingBox = null;

        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');

            var dieSize = DieSizeErrorLine.Match(line);
            if (dieSize.Success)
            {
                boundingBox = ParseBox(dieSize.Groups[1].Value, dieSize.Groups[2].Value);
                errors.Add(new OpenEblCheckError(OpenEblCheckCategories.DieSize, line));
                continue;
            }

            var box = BoundingBoxLine.Match(line);
            if (box.Success)
            {
                boundingBox = ParseBox(box.Groups[1].Value, box.Groups[2].Value);
                continue;
            }

            var category = ClassifySubmissionLine(line);
            if (category != null)
                errors.Add(new OpenEblCheckError(category, line));
        }

        return (ParseErrorCount(output), errors, boundingBox);
    }

    /// <summary>
    /// Parses the verification (layout_check) output: every printed category with a
    /// non-zero count becomes an error entry under that rule name; an
    /// "Unknown error occurred" abort becomes a <see cref="OpenEblCheckCategories.VerificationCrash"/>.
    /// </summary>
    public static (int ErrorCount, List<OpenEblCheckError> Errors) ParseVerificationOutput(string output)
    {
        var errors = new List<OpenEblCheckError>();

        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');

            var category = VerificationCategoryLine.Match(line);
            if (category.Success && int.Parse(category.Groups[2].Value, CultureInfo.InvariantCulture) > 0)
            {
                var rule = category.Groups[1].Value;
                var count = category.Groups[2].Value;
                errors.Add(new OpenEblCheckError(
                    rule,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"SiEPIC layout_check reported {count} error(s) of category '{rule}'.")));
                continue;
            }

            if (line.StartsWith("Unknown error occurred", StringComparison.Ordinal))
                errors.Add(new OpenEblCheckError(OpenEblCheckCategories.VerificationCrash, line));
        }

        return (ParseErrorCount(output), errors);
    }

    private static string? ClassifySubmissionLine(string line)
    {
        if (line.StartsWith("Error: layout does not have 1 top cell", StringComparison.Ordinal))
            return OpenEblCheckCategories.TopCell;
        if (line.StartsWith("No shapes found in the specified layers.", StringComparison.Ordinal))
            return OpenEblCheckCategories.Floorplan;
        if (line.StartsWith("ERROR: unidentified black box cells", StringComparison.Ordinal))
            return OpenEblCheckCategories.BlackBoxCells;
        if (line.StartsWith("Error: the layer ", StringComparison.Ordinal)
            && line.EndsWith("is not defined in the PDK.", StringComparison.Ordinal))
            return OpenEblCheckCategories.LayerConformity;
        if (line.StartsWith("Error:", StringComparison.Ordinal)
            || line.StartsWith("ERROR:", StringComparison.Ordinal))
            return OpenEblCheckCategories.SubmissionCheck;
        return null;
    }

    private static OpenEblDieBoundingBox ParseBox(string width, string height) =>
        new(
            double.Parse(width, CultureInfo.InvariantCulture),
            double.Parse(height, CultureInfo.InvariantCulture));

    private static int ParseErrorCount(string output)
    {
        var lastLine = output.TrimEnd().Split('\n').LastOrDefault()?.Trim();
        return int.TryParse(lastLine, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)
            ? count
            : -1;
    }
}
