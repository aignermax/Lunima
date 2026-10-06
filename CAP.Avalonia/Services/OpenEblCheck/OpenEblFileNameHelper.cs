using System.Globalization;
using System.Text;

namespace CAP.Avalonia.Services.OpenEblCheck;

/// <summary>The openEBL course categories whose filename prefixes the submission repo accepts.</summary>
public enum OpenEblCourseCategory
{
    /// <summary>UBCx/edX "Silicon Photonics Design" course — prefix EBeam_.</summary>
    EBeam,

    /// <summary>UBC ELEC 413 students — prefix ELEC413_.</summary>
    Elec413,

    /// <summary>SiEPIC passives designs — prefix SiEPIC_Passives_.</summary>
    SiepicPassives,

    /// <summary>General openEBL submissions — prefix openEBL_.</summary>
    OpenEbl,
}

/// <summary>
/// Proposes openEBL-conformant GDS file names of the form
/// <c>EBeam_&lt;username&gt;_&lt;design&gt;.gds</c>, sanitising the user-supplied parts to
/// characters that are safe on every filesystem and accepted by the submission repo.
/// </summary>
public static class OpenEblFileNameHelper
{
    /// <summary>
    /// Builds the openEBL-conformant file name for the given username and design name.
    /// Umlauts/accents are folded to their base letters (ü → u), ß becomes ss, spaces and
    /// every character outside [A-Za-z0-9._-] become an underscore. All comparisons are
    /// ordinal / <see cref="CultureInfo.InvariantCulture"/> so the result is locale-independent.
    /// </summary>
    /// <param name="username">Submitter's username (edX / course account).</param>
    /// <param name="designName">Free-form design name, e.g. the .lun design title.</param>
    /// <param name="category">Course category selecting the required filename prefix.</param>
    /// <returns>The sanitised file name including the .gds extension.</returns>
    public static string ProposeFileName(
        string username,
        string designName,
        OpenEblCourseCategory category = OpenEblCourseCategory.EBeam)
    {
        var prefix = category switch
        {
            OpenEblCourseCategory.EBeam => "EBeam_",
            OpenEblCourseCategory.Elec413 => "ELEC413_",
            OpenEblCourseCategory.SiepicPassives => "SiEPIC_Passives_",
            OpenEblCourseCategory.OpenEbl => "openEBL_",
            _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
        };

        var user = Sanitize(username);
        var design = Sanitize(designName);
        if (user.Length == 0)
            user = "anonymous";
        if (design.Length == 0)
            design = "design";

        return string.Create(CultureInfo.InvariantCulture, $"{prefix}{user}_{design}.gds");
    }

    private static string Sanitize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var builder = new StringBuilder(value.Length);
        foreach (var c in value.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;
            if (char.IsWhiteSpace(c))
            {
                builder.Append('_');
                continue;
            }
            builder.Append(c switch
            {
                'ß' => "ss",
                >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '.' or '-' or '_'
                    => c.ToString(),
                _ => "_",
            });
        }
        return builder.ToString();
    }
}
