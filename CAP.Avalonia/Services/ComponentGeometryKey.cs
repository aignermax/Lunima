// CAP.Avalonia/Services/ComponentGeometryKey.cs
using System;
using System.Security.Cryptography;
using System.Text;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.Services;

/// <summary>
/// Geometry identity of a component, used to scope S-matrix overrides: components with
/// the same geometry must share the same (recomputed) S-matrix. When a raw-code Nazca
/// override (#561) is active the code itself defines the geometry; otherwise the Nazca
/// call (module|function|parameters) does. Same identity ⇒ same key.
/// </summary>
public static class ComponentGeometryKey
{
    /// <summary>Bump to invalidate all geometry-scoped override keys.</summary>
    public const int FormatVersion = 1;

    /// <summary>
    /// Builds the geometry key. <paramref name="rawCodeLookup"/> returns the active raw-code
    /// override for the component (e.g. from StoredNazcaOverrides), or null if none.
    /// </summary>
    public static string For(Component component, Func<Component, string?> rawCodeLookup)
    {
        var raw = rawCodeLookup(component);
        if (!string.IsNullOrWhiteSpace(raw))
            return $"raw:v{FormatVersion}-{Hash(raw)}";

        var material = $"{component.NazcaModuleName}{component.NazcaFunctionName}{component.NazcaFunctionParameters}";
        return $"geo:v{FormatVersion}-{Hash(material)}";
    }

    private static string Hash(string s)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(s));
        return Convert.ToHexString(bytes, 0, 12).ToLowerInvariant();
    }
}
