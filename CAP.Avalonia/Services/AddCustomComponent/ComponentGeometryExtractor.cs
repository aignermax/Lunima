using CAP.Avalonia.ViewModels.ComponentSettings.InstanceOverride;
using CAP_Core.Export;
using CAP_DataAccess.Persistence.PIR;

namespace CAP.Avalonia.Services.AddCustomComponent;

public sealed record GeometryExtractResult(
    bool Success, string? Error, double WidthUm, double HeightUm,
    IReadOnlyList<OverridePinData> Pins, NazcaPreviewResult Raw);

public sealed class ComponentGeometryExtractor
{
    private readonly IPdkGeometryRenderer _nazca;
    private readonly IPdkGeometryRenderer _gdsFactory;

    public ComponentGeometryExtractor(IPdkGeometryRenderer nazcaPreview, IPdkGeometryRenderer gdsFactoryPreview)
    {
        _nazca = nazcaPreview;
        _gdsFactory = gdsFactoryPreview;
    }

    public async Task<GeometryExtractResult> ExtractAsync(GeometryReference reference, CancellationToken ct = default)
    {
        NazcaPreviewResult preview = await RenderAsync(reference, ct);

        if (!preview.Success)
            return new GeometryExtractResult(false, preview.Error, 0, 0, Array.Empty<OverridePinData>(), preview);

        double width = preview.XMax - preview.XMin;
        double height = preview.YMax - preview.YMin;
        var pins = OverridePinMapper.BuildOverridePins(preview);
        return new GeometryExtractResult(true, null, width, height, pins, preview);
    }

    private Task<NazcaPreviewResult> RenderAsync(GeometryReference reference, CancellationToken ct)
    {
        IPdkGeometryRenderer renderer = reference.Backend == GeometryBackend.GdsFactory ? _gdsFactory : _nazca;

        if (reference.RawSourceCode is not null)
            return renderer.RenderRawCodeAsync(reference.RawSourceCode, ct);

        return reference.Backend == GeometryBackend.GdsFactory
            ? _gdsFactory.RenderRawCodeAsync(reference.ToGdsFactoryRawCode(), ct)
            : _nazca.RenderAsync(reference.Module, reference.Function, reference.Parameters, ct);
    }
}
