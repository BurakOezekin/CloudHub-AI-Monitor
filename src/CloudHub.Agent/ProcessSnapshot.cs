using System.ComponentModel;
using System.Diagnostics;

internal sealed record ProcessReading(int Pid, string Name, decimal CpuPercent, decimal RamMb);

internal sealed class ProcessSnapshot : IDisposable
{
    private readonly long started = Stopwatch.GetTimestamp();
    private readonly List<(Process Process, TimeSpan Cpu, DateTime Start)> baseline = [];

    internal ProcessSnapshot()
    {
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.Id == 0) { process.Dispose(); continue; }
                baseline.Add((process, process.TotalProcessorTime, process.StartTime));
            }
            catch (Exception error) when (error is Win32Exception or InvalidOperationException or NotSupportedException)
            {
                process.Dispose();
            }
        }
    }

    internal ProcessReading[] Finish()
    {
        double elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var readings = new List<ProcessReading>();
        foreach (var item in baseline)
        {
            try
            {
                var process = item.Process;
                process.Refresh();
                if (process.HasExited || process.StartTime != item.Start) continue;
                double busy = (process.TotalProcessorTime - item.Cpu).TotalMilliseconds;
                decimal cpu = Math.Round((decimal)Math.Clamp(100 * busy / elapsed / Environment.ProcessorCount, 0, 100), 1);
                decimal ram = Math.Round(process.WorkingSet64 / 1048576m, 1);
                readings.Add(new ProcessReading(process.Id, process.ProcessName, cpu, ram));
            }
            catch (Exception error) when (error is Win32Exception or InvalidOperationException or NotSupportedException)
            {
                // Prozesse können während des Messfensters enden oder geschützt sein.
            }
        }
        return readings.OrderByDescending(item => item.CpuPercent).Take(5)
            .UnionBy(readings.OrderByDescending(item => item.RamMb).Take(5), item => item.Pid)
            .ToArray();
    }

    public void Dispose()
    {
        foreach (var item in baseline) item.Process.Dispose();
    }
}
