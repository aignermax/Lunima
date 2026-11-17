using Shouldly;
using Xunit;

namespace UnitTests.AgentBridge.Headless.McpProcess;

/// <summary>Cleanup of the throw-away home a <c>lunima</c> child process runs in.</summary>
public sealed class LunimaSandboxTests
{
    private const string ShellFolderMarker = "desktop.ini";

    /// <summary>
    /// A Windows child started with APPDATA redirected into the sandbox leaves
    /// <c>Start Menu\Programs</c> there with the read-only attribute set; disposing must still
    /// remove the whole sandbox instead of failing the test that used it.
    /// </summary>
    [Fact]
    public async Task DisposeAsync_RemovesReadOnlyShellFoldersLeftByTheChild()
    {
        var sandbox = LunimaSandbox.Create();
        var startMenu = new DirectoryInfo(
            Path.Combine(sandbox.Environment["APPDATA"], "Microsoft", "Windows", "Start Menu"));
        var programs = startMenu.CreateSubdirectory("Programs");
        var marker = new FileInfo(Path.Combine(programs.FullName, ShellFolderMarker));
        File.WriteAllText(marker.FullName, string.Empty);
        MarkReadOnly(marker);
        MarkReadOnly(programs);
        MarkReadOnly(startMenu);

        await sandbox.DisposeAsync();

        Directory.Exists(sandbox.Root).ShouldBeFalse();
    }

    private static void MarkReadOnly(FileSystemInfo entry) => entry.Attributes |= FileAttributes.ReadOnly;
}
