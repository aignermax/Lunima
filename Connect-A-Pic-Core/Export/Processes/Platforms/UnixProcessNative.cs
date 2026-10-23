using System.Runtime.InteropServices;

namespace CAP_Core.Export.Processes;

internal static class UnixProcessNative
{
    internal const int Interrupted = 4;
    internal const int OperationNotPermitted = 1;
    internal const int NoSuchProcess = 3;
    internal const int KillSignal = 9;
    internal const int ChildExited = 1;
    internal const int ChildKilled = 2;
    internal const int ChildDumped = 3;
    internal static int WaitOptions => OperatingSystem.IsMacOS() ? 0x4 | 0x20 | 0x1 : 0x4 | 0x01000000 | 0x1;
    internal static int SpawnAttributeBytes => OperatingSystem.IsMacOS() ? nint.Size : 336;
    internal static int SpawnActionBytes => OperatingSystem.IsMacOS() ? nint.Size : 80;
    internal static int SignalInfoPidOffset => OperatingSystem.IsMacOS() ? 12 : 16;
    internal static int SignalInfoStatusOffset => OperatingSystem.IsMacOS() ? 20 : 24;
    // siginfo_t is 104 bytes on Darwin and 128 bytes on glibc/musl x64/arm64.
    internal const int SignalInfoBytes = 128;

    [DllImport("libc", SetLastError = true)] internal static extern int kill(int pid, int signal);
    [DllImport("libc", SetLastError = true)] internal static extern int waitpid(int pid, out int status, int options);
    [DllImport("libc", SetLastError = true)] internal static extern int waitid(int type, uint pid, nint info, int options);
    // .NET 10's fixed-signature PAL wrapper performs atomic F_DUPFD_CLOEXEC and retries EINTR.
    // A fixed three-argument libc fcntl import is ABI-incorrect on Darwin ARM64 (variadic arg on stack).
    // https://github.com/dotnet/runtime/blob/v10.0.0/src/native/libs/System.Native/pal_io.c#L328-L343
    [DllImport("System.Native", EntryPoint = "SystemNative_Dup", SetLastError = true)]
    internal static extern nint DuplicateCloseOnExec(nint fd);
    [DllImport("libc", SetLastError = true)] internal static extern int close(int fd);
    [DllImport("libc")] internal static extern int posix_spawnattr_init(nint attributes);
    [DllImport("libc")] internal static extern int posix_spawnattr_destroy(nint attributes);
    [DllImport("libc")] internal static extern int posix_spawnattr_setflags(nint attributes, short flags);
    [DllImport("libc")] internal static extern int posix_spawnattr_setpgroup(nint attributes, int group);
    [DllImport("libc")] internal static extern int posix_spawnattr_setsigmask(nint attributes, nint signals);
    [DllImport("libc")] internal static extern int posix_spawnattr_setsigdefault(nint attributes, nint signals);
    [DllImport("libc")] internal static extern int sigemptyset(nint signals);
    [DllImport("libc")] internal static extern int sigfillset(nint signals);
    [DllImport("libc")] internal static extern int posix_spawn_file_actions_init(nint actions);
    [DllImport("libc")] internal static extern int posix_spawn_file_actions_destroy(nint actions);
    [DllImport("libc")] internal static extern int posix_spawn_file_actions_adddup2(nint actions, int descriptor, int target);
    [DllImport("libc")] internal static extern int posix_spawn_file_actions_addclose(nint actions, int descriptor);
    [DllImport("libc")] internal static extern int posix_spawn_file_actions_addchdir_np(nint actions,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path);
    [DllImport("libc")] internal static extern int posix_spawn(out int pid,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path, nint actions, nint attributes, nint arguments, nint environment);
    [DllImport("/usr/lib/libproc.dylib", SetLastError = true)] internal static extern int proc_listpids(
        uint type, uint group, nint buffer, int size);
    [DllImport("/usr/lib/libproc.dylib", SetLastError = true)] internal static extern int proc_pidinfo(
        int pid, int flavor, ulong argument, nint buffer, int size);
}
