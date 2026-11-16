using System.Runtime.InteropServices;
using CAP_Core.Export;
using Shouldly;

namespace UnitTests.Export;

public class ProcessLaunchErrorModeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Scope_PreservesExistingFlags_AndRestoresAfterLaunch(bool fail)
    {
        if (!OperatingSystem.IsWindows()) return;
        var original = GetThreadErrorMode();
        try
        {
            SetThreadErrorMode(0x8000, out _).ShouldBeTrue();
            try
            {
                using var scope = new ProcessLaunchErrorMode();
                GetThreadErrorMode().ShouldBe(0x8001u);
                if (fail) throw new InvalidOperationException("Launch failed");
            }
            catch (InvalidOperationException) when (fail) { }
            GetThreadErrorMode().ShouldBe(0x8000u);
        }
        finally { SetThreadErrorMode(original, out _); }
    }

    [Fact]
    public void Scope_CanBeUsedOnEveryPlatform()
    {
        using var scope = new ProcessLaunchErrorMode();
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetThreadErrorMode();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetThreadErrorMode(uint mode, out uint oldMode);
}
