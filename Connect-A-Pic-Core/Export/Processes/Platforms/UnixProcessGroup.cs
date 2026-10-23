using System.ComponentModel;
using System.Runtime.InteropServices;

namespace CAP_Core.Export.Processes;

internal static class UnixProcessGroup
{
    internal static void Kill(int id)
    {
        if (id <= 1) throw new InvalidOperationException("Refusing an invalid owned process group.");
        if (UnixProcessNative.kill(-id, UnixProcessNative.KillSignal) == 0) return;
        var error = Marshal.GetLastPInvokeError();
        if (error != UnixProcessNative.NoSuchProcess) throw new Win32Exception(error);
    }

    internal static async Task WaitForTerminationAsync(int group, CancellationToken token)
    {
        while (HasLiveMembers(group))
        {
            token.ThrowIfCancellationRequested();
            Kill(group);
            await Task.Delay(10, token).ConfigureAwait(false);
        }
    }

    private static bool HasLiveMembers(int group)
    {
        if (OperatingSystem.IsLinux())
        {
            foreach (var directory in Directory.EnumerateDirectories("/proc"))
            {
                if (!int.TryParse(Path.GetFileName(directory), out _)) continue;
                try
                {
                    var stat = File.ReadAllText(Path.Combine(directory, "stat"));
                    var stateOffset = stat.LastIndexOf(')') + 2;
                    if (stateOffset < 2 || stateOffset >= stat.Length) continue;
                    var fields = stat[stateOffset..].Split(' ');
                    if (fields.Length > 2 && int.TryParse(fields[2], out var pgid) && pgid == group &&
                        fields[0] is not ("Z" or "X")) return true;
                }
                catch (IOException) { /* Process exited during enumeration. */ }
                catch (UnauthorizedAccessException) { /* Other users' processes cannot join this owned group. */ }
            }
            return false;
        }
        using var memory = new NativeLaunchMemory();
        var capacity = Math.Max(256, UnixProcessNative.proc_listpids(2, (uint)group, 0, 0) + 256);
        while (true)
        {
            var list = memory.Allocate(capacity);
            var count = UnixProcessNative.proc_listpids(2, (uint)group, list, capacity);
            if (count < 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
            if (count >= capacity) { capacity = checked(capacity * 2); continue; }
            var info = memory.Allocate(136); // proc_bsdinfo on supported Darwin x64/arm64 ABIs.
            for (var offset = 0; offset < count; offset += sizeof(int))
            {
                var pid = Marshal.ReadInt32(list, offset);
                if (pid <= 0) continue;
                var length = UnixProcessNative.proc_pidinfo(pid, 3, 0, info, 136);
                if (length > 0 && Marshal.ReadInt32(info, 4) != 5) return true; // SZOMB
            }
            return false;
        }
    }
}
