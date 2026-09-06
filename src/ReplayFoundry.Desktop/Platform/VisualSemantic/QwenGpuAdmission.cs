using System.IO;
using System.Runtime.InteropServices;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

/// <summary>Reads NVML without allocating a CUDA context or loading model weights.</summary>
internal static class QwenGpuAdmission
{
    private static readonly object Gate = new();
    internal static string DescribeReadiness()
    {
        long required = Qwen3VlGroundedMemoryPolicy.ReservedAllocatorHeadroomBytes +
            Qwen3VlGroundedMemoryPolicy.MinimumViableAllocatorLimitBytes;
        return TryReadSingleDevice(out ulong free)
            ? $"GPU memory: {free / (1024d * 1024 * 1024):F2} GiB free; title writing requires {required / (1024d * 1024 * 1024):F2} GiB. " +
                (free >= (ulong)required ? "Memory check passes now; generation checks again before starting." : "Free GPU memory before starting local AI title writing.")
            : "GPU free memory could not be read unambiguously. The local AI runtime checks memory before loading its model.";
    }
    internal static string? GetBlockingReason()
    {
        long required = Qwen3VlGroundedMemoryPolicy.ReservedAllocatorHeadroomBytes +
            Qwen3VlGroundedMemoryPolicy.MinimumViableAllocatorLimitBytes;
        if (!TryReadSingleDevice(out ulong free) || free >= (ulong)required) return null;
        return $"Local AI title writing needs at least {required / (1024d * 1024 * 1024):F1} GiB of free GPU memory; " +
            $"this GPU currently has {free / (1024d * 1024 * 1024):F1} GiB free. " +
            "Free GPU memory and retry, or choose heuristic title writing. Your source and edits remain available.";
    }

    private static bool TryReadSingleDevice(out ulong free)
    {
        free = 0;
        if (!OperatingSystem.IsWindows()) return false;
        lock (Gate)
        {
            if (!NativeLibrary.TryLoad(Path.Combine(Environment.SystemDirectory, "nvml.dll"), out nint library)) return false;
            bool initialized = false;
            try
            {
                initialized = Function<Initialize>(library, "nvmlInit_v2")() == 0;
                if (!initialized || Function<DeviceCount>(library, "nvmlDeviceGetCount_v2")(out uint count) != 0 || count != 1)
                    return false; // Do not confuse NVML ordering with CUDA ordering on a multiple-GPU system.
                if (Function<DeviceHandle>(library, "nvmlDeviceGetHandleByIndex_v2")(0, out nint device) != 0 ||
                    Function<MemoryInfo>(library, "nvmlDeviceGetMemoryInfo")(device, out DeviceMemory memory) != 0 || memory.Free > memory.Total)
                    return false;
                free = memory.Free;
                return true;
            }
            catch (Exception error) when (error is EntryPointNotFoundException or ArgumentException) { return false; }
            finally
            {
                if (initialized && NativeLibrary.TryGetExport(library, "nvmlShutdown", out nint shutdown))
                    Marshal.GetDelegateForFunctionPointer<Initialize>(shutdown)();
                NativeLibrary.Free(library);
            }
        }
    }
    private static T Function<T>(nint library, string name) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));
    [StructLayout(LayoutKind.Sequential)] private struct DeviceMemory { public ulong Total; public ulong Free; public ulong Used; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Initialize();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DeviceCount(out uint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DeviceHandle(uint index, out nint device);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int MemoryInfo(nint device, out DeviceMemory memory);
}
