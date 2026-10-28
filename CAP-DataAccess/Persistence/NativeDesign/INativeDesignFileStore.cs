namespace CAP_DataAccess.Persistence.NativeDesign;

/// <summary>Native document I/O; the editor owns capture, navigation and publication.</summary>
public interface INativeDesignFileStore
{
    bool Exists(string filePath);
    Task<string> ReadAsync(string filePath, CancellationToken cancellationToken = default);
    Task WriteAsync(string filePath, DesignFileData data, CancellationToken cancellationToken = default);
}
