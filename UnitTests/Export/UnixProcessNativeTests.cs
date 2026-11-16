using System.Runtime.InteropServices;
using CAP_Core.Export.Processes;
using Shouldly;

namespace UnitTests.Export;

public sealed class UnixProcessNativeTests
{
    private const int GetDescriptorFlags = 1;
    private const int CloseOnExec = 1;
    private const int BadDescriptor = 9;

    [SkippableFact]
    public void LinuxDuplicate_IsIndependentAndCloseOnExec()
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        using var source = File.OpenRead("/dev/null");
        var descriptor = source.SafeFileHandle.DangerousGetHandle();
        var duplicate = (int)UnixProcessNative.DuplicateCloseOnExec(descriptor);
        duplicate.ShouldBeGreaterThanOrEqualTo(0);
        try
        {
            duplicate.ShouldNotBe((int)descriptor);
            (Fcntl(duplicate, GetDescriptorFlags, 0) & CloseOnExec).ShouldBe(CloseOnExec);
            source.Dispose();
            Fcntl(duplicate, GetDescriptorFlags, 0).ShouldBeGreaterThanOrEqualTo(0);
        }
        finally { UnixProcessNative.close(duplicate); }
    }

    [SkippableFact]
    public void LinuxDuplicate_PreservesNativeError()
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        var duplicate = UnixProcessNative.DuplicateCloseOnExec(-1);
        var error = Marshal.GetLastPInvokeError();
        duplicate.ShouldBe((nint)(-1));
        error.ShouldBe(BadDescriptor);
    }

    [DllImport("libc", EntryPoint = "fcntl", SetLastError = true)]
    private static extern int Fcntl(int descriptor, int command, int argument);
}
