using Microsoft.Win32;

namespace CAP.Avalonia.Services.Update;

/// <summary>Distinguishes an MSI installation from a portable/source copy before updating it.</summary>
internal static class WindowsMsiRegistration
{
    internal static string? ReadInstallRoot()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = machine.OpenSubKey(@"Software\Lunima\Installer");
            return key?.GetValue("InstallRoot") as string;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return null;
        }
    }

    internal static bool Matches(InstallLocation location, string? registeredRoot)
    {
        if (string.IsNullOrWhiteSpace(registeredRoot)) return false;
        try
        {
            static string Normalize(string path) => Path.GetFullPath(path).TrimEnd('/', '\\');
            return string.Equals(Normalize(location.Root), Normalize(registeredRoot), StringComparison.OrdinalIgnoreCase)
                && string.Equals(Normalize(Path.GetDirectoryName(location.ExecutablePath)!), Normalize(registeredRoot), StringComparison.OrdinalIgnoreCase)
                && string.Equals(Path.GetFileName(location.ExecutablePath), "CAP.Desktop.exe", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
