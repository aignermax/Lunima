using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CAP_Core.Export.Processes;

internal static class UnixProcessLauncher
{
    internal static int Start(ProcessStartInfo info, OwnedProcessPipes pipes)
    {
        if ((!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) ||
            RuntimeInformation.ProcessArchitecture is not (Architecture.X64 or Architecture.Arm64))
            throw new PlatformNotSupportedException("Owned Unix tools require Linux/macOS x64 or arm64.");
        using var memory = new NativeLaunchMemory();
        var attributes = memory.Allocate(UnixProcessNative.SpawnAttributeBytes);
        var actions = memory.Allocate(UnixProcessNative.SpawnActionBytes);
        var signals = memory.Allocate(128);
        var arguments = memory.Utf8Vector(new[] { info.FileName }.Concat(ProcessArguments.Read(info)));
        var environment = memory.Utf8Vector(info.Environment.Where(pair => pair.Value != null)
            .Select(pair => pair.Key + "=" + pair.Value));
        var attributesReady = false;
        var actionsReady = false;
        var duplicates = new List<int>();
        try
        {
            Check(UnixProcessNative.posix_spawnattr_init(attributes));
            attributesReady = true;
            Check(UnixProcessNative.posix_spawn_file_actions_init(actions));
            actionsReady = true;
            Check(UnixProcessNative.posix_spawnattr_setpgroup(attributes, 0));
            UnixProcessNative.sigemptyset(signals);
            Check(UnixProcessNative.posix_spawnattr_setsigmask(attributes, signals));
            UnixProcessNative.sigfillset(signals);
            Check(UnixProcessNative.posix_spawnattr_setsigdefault(attributes, signals));
            // SETPGROUP, SETSIGDEF and SETSIGMASK apply in the child before exec.
            var flags = (short)(0x2 | 0x4 | 0x8 | (OperatingSystem.IsMacOS() ? 0x4000 : 0));
            Check(UnixProcessNative.posix_spawnattr_setflags(attributes, flags));
            for (var target = 0; target < 3; target++)
            {
                // Reserve sources above stdin/out/err so dup2 cannot overwrite another source.
                var fd = UnixProcessNative.fcntl((int)pipes.ChildHandles[target],
                    OperatingSystem.IsMacOS() ? 67 : 1030, 3); // F_DUPFD_CLOEXEC
                if (fd < 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
                duplicates.Add(fd);
                Check(UnixProcessNative.posix_spawn_file_actions_adddup2(actions, fd, target));
            }
            foreach (var descriptor in pipes.ChildHandles.Concat(pipes.ParentHandles)
                         .Select(handle => (int)handle).Concat(duplicates).Where(fd => fd > 2).Distinct())
                Check(UnixProcessNative.posix_spawn_file_actions_addclose(actions, descriptor));
            if (!string.IsNullOrEmpty(info.WorkingDirectory))
                Check(UnixProcessNative.posix_spawn_file_actions_addchdir_np(actions, info.WorkingDirectory));
            Check(UnixProcessNative.posix_spawn(out var pid, ResolveExecutable(info.FileName),
                actions, attributes, arguments, environment));
            return pid;
        }
        finally
        {
            foreach (var descriptor in duplicates) UnixProcessNative.close(descriptor);
            if (actionsReady) UnixProcessNative.posix_spawn_file_actions_destroy(actions);
            if (attributesReady) UnixProcessNative.posix_spawnattr_destroy(attributes);
        }
    }

    private static string ResolveExecutable(string name)
    {
        if (string.IsNullOrEmpty(name)) throw new ArgumentException("A tool executable is required.");
        if (Path.IsPathRooted(name)) return name;
        // Match Process.Start's direct-execution search, independent of the child's working directory.
        var candidates = new[] { Path.Combine(Path.GetDirectoryName(Environment.ProcessPath) ?? ".", name),
                Path.GetFullPath(name) }
            .Concat((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
                .Where(directory => directory.Length > 0).Select(directory => Path.Combine(directory, name)));
        return candidates.FirstOrDefault(File.Exists) ?? throw new Win32Exception(2, $"Tool not found: {name}");
    }

    private static void Check(int error)
    {
        if (error != 0) throw new Win32Exception(error);
    }
}
