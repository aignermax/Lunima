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
    public static IInstaller Create()
    {
        if (OperatingSystem.IsMacOS())
            return new MacOsBundleInstaller();

        if (OperatingSystem.IsWindows())
            return new WindowsMsiInstaller();

        return new LinuxTarballInstaller();
    }
}
