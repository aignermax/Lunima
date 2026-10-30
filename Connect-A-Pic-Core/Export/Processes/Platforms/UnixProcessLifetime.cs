using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CAP_Core.Export.Processes;

internal sealed class UnixProcessLifetime : IProcessLifetime
{
    public int Id { get; }
    public Task<int> Exit { get; }
    private bool _reaped;

    private UnixProcessLifetime(int id)
    {
        Id = id;
        Exit = Task.Run(ObserveExitAsync);
    }

    internal static IProcessLifetime Start(ProcessStartInfo info, OwnedProcessPipes pipes) =>
        new UnixProcessLifetime(UnixProcessLauncher.Start(info, pipes));

    private async Task<int> ObserveExitAsync()
    {
        using var memory = new NativeLaunchMemory();
        var info = memory.Allocate(UnixProcessNative.SignalInfoBytes);
        while (true)
        {
            Marshal.WriteInt32(info, UnixProcessNative.SignalInfoPidOffset, 0);
            if (UnixProcessNative.waitid(1, (uint)Id, info, UnixProcessNative.WaitOptions) != 0)
            {
                var error = Marshal.GetLastPInvokeError();
                if (error != UnixProcessNative.Interrupted) throw new Win32Exception(error);
            }
            else if (Marshal.ReadInt32(info, UnixProcessNative.SignalInfoPidOffset) == Id)
            {
                var code = Marshal.ReadInt32(info, 8);
                var status = Marshal.ReadInt32(info, UnixProcessNative.SignalInfoStatusOffset);
                if (code == UnixProcessNative.ChildExited) return status;
                if (code is UnixProcessNative.ChildKilled or UnixProcessNative.ChildDumped) return 128 + status;
            }
            await Task.Delay(10).ConfigureAwait(false);
        }
    }

    public void Terminate() => UnixProcessGroup.Kill(Id);

    public async Task ReapAsync(CancellationToken cancellationToken)
    {
        // WNOWAIT keeps the original PID reserved until every ordinary group member has stopped.
        // Only then can the PGID safely be forgotten, without signalling a recycled numeric identity.
        try { await UnixProcessGroup.WaitForTerminationAsync(Id, cancellationToken).ConfigureAwait(false); }
        finally { ReapRoot(); }
    }

    private void ReapRoot()
    {
        if (_reaped || !Exit.IsCompletedSuccessfully) return;
        while (UnixProcessNative.waitpid(Id, out _, 0) < 0)
        {
            var error = Marshal.GetLastPInvokeError();
            if (error != UnixProcessNative.Interrupted) throw new Win32Exception(error);
        }
        _reaped = true;
    }

    public void Dispose() => ReapRoot();
}
