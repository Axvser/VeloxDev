using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace VeloxDev.AT.Engine;

/// <summary>
/// A Windows job object whose members are killed when its last handle closes.
/// </summary>
/// <remarks>
/// The demos are launched as separate processes. Without this, a test host that dies mid-run leaves them on the
/// desktop, where they keep a foreground window and an automation tree alive and quietly poison the next run.
/// <see cref="DesktopProcessHost"/> keeps a pid list as well, because assigning to a job can fail for reasons that
/// are not the tests' to fix — the host already living inside a job that forbids breakaway, for one.
/// </remarks>
internal sealed class JobObject : IDisposable
{
    private const uint LimitKillOnJobClose = 0x2000;
    private const int ExtendedLimitInformationClass = 9;

    private IntPtr _handle;

    private JobObject(IntPtr handle) => _handle = handle;

    /// <summary>Create a job that terminates everything in it once the last handle to it is closed.</summary>
    internal static JobObject CreateKillOnClose()
    {
        var handle = CreateJobObject(IntPtr.Zero, null);
        if (handle == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateJobObject failed.");

        var limits = new JobObjectExtendedLimitInformation
        {
            BasicLimitInformation = new JobObjectBasicLimitInformation { LimitFlags = LimitKillOnJobClose },
        };

        if (!SetInformationJobObject(handle, ExtendedLimitInformationClass, ref limits, (uint)Marshal.SizeOf<JobObjectExtendedLimitInformation>()))
        {
            var error = Marshal.GetLastWin32Error();
            CloseHandle(handle);
            throw new Win32Exception(error, "SetInformationJobObject(JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE) failed.");
        }

        return new JobObject(handle);
    }

    /// <summary>
    /// Put a process in the job. Reports failure instead of throwing: the pid list still covers this process, and a
    /// suite that cannot use a job object should still run.
    /// </summary>
    internal bool Assign(Process process)
    {
        if (_handle == IntPtr.Zero) return false;

        try
        {
            return AssignProcessToJobObject(_handle, process.Handle);
        }
        catch (InvalidOperationException)
        {
            // 进程已经退出，无需再管。
            return false;
        }
    }

    /// <summary>Terminate every process in the job, whether or not it is one this run launched.</summary>
    internal void Terminate()
    {
        var handle = _handle;
        if (handle != IntPtr.Zero) TerminateJobObject(handle, 1);
    }

    public void Dispose()
    {
        // 关闭句柄本身就是拆除手段：KILL_ON_JOB_CLOSE 会把作业里剩下的进程全部结束。
        var handle = Interlocked.Exchange(ref _handle, IntPtr.Zero);
        if (handle != IntPtr.Zero) CloseHandle(handle);
        GC.SuppressFinalize(this);
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateJobObjectW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr job, int informationClass, ref JobObjectExtendedLimitInformation information, uint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateJobObject(IntPtr job, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }
}
