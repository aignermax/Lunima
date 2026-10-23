namespace CAP_Core.Export.Contracts;

/// <summary>Environment-aware rendering backed by the shared bounded geometry store.</summary>
public interface IPdkPreviewBackend : IPdkGeometryRenderer, IPreviewGeometryStore
{
    string EnvironmentIdentity { get; }
    void InvalidateCache();
}
