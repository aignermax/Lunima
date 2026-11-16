using System.ComponentModel;
using System.Runtime.InteropServices;

namespace CAP_Core.Export;

/// <summary>
/// Suppresses Windows loader error dialogs on the current thread during a synchronous launch.
/// The scope must be disposed on that same thread, before any await.
/// </summary>
internal sealed class ProcessLaunchErrorMode : IDisposable
{
    private const uint FailCriticalErrors = 0x0001;
    private readonly uint _previousMode;
    private bool _disposed;

    public ProcessLaunchErrorMode()
    {
        if (OperatingSystem.IsWindows() &&
            !SetThreadErrorMode(GetThreadErrorMode() | FailCriticalErrors, out _previousMode))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (OperatingSystem.IsWindows() && !SetThreadErrorMode(_previousMode, out _))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        _disposed = true;
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetThreadErrorMode();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetThreadErrorMode(uint mode, out uint oldMode);
}
