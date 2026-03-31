namespace CAP_Core.Update;

/// <summary>
/// Represents a semantic version (Major.Minor.Patch) with comparison support.
/// </summary>
public sealed class SemanticVersion : IComparable<SemanticVersion>, IEquatable<SemanticVersion>
{
    /// <summary>Gets the major version component.</summary>
    public int Major { get; }

    /// <summary>Gets the minor version component.</summary>
    public int Minor { get; }

    /// <summary>Gets the patch version component.</summary>
    public int Patch { get; }

    /// <summary>Initializes a new instance of <see cref="SemanticVersion"/>.</summary>
    public SemanticVersion(int major, int minor, int patch)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
    }

    /// <summary>
    /// Parses a version string in the format "X.Y.Z" or "vX.Y.Z".
    /// Pre-release labels (e.g. "-beta") are stripped from the patch component.
    /// </summary>
    /// <exception cref="FormatException">Thrown if the string is not a valid version.</exception>
    public static SemanticVersion Parse(string version)
    {
        var trimmed = version.TrimStart('v', 'V');
        var parts = trimmed.Split('.');

        if (parts.Length < 2)
            throw new FormatException($"Invalid semantic version format: '{version}'");

        if (!int.TryParse(parts[0], out var major))
            throw new FormatException($"Invalid major component in version: '{version}'");

        if (!int.TryParse(parts[1], out var minor))
            throw new FormatException($"Invalid minor component in version: '{version}'");

        var patchStr = parts.Length >= 3 ? parts[2].Split('-')[0] : "0";
        if (!int.TryParse(patchStr, out var patch))
            throw new FormatException($"Invalid patch component in version: '{version}'");

        return new SemanticVersion(major, minor, patch);
    }

    /// <summary>
    /// Attempts to parse a version string. Returns false if the format is invalid.
    /// </summary>
    public static bool TryParse(string? version, out SemanticVersion? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(version))
            return false;

        try
        {
            result = Parse(version);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public int CompareTo(SemanticVersion? other)
    {
        if (other is null) return 1;
        if (Major != other.Major) return Major.CompareTo(other.Major);
        if (Minor != other.Minor) return Minor.CompareTo(other.Minor);
        return Patch.CompareTo(other.Patch);
    }

    /// <inheritdoc/>
    public bool Equals(SemanticVersion? other) => CompareTo(other) == 0;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is SemanticVersion v && Equals(v);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch);

    /// <inheritdoc/>
    public override string ToString() => $"{Major}.{Minor}.{Patch}";

    /// <summary>Returns true if <paramref name="a"/> is greater than <paramref name="b"/>.</summary>
    public static bool operator >(SemanticVersion a, SemanticVersion b) => a.CompareTo(b) > 0;

    /// <summary>Returns true if <paramref name="a"/> is less than <paramref name="b"/>.</summary>
    public static bool operator <(SemanticVersion a, SemanticVersion b) => a.CompareTo(b) < 0;

    /// <summary>Returns true if <paramref name="a"/> is greater than or equal to <paramref name="b"/>.</summary>
    public static bool operator >=(SemanticVersion a, SemanticVersion b) => a.CompareTo(b) >= 0;

    /// <summary>Returns true if <paramref name="a"/> is less than or equal to <paramref name="b"/>.</summary>
    public static bool operator <=(SemanticVersion a, SemanticVersion b) => a.CompareTo(b) <= 0;
}
