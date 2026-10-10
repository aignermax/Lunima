using System.Security.Cryptography;
using System.Text;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.Controls.Canvas.ComponentPreview;

/// <summary>
/// Resolution-independent render identity for a GDS preview: the geometry depends
/// only on the Nazca module/function/parameters, not on the display size. Used as
/// both the in-memory and on-disk cache key.
/// </summary>
public readonly record struct GdsPreviewKey(string? Module, string? Function, string? Parameters)
{
    /// <summary>Bump to invalidate every cached entry (format or render-semantics change).</summary>
    // v3: failed renders are no longer persisted as empty markers, so any poisoned "empty"
    // entries written under v2 (from a transient broken/half-provisioned interpreter) are
    // discarded and re-rendered cleanly (#570 field test).
    public const int FormatVersion = 3;

    /// <summary>ASCII unit separator — never appears in module/function/parameter strings.</summary>
    private const char FieldSeparator = (char)31;

    /// <summary>
    /// Module-qualified gdsfactory factory name for gdsfactory-native components
    /// (e.g. "cspdk.sin300.mmi1x2"); null for Nazca components. When set, the geometry is
    /// rendered via the gdsfactory preview service instead of Nazca (#570).
    /// </summary>
    public string? GdsFactoryFunction { get; init; }

    /// <summary>True when there is something to render — a Nazca function or a gdsfactory
    /// factory (built-in / external-port components have neither).</summary>
    public bool IsRenderable =>
        !string.IsNullOrWhiteSpace(Function) || !string.IsNullOrWhiteSpace(GdsFactoryFunction);

    /// <summary>
    /// Builds the render identity of a placed component: the same key the library thumbnail
    /// of its template uses, so both share one cached render. gdsfactory-native components
    /// (module-qualified <see cref="Component.GdsFactoryFunction"/>, e.g. "cspdk.sin300.mmi1x2")
    /// render via gdsfactory; their placement-synthesized nazcaFunction is ignored because no
    /// Nazca script can render it.
    /// </summary>
    public static GdsPreviewKey ForComponent(Component component)
    {
        if (IsGdsFactoryNative(component.GdsFactoryFunction))
            return new GdsPreviewKey(component.NazcaModuleName, null, null)
                { GdsFactoryFunction = component.GdsFactoryFunction };
        return new GdsPreviewKey(component.NazcaModuleName, component.NazcaFunctionName,
            component.NazcaFunctionParameters);
    }

    /// <summary>True for a module-qualified gdsfactory factory name.</summary>
    public static bool IsGdsFactoryNative(string? gdsFactoryFunction) =>
        !string.IsNullOrWhiteSpace(gdsFactoryFunction) && gdsFactoryFunction.Contains('.');

    /// <summary>Stable filesystem-safe hash, prefixed with the format version.</summary>
    public string Hash()
    {
        // Separate the fields so distinct tuples can't collide via boundary shifts,
        // e.g. ("ab","c") vs ("a","bc").
        var material = $"{Module}{FieldSeparator}{Function}{FieldSeparator}{Parameters}{FieldSeparator}{GdsFactoryFunction}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        var hex = Convert.ToHexString(bytes, 0, 12).ToLowerInvariant(); // 24 hex chars
        return $"v{FormatVersion}-{hex}";
    }
}
