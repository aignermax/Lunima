namespace CAP_Core.Export;

/// <summary>Geometry rendering boundary used by application workflows independently of Python process ownership.</summary>
public interface IPdkGeometryRenderer
{
    Task<NazcaPreviewResult> RenderAsync(string? moduleName, string nazcaFunction, string? nazcaParameters,
        CancellationToken ct = default);

    Task<NazcaPreviewResult> RenderRawCodeAsync(string code, CancellationToken ct = default);
}
