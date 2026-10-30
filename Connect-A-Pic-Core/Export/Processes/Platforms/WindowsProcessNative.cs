using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace CAP_Core.Export.Processes;

internal static class WindowsProcessNative
{
    internal const uint CreateSuspended = 0x4;
    internal const uint UnicodeEnvironment = 0x400;
    internal const uint ExtendedStartupInfo = 0x80000;
    internal const uint CreateNoWindow = 0x08000000;
    internal const uint KillOnJobClose = 0x2000;
    internal const uint UseStandardHandles = 0x100;
    internal const nuint HandleListAttribute = 0x20002;
    internal const uint Infinite = 0xffffffff;

    [StructLayout(LayoutKind.Sequential)]
    internal struct StartupInfo
    {
        internal uint Size;
        internal nint Reserved, Desktop, Title;
        internal uint X, Y, XSize, YSize, XChars, YChars, Fill, Flags;
        internal ushort ShowWindow, ReservedSize;
        internal nint ReservedBytes, Input, Output, Error;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct StartupInfoEx
    {
        internal StartupInfo Startup;
        internal nint Attributes;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct ProcessInformation
    {
        internal nint Process, Thread;
        internal int ProcessId, ThreadId;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct BasicLimitInformation
    {
        internal long ProcessTime, JobTime;
        internal uint Flags;
        internal nuint MinimumWorkingSet, MaximumWorkingSet;
        internal uint ActiveProcessLimit;
        internal nuint Affinity;
        internal uint PriorityClass, SchedulingClass;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct IoCounters
    {
        internal ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct ExtendedLimitInformation
    {
        internal BasicLimitInformation Basic;
        internal IoCounters Io;
        internal nuint ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemory, PeakJobMemory;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct BasicAccountingInformation
    {
        internal long UserTime, KernelTime, PeriodUserTime, PeriodKernelTime;
        internal uint PageFaults, TotalProcesses, ActiveProcesses, TerminatedProcesses;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern SafeFileHandle CreateJobObjectW(nint attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetInformationJobObject(SafeFileHandle job, int informationClass,
        ref ExtendedLimitInformation information, uint size);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool QueryInformationJobObject(SafeFileHandle job, int informationClass,
        out BasicAccountingInformation information, uint size, nint returnLength);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AssignProcessToJobObject(SafeFileHandle job, SafeProcessHandle process);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool TerminateJobObject(SafeFileHandle job, uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool InitializeProcThreadAttributeList(nint list, int count, uint flags, ref nuint size);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UpdateProcThreadAttribute(nint list, uint flags, nuint attribute,
        nint value, nuint size, nint previousValue, nint returnSize);
    [DllImport("kernel32.dll")]
    internal static extern void DeleteProcThreadAttributeList(nint list);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CreateProcessW(string? applicationName, StringBuilder commandLine,
        nint processAttributes, nint threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint flags, nint environment, string? directory, ref StartupInfoEx startup,
        out ProcessInformation process);
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint ResumeThread(SafeFileHandle thread);
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint WaitForSingleObject(SafeProcessHandle process, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetExitCodeProcess(SafeProcessHandle process, out uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool TerminateProcess(SafeProcessHandle process, uint exitCode);
}
