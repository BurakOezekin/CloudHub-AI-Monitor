using System.ComponentModel;
using System.Runtime.InteropServices;

internal sealed record SystemMeasurement(decimal CpuPercent, decimal RamPercent, decimal DiskUsedPercent);

internal static class WindowsMetrics
{
    internal static async Task<SystemMeasurement> SampleAsync()
    {
        var start = ReadCpuTimes();
        await Task.Delay(TimeSpan.FromSeconds(1));
        var end = ReadCpuTimes();

        // Windows zählt Leerlauf zur Kernelzeit. Nur die Differenzen im Messfenster zählen.
        long total = end.Total - start.Total;
        long idle = end.Idle - start.Idle;
        if (total <= 0 || idle < 0 || idle > total)
            throw new IOException("Die CPU-Zähler liefern kein gültiges Messfenster.");

        decimal cpu = Math.Round(100m * (total - idle) / total, 1);

        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (!GlobalMemoryStatusEx(ref memory))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        var systemDrive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
        long capacity = systemDrive.TotalSize;
        if (capacity == 0)
            throw new IOException("Das Systemlaufwerk meldet keine Kapazität.");

        decimal diskUsed = Math.Round(100m * (capacity - systemDrive.TotalFreeSpace) / capacity, 1);
        return new SystemMeasurement(cpu, memory.MemoryLoad, diskUsed);
    }

    private static (long Idle, long Total) ReadCpuTimes()
    {
        if (!GetSystemTimes(out long idle, out long kernel, out long user))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return (idle, kernel + user);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

    // Das Layout entspricht der Windows-Struktur MEMORYSTATUSEX.
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }
}
