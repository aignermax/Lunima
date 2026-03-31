namespace CAP.Avalonia.Services;

/// <summary>
/// Opens URLs using the host operating system.
/// Abstracted so tests can verify behavior without launching a real browser.
/// </summary>
public interface IUrlLauncher
{
    void Open(string url);
}
