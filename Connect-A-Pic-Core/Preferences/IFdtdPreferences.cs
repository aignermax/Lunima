using CAP_Core.Solvers.Fdtd;

namespace CAP_Core.Preferences;

/// <summary>Persisted FDTD backend choice and cloud credential.</summary>
public interface IFdtdPreferences
{
    FdtdBackendType GetFdtdBackend();
    void SetFdtdBackend(FdtdBackendType backend);
    string GetTidy3dApiKey();
    void SetTidy3dApiKey(string apiKey);
}
