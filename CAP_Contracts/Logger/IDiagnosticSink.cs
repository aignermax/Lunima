namespace CAP_Contracts.Logger;

/// <summary>Receives application diagnostics without exposing a UI collection or dispatcher.</summary>
public interface IDiagnosticSink
{
    void LogError(string message, Exception? ex = null);
    void LogWarning(string message);
    void LogInfo(string message);
}
