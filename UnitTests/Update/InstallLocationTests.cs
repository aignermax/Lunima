using CAP.Avalonia.Services.Update;
using Shouldly;

namespace UnitTests.Update;

public class InstallLocationTests
{
    [Fact]
    public void FindEnclosingAppBundle_ExecutableInsideBundle_ReturnsBundleRoot()
    {
        const string exe = "/Applications/Lunima.app/Contents/MacOS/CAP.Desktop";

        InstallLocation.FindEnclosingAppBundle(exe).ShouldBe("/Applications/Lunima.app");
    }

    [Fact]
    public void FindEnclosingAppBundle_NestedBundlePath_ReturnsNearestBundle()
    {
        const string exe = "/Users/me/Downloads/Lunima.app/Contents/MacOS/CAP.Desktop";

        InstallLocation.FindEnclosingAppBundle(exe).ShouldBe("/Users/me/Downloads/Lunima.app");
    }

    [Fact]
    public void FindEnclosingAppBundle_NotInsideBundle_ReturnsNull()
    {
        const string exe = "/usr/local/bin/Lunima";

        InstallLocation.FindEnclosingAppBundle(exe).ShouldBeNull();
    }
}
