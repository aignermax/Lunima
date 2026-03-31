using System.Diagnostics;

namespace CAP.Avalonia.Services;

/// <summary>
/// Launches URLs via the default OS handler.
/// </summary>
public sealed class SystemUrlLauncher : IUrlLauncher
{
    public void Open(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }
}
