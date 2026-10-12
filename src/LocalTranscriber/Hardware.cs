using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using Microsoft.Win32;

namespace LocalTranscriber;

public static class Hardware
{
    [StructLayout(LayoutKind.Sequential)] private struct MemoryStatus { public uint Length, Load; public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual; }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetLogicalProcessorInformationEx(int relationship, IntPtr buffer, ref uint length);
    public static (long Total, long Free) Memory() { var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() }; if (!GlobalMemoryStatusEx(ref status)) throw new System.ComponentModel.Win32Exception(); return ((long)status.TotalPhysical, (long)status.AvailablePhysical); }
    public static string Cpu => Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString", "Unknown CPU")?.ToString()?.Trim() ?? "Unknown CPU";
    public static int? PhysicalCores()
    {
        uint length = 0; GetLogicalProcessorInformationEx(0, IntPtr.Zero, ref length); if (length == 0) return null;
        var buffer = Marshal.AllocHGlobal((int)length);
        try { if (!GetLogicalProcessorInformationEx(0, buffer, ref length)) return null; var count = 0; var offset = 0; while (offset + 8 <= length) { var size = Marshal.ReadInt32(buffer, offset + 4); if (size < 8 || offset + size > length) return null; if (Marshal.ReadInt32(buffer, offset) == 0) count++; offset += size; } return count; }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    public static string[] Gpus()
    {
        using var root = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Video"); if (root == null) return [];
        var names = new List<string>(); foreach (var name in root.GetSubKeyNames()) { using var device = root.OpenSubKey(name + @"\0000"); if (device?.GetValue("DriverDesc") is string description && description.Length > 0) names.Add(description); }
        return names.Distinct().ToArray();
    }
    public static bool Compatible => Environment.Is64BitProcess && Avx2.IsSupported;
    public static string Rating(Model model) => !model.Available ? "Unavailable" : Memory().Free < (model.EstimatedMemoryMb + 2000) * 1048576L ? "May exceed available memory — choose a smaller model" : model.EstimatedMemoryMb > 6000 ? "Supported; larger models may be slow on CPU" : "Recommended memory headroom";
    public static int Context(Model model)
    {
        var headroom = Memory().Free / 1048576 - model.EstimatedMemoryMb - 2500;
        return headroom < 1000 ? 2048 : 4096; // Conservative until this architecture's KV budget is verified.
    }
}
