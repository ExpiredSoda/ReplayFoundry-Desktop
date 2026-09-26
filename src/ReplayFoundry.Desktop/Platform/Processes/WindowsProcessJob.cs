using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ReplayFoundry.Desktop.Platform.Processes;

internal sealed class WindowsProcessJob : IDisposable
{
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;

    private readonly SafeFileHandle _handle;

    private WindowsProcessJob(
        SafeFileHandle handle)
    {
        _handle = handle;
    }

    public static WindowsProcessJob CreateKillOnClose()
    {
        var handle =
            new SafeFileHandle(
                CreateJobObject(
                    IntPtr.Zero,
                    null),
                ownsHandle: true);

        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            handle.Dispose();

            throw new Win32Exception(
                error,
                "Windows could not create the protected process job.");
        }

        var information =
            new JobObjectExtendedLimitInformation
            {
                BasicLimitInformation =
                    new JobObjectBasicLimitInformation
                    {
                        LimitFlags =
                            JobObjectLimitKillOnJobClose,
                    },
            };

        if (!SetInformationJobObject(
                handle,
                JobObjectInformationClass.ExtendedLimitInformation,
                ref information,
                (uint)Marshal.SizeOf<JobObjectExtendedLimitInformation>()))
        {
            int error = Marshal.GetLastWin32Error();
            handle.Dispose();

            throw new Win32Exception(
                error,
                "Windows could not configure the protected process job.");
        }

        return new WindowsProcessJob(
            handle);
    }

    public void Assign(
        Process process)
    {
        ArgumentNullException.ThrowIfNull(process);

        if (!AssignProcessToJobObject(
                _handle,
                process.SafeHandle))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Windows could not assign the process to its protected job.");
        }
    }

    public void TryTerminate()
    {
        if (_handle.IsClosed ||
            _handle.IsInvalid)
        {
            return;
        }

        _ = TerminateJobObject(
            _handle,
            1);
    }

    public void Dispose()
    {
        _handle.Dispose();
    }

    private enum JobObjectInformationClass
    {
        ExtendedLimitInformation = 9,
    }

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

    [DllImport(
        "kernel32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern IntPtr CreateJobObject(
        IntPtr jobAttributes,
        string? name);

    [DllImport(
        "kernel32.dll",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(
        SafeFileHandle job,
        JobObjectInformationClass informationClass,
        ref JobObjectExtendedLimitInformation information,
        uint informationLength);

    [DllImport(
        "kernel32.dll",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(
        SafeFileHandle job,
        SafeProcessHandle process);

    [DllImport(
        "kernel32.dll",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateJobObject(
        SafeFileHandle job,
        uint exitCode);
}
