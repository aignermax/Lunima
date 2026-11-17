using Shouldly;
using Xunit;

namespace UnitTests.AgentBridge.Headless.McpProcess;

/// <summary>Checks cleanup of the private profile created for desktop process tests.</summary>
public sealed class LunimaSandboxTests
{
    /// <summary>Ordinary cleanup is portable and safe to repeat.</summary>
    [Fact]
    public async Task DisposeAsync_RemovesSandboxAndCanBeRepeated()
    {
        var sandbox = LunimaSandbox.Create();
        await File.WriteAllTextAsync(sandbox.PathFor("design.lun"), "test design");

        await sandbox.DisposeAsync();
        Directory.Exists(sandbox.Root).ShouldBeFalse();
        await sandbox.DisposeAsync();
    }

    /// <summary>Shell-created read-only directories and metadata must not prevent cleanup.</summary>
    [SkippableFact]
    public async Task DisposeAsync_RemovesReadOnlyWindowsProfileEntries()
    {
        Skip.IfNot(OperatingSystem.IsWindows());
        var sandbox = LunimaSandbox.Create();
        var programs = Path.Combine(sandbox.Environment["APPDATA"], "Microsoft", "Windows", "Start Menu", "Programs");
        Directory.CreateDirectory(programs);
        var file = Path.Combine(programs, "desktop.ini");
        await File.WriteAllTextAsync(file, "profile metadata");
        File.SetAttributes(file, File.GetAttributes(file) | FileAttributes.ReadOnly);
        File.SetAttributes(programs, File.GetAttributes(programs) | FileAttributes.ReadOnly);
        File.SetAttributes(sandbox.Root, File.GetAttributes(sandbox.Root) | FileAttributes.ReadOnly);
        try
        {
            await sandbox.DisposeAsync();
            Directory.Exists(sandbox.Root).ShouldBeFalse();
            await sandbox.DisposeAsync();
        }
        finally
        {
            foreach (var path in new[] { file, programs, sandbox.Root })
                if (Path.Exists(path))
                    File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
            await sandbox.DisposeAsync();
        }
    }
}
