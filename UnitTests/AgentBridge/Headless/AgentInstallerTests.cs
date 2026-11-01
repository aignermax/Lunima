using System;
using System.IO;
using CAP.Avalonia.Services.AgentBridge.Headless;
using Shouldly;
using Xunit;

namespace UnitTests.AgentBridge.Headless;

/// <summary>The <c>lunima</c> launcher written by <c>lunima agent install</c>.</summary>
public class AgentInstallerTests
{
    [Fact]
    public void Launcher_ForwardsEveryArgument_ToTheExecutable()
    {
        var home = Path.Combine(Path.GetTempPath(), "lunima-install-" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = AgentInstaller.InstallLauncher("/opt/My Lunima/CAP.Desktop", home);

            result.LauncherPath.ShouldStartWith(home);
            var text = File.ReadAllText(result.LauncherPath);
            text.ShouldContain("\"/opt/My Lunima/CAP.Desktop\"", customMessage: "the path is quoted (spaces)");
            text.ShouldContain(OperatingSystem.IsWindows() ? "%*" : "\"$@\"");
            if (!OperatingSystem.IsWindows())
                File.GetUnixFileMode(result.LauncherPath).HasFlag(UnixFileMode.UserExecute).ShouldBeTrue();
        }
        finally
        {
            if (Directory.Exists(home)) Directory.Delete(home, recursive: true);
        }
    }
}
