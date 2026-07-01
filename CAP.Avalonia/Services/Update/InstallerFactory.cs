using CAP_Core.Export;

namespace CAP.Avalonia.Services.Update;

/// <summary>
/// Creates the <see cref="IInstaller"/> implementation appropriate for the current operating system.
/// </summary>
public static class InstallerFactory
{
    /// <summary>
    /// Returns a platform-specific <see cref="IInstaller"/>:
    /// <list type="bullet">
    ///   <item><description>macOS → <see cref="MacOsBundleInstaller"/></description></item>
    ///   <item><description>Windows → <see cref="WindowsMsiInstaller"/></description></item>
    ///   <item><description>Linux → <see cref="LinuxTarballInstaller"/></description></item>
    /// </list>
    /// </summary>
    /// <param name="launchFactory">
    /// Process-launch factory for cross-platform executable resolution; pass <c>null</c> to use
    /// <see cref="ProcessLaunchFactory.CreateDefault"/>.
    /// </param>
    public static IInstaller Create(ProcessLaunchFactory? launchFactory = null)
    {
        var factory = launchFactory ?? ProcessLaunchFactory.CreateDefault();

        if (OperatingSystem.IsMacOS())
            return new MacOsBundleInstaller(factory);

        if (OperatingSystem.IsWindows())
            return new WindowsMsiInstaller(factory);

        return new LinuxTarballInstaller(factory);
    }
}
