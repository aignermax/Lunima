using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace CAP_Core.Export.Processes;

internal sealed class WindowsProcessLifetime : IProcessLifetime
{
    private readonly SafeFileHandle _job;
    private readonly SafeProcessHandle _process;
    public int Id { get; }
    public Task<int> Exit { get; }

    private WindowsProcessLifetime(SafeFileHandle job, SafeProcessHandle process, int id)
    {
        _job = job;
        _process = process;
        Id = id;
        Exit = Task.Run(ObserveExitAsync);
    }

    internal static IProcessLifetime Start(ProcessStartInfo info, OwnedProcessPipes pipes)
    {
        var job = WindowsProcessNative.CreateJobObjectW(0, null);
        SafeProcessHandle? process = null;
        try
        {
            if (job.IsInvalid) throw new Win32Exception(Marshal.GetLastPInvokeError());
            var limits = new WindowsProcessNative.ExtendedLimitInformation
            { Basic = new() { Flags = WindowsProcessNative.KillOnJobClose } };
            Check(WindowsProcessNative.SetInformationJobObject(job, 9, ref limits,
                (uint)Marshal.SizeOf<WindowsProcessNative.ExtendedLimitInformation>()));
            var started = StartSuspended(info, pipes);
            process = new SafeProcessHandle(started.Process, ownsHandle: true);
            using var thread = new SafeFileHandle(started.Thread, ownsHandle: true);
            // No tool instruction runs until the job owns it, so even an immediate fork/exit is contained.
            Check(WindowsProcessNative.AssignProcessToJobObject(job, process));
            if (WindowsProcessNative.ResumeThread(thread) == uint.MaxValue)
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            return new WindowsProcessLifetime(job, process, started.ProcessId);
        }
        catch
        {
            if (process is { IsInvalid: false })
            {
                WindowsProcessNative.TerminateProcess(process, 1);
                WindowsProcessNative.WaitForSingleObject(process, 5000);
                process.Dispose();
            }
            job.Dispose();
            throw;
        }
    }

    private static WindowsProcessNative.ProcessInformation StartSuspended(ProcessStartInfo info, OwnedProcessPipes pipes)
    {
        nuint attributeBytes = 0;
        WindowsProcessNative.InitializeProcThreadAttributeList(0, 1, 0, ref attributeBytes);
        var attributes = Marshal.AllocHGlobal(checked((int)attributeBytes));
        var handleList = Marshal.AllocHGlobal(3 * nint.Size);
        var environment = Marshal.StringToHGlobalUni(string.Join('\0', info.Environment
            .Where(pair => pair.Value != null)
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => pair.Key + "=" + pair.Value)) + "\0\0");
        var initialized = false;
        try
        {
            Check(WindowsProcessNative.InitializeProcThreadAttributeList(attributes, 1, 0, ref attributeBytes));
            initialized = true;
            Marshal.Copy(pipes.ChildHandles, 0, handleList, 3);
            Check(WindowsProcessNative.UpdateProcThreadAttribute(attributes, 0,
                WindowsProcessNative.HandleListAttribute, handleList, (nuint)(3 * nint.Size), 0, 0));
            var startup = new WindowsProcessNative.StartupInfoEx
            {
                Startup = new()
                {
                    Size = (uint)Marshal.SizeOf<WindowsProcessNative.StartupInfoEx>(),
                    Flags = WindowsProcessNative.UseStandardHandles,
                    Input = pipes.ChildInput, Output = pipes.ChildOutput, Error = pipes.ChildError
                },
                Attributes = attributes
            };
            var flags = WindowsProcessNative.CreateSuspended | WindowsProcessNative.UnicodeEnvironment |
                WindowsProcessNative.ExtendedStartupInfo | WindowsProcessNative.CreateNoWindow;
            Check(WindowsProcessNative.CreateProcessW(null, new StringBuilder(ProcessArguments.WindowsCommandLine(info)),
                0, 0, true, flags, environment, string.IsNullOrEmpty(info.WorkingDirectory) ? null : info.WorkingDirectory,
                ref startup, out var process));
            return process;
        }
        finally
        {
            if (initialized) WindowsProcessNative.DeleteProcThreadAttributeList(attributes);
            Marshal.FreeHGlobal(attributes);
            Marshal.FreeHGlobal(handleList);
            Marshal.FreeHGlobal(environment);
        }
    }

    private async Task<int> ObserveExitAsync()
    {
        while (true)
        {
            var status = WindowsProcessNative.WaitForSingleObject(_process, 0);
            if (status == 0)
            {
                Check(WindowsProcessNative.GetExitCodeProcess(_process, out var exitCode));
                return unchecked((int)exitCode);
            }
            if (status == uint.MaxValue) throw new Win32Exception(Marshal.GetLastPInvokeError());
            await Task.Delay(10).ConfigureAwait(false);
        }
    }

    public void Terminate() => Check(WindowsProcessNative.TerminateJobObject(_job, 1));

    public async Task ReapAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            Check(WindowsProcessNative.QueryInformationJobObject(_job, 1, out var accounting,
                (uint)Marshal.SizeOf<WindowsProcessNative.BasicAccountingInformation>(), 0));
            if (accounting.ActiveProcesses == 0) return;
            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        _job.Dispose();
        _process.Dispose();
    }

    private static void Check(bool success)
    {
        if (!success) throw new Win32Exception(Marshal.GetLastPInvokeError());
    }
}
