using Shouldly;
using Xunit;

namespace UnitTests.AgentBridge.Headless.McpProcess;

/// <summary>Checks cleanup of files and known folders created by child processes.</summary>
public sealed class LunimaSandboxTests
{
    /// <summary>Windows known folders can retain the read-only attribute after process exit.</summary>
    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposeAsync_RemovesReadOnlyKnownFoldersAndFiles(bool readOnlyFile)
    {
        Skip.IfNot(OperatingSystem.IsWindows());
        var sandbox = LunimaSandbox.Create();
        var programs = Path.Combine(sandbox.Environment["APPDATA"], "Microsoft", "Windows", "Start Menu", "Programs");
        Directory.CreateDirectory(programs);
        var file = Path.Combine(programs, "test.txt");
        await File.WriteAllTextAsync(file, "sandbox content");
        var protectedPath = readOnlyFile ? file : programs;
        File.SetAttributes(protectedPath, File.GetAttributes(protectedPath) | FileAttributes.ReadOnly);
        try
        {
            await sandbox.DisposeAsync();
            Directory.Exists(sandbox.Root).ShouldBeFalse();
        }
        finally
        {
            if (Path.Exists(protectedPath))
                File.SetAttributes(protectedPath, File.GetAttributes(protectedPath) & ~FileAttributes.ReadOnly);
            await sandbox.DisposeAsync();
        }
    }

    /// <summary>Cleanup also works for ordinary trees and already removed sandboxes.</summary>
    [Fact]
    public async Task DisposeAsync_RemovesNestedContentAndCanBeRepeated()
    {
        var sandbox = LunimaSandbox.Create();
        var nested = Directory.CreateDirectory(sandbox.PathFor("nested"));
        await File.WriteAllTextAsync(Path.Combine(nested.FullName, "test.txt"), "sandbox content");

        await sandbox.DisposeAsync();
        await sandbox.DisposeAsync();

        Directory.Exists(sandbox.Root).ShouldBeFalse();
    }
}
